import 'dart:async';
import 'dart:convert';
import 'package:firebase_auth/firebase_auth.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:google_sign_in/google_sign_in.dart';
import 'package:http/http.dart' as http;
import '../api/api_client.dart';
import '../services/analytics_service.dart';

class AuthProvider extends ChangeNotifier {
  static const _storage = FlutterSecureStorage();
  static const _tokenKey = 'jwt_token';

  String? _token;
  bool _loading = false;
  String? _error;

  final GoogleSignIn _googleSignIn = GoogleSignIn.instance;
  bool _googleInitialized = false;

  bool get isAuthenticated => _token != null;
  bool get loading => _loading;
  String? get error => _error;

  AuthProvider() {
    ApiClient.onUnauthorized = _handleUnauthorized;
    _loadStoredToken();
  }

  @override
  void dispose() {
    if (ApiClient.onUnauthorized == _handleUnauthorized) {
      ApiClient.onUnauthorized = null;
    }
    super.dispose();
  }

  Future<void> _loadStoredToken() async {
    final t = await _storage.read(key: _tokenKey);
    if (t != null) {
      if (_isJwtExpired(t)) {
        await _storage.delete(key: _tokenKey);
        ApiClient.setToken(null);
        return;
      }

      _token = t;
      ApiClient.setToken(t);
      notifyListeners();
      onAuthenticated?.call();
    }
  }

  Future<bool> login(String email, String password) async {
    _loading = true;
    _error = null;
    notifyListeners();

    try {
      final res = await http.post(
        ApiClient.uri('/auth/login'),
        headers: {'Content-Type': 'application/json'},
        body: jsonEncode({'email': email, 'password': password}),
      );

      if (res.statusCode == 200) {
        final token = jsonDecode(res.body)['token'] as String;
        await _saveToken(token);
        unawaited(AnalyticsService.instance.logLoginSuccess());
        return true;
      } else {
        _error = _extractError(res.body, 'E-posta veya şifre hatalı.');
        unawaited(AnalyticsService.instance.logLoginFailed(
          reason: _classifyAuthFailure(statusCode: res.statusCode),
        ));
        return false;
      }
    } catch (_) {
      _error = 'Sunucuya bağlanılamadı.';
      unawaited(AnalyticsService.instance.logLoginFailed(reason: 'network'));
      return false;
    } finally {
      _loading = false;
      notifyListeners();
    }
  }

  Future<bool> register(String email, String password) async {
    _loading = true;
    _error = null;
    notifyListeners();

    try {
      final res = await http.post(
        ApiClient.uri('/auth/register'),
        headers: {'Content-Type': 'application/json'},
        body: jsonEncode({'email': email, 'password': password}),
      );

      if (res.statusCode == 200) {
        final token = jsonDecode(res.body)['token'] as String;
        await _saveToken(token);
        unawaited(AnalyticsService.instance.logSignupSuccess());
        return true;
      } else {
        _error = _extractError(res.body, 'Kayıt başarısız.');
        unawaited(AnalyticsService.instance.logSignupFailed(
          reason: _classifyAuthFailure(statusCode: res.statusCode),
        ));
        return false;
      }
    } catch (_) {
      _error = 'Sunucuya bağlanılamadı.';
      unawaited(AnalyticsService.instance.logSignupFailed(reason: 'network'));
      return false;
    } finally {
      _loading = false;
      notifyListeners();
    }
  }

  Future<bool> loginWithGoogle() async {
    _loading = true;
    _error = null;
    notifyListeners();

    try {
      await _ensureGoogleInitialized();
      final googleUser = await _googleSignIn.authenticate();
      final googleAuth = googleUser.authentication;
      final credential = GoogleAuthProvider.credential(
        idToken: googleAuth.idToken,
      );

      final userCredential = await FirebaseAuth.instance.signInWithCredential(credential);
      final firebaseIdToken = await userCredential.user?.getIdToken();
      if (firebaseIdToken == null || firebaseIdToken.isEmpty) {
        _error = 'Google kimlik doğrulaması tamamlanamadı.';
        return false;
      }

      final res = await http.post(
        ApiClient.uri('/auth/google'),
        headers: {'Content-Type': 'application/json'},
        body: jsonEncode({'firebaseIdToken': firebaseIdToken}),
      );

      if (res.statusCode == 200) {
        final token = jsonDecode(res.body)['token'] as String;
        await _saveToken(token);
        return true;
      }

      _error = _extractError(res.body, 'Google ile giriş başarısız.');
      return false;
    } on GoogleSignInException catch (ex) {
      if (ex.code == GoogleSignInExceptionCode.canceled) {
        return false;
      }
      _error = 'Google ile giriş başarısız.';
      return false;
    } catch (_) {
      _error = 'Google ile giriş başarısız.';
      return false;
    } finally {
      _loading = false;
      notifyListeners();
    }
  }

  Future<void> logout({String? reason}) async {
    await _storage.delete(key: _tokenKey);
    _token = null;
    _error = reason;
    ApiClient.setToken(null);
    try {
      await FirebaseAuth.instance.signOut();
      if (_googleInitialized) {
        await _googleSignIn.signOut();
      }
    } catch (_) {}
    notifyListeners();
  }

  Future<void> _ensureGoogleInitialized() async {
    if (_googleInitialized) return;
    await _googleSignIn.initialize();
    _googleInitialized = true;
  }

  Future<void> _handleUnauthorized() async {
    if (_token == null) return;
    await logout(reason: 'Oturum süresi doldu. Lütfen tekrar giriş yapın.');
  }

  // Login/restore sonrası çağrılacak callback (FCM token kaydı için)
  Future<void> Function()? onAuthenticated;

  Future<void> _saveToken(String token) async {
    _token = token;
    _error = null;
    ApiClient.setToken(token);
    await _storage.write(key: _tokenKey, value: token);
    onAuthenticated?.call();
  }

  bool _isJwtExpired(String token) {
    try {
      final parts = token.split('.');
      if (parts.length != 3) return false;

      final payloadMap = jsonDecode(
        utf8.decode(base64Url.decode(base64Url.normalize(parts[1]))),
      ) as Map<String, dynamic>;

      final exp = payloadMap['exp'];
      if (exp is! num) return false;

      final expTime = DateTime.fromMillisecondsSinceEpoch(
        exp.toInt() * 1000,
        isUtc: true,
      );

      // Saat farklarına karşı küçük tolerans.
      return DateTime.now().toUtc().isAfter(expTime.subtract(const Duration(seconds: 20)));
    } catch (_) {
      return false;
    }
  }

  String _extractError(String body, String fallback) {
    try {
      final j = jsonDecode(body);
      if (j['error'] != null) return j['error'];
      final errs = j['errors'];
      if (errs is List && errs.isNotEmpty) return errs.join('\n');
    } catch (_) {}
    return fallback;
  }

  String _classifyAuthFailure({required int statusCode}) {
    if (statusCode == 400 || statusCode == 401) return 'invalid_credentials';
    if (statusCode == 409) return 'already_exists';
    if (statusCode >= 500) return 'server_error';
    return 'request_rejected';
  }
}
