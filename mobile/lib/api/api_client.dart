class ApiClient {
  // macOS / iOS simülatörü → localhost
  // Android emülatörü      → 10.0.2.2
  // Fiziksel cihaz          → bilgisayarın yerel IP'si
  static const String baseUrl = 'http://localhost:5254/api';

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
}
