import 'dart:async';
import 'dart:convert';
import 'package:flutter/foundation.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;
import '../api/api_client.dart';
import '../services/analytics_service.dart';

class AuthProvider extends ChangeNotifier {
  static const _storage = FlutterSecureStorage();
  static const _tokenKey = 'jwt_token';

  String? _token;
  bool _loading = false;
  String? _error;

  bool get isAuthenticated => _token != null;
  bool get loading => _loading;
  String? get error => _error;

  AuthProvider() {
    _loadStoredToken();
  }

  Future<void> _loadStoredToken() async {
    final t = await _storage.read(key: _tokenKey);
    if (t != null) {
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

  Future<void> logout() async {
    await _storage.delete(key: _tokenKey);
    _token = null;
    ApiClient.setToken(null);
    notifyListeners();
  }

  // Login/restore sonrası çağrılacak callback (FCM token kaydı için)
  Future<void> Function()? onAuthenticated;

  Future<void> _saveToken(String token) async {
    _token = token;
    ApiClient.setToken(token);
    await _storage.write(key: _tokenKey, value: token);
    onAuthenticated?.call();
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
