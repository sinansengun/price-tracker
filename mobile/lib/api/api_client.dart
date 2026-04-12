import 'dart:convert';
import 'package:http/http.dart' as http;

class ApiClient {
  static const String baseUrl = 'https://price-tracker-api.up.railway.app/api';

  // Token depolamak için basit in-memory slot (AuthProvider tarafından set edilir)
  static String? _token;
  static void setToken(String? token) => _token = token;

  static Map<String, String> get _headers => {
        'Content-Type': 'application/json',
        if (_token != null) 'Authorization': 'Bearer $_token',
      };

  static Uri _uri(String path) => Uri.parse('$baseUrl$path');

  // Export için
  static Map<String, String> get headers => _headers;
  static Uri Function(String) get uri => _uri;

  static Future<void> updateDeviceToken(String fcmToken) async {
    try {
      await http.put(
        _uri('/auth/device-token'),
        headers: _headers,
        body: jsonEncode(fcmToken),
      );
    } catch (_) {
      // Bildirim token kaydı kritik değil, sessizce geç
    }
  }
}
