import 'dart:async';
import 'dart:convert';
import 'package:http/http.dart' as http;

class ApiClient {
  static const String baseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'https://price-tracker-api.up.railway.app/api',
  );

  // Token depolamak için basit in-memory slot (AuthProvider tarafından set edilir)
  static String? _token;
  static void setToken(String? token) {
    _token = token;
    _unauthorizedNotified = false;
  }

  // 401 olduğunda AuthProvider tarafından set edilen callback tetiklenir.
  static Future<void> Function()? onUnauthorized;
  static bool _unauthorizedNotified = false;

  static Map<String, String> get _headers => {
        'Content-Type': 'application/json',
        if (_token != null) 'Authorization': 'Bearer $_token',
      };

  static Uri _uri(String path) => Uri.parse('$baseUrl$path');

  static Future<http.Response> _withUnauthorizedHandling(
    Future<http.Response> Function() request,
  ) async {
    final res = await request();
    if (res.statusCode == 401 && !_unauthorizedNotified) {
      _unauthorizedNotified = true;
      final callback = onUnauthorized;
      if (callback != null) {
        unawaited(callback());
      }
    }
    return res;
  }

  static Future<http.Response> get(String path) {
    return _withUnauthorizedHandling(() => http.get(_uri(path), headers: _headers));
  }

  static Future<http.Response> post(String path, {Object? body}) {
    return _withUnauthorizedHandling(
      () => http.post(_uri(path), headers: _headers, body: body),
    );
  }

  static Future<http.Response> put(String path, {Object? body}) {
    return _withUnauthorizedHandling(
      () => http.put(_uri(path), headers: _headers, body: body),
    );
  }

  static Future<http.Response> patch(String path, {Object? body}) {
    return _withUnauthorizedHandling(
      () => http.patch(_uri(path), headers: _headers, body: body),
    );
  }

  static Future<http.Response> delete(String path, {Object? body}) {
    return _withUnauthorizedHandling(
      () => http.delete(_uri(path), headers: _headers, body: body),
    );
  }

  // Export için
  static Map<String, String> get headers => _headers;
  static Uri Function(String) get uri => _uri;

  static Future<void> updateDeviceToken(String fcmToken) async {
    if (_token == null) return;
    try {
      await put(
        '/auth/device-token',
        body: jsonEncode({'token': fcmToken}),
      );
    } catch (_) {
      // Bildirim token kaydı kritik değil, sessizce geç
    }
  }
}
