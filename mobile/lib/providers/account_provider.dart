import 'dart:convert';

import 'package:flutter/foundation.dart';

import '../api/api_client.dart';
import '../models/account_profile.dart';

class AccountProvider extends ChangeNotifier {
  AccountProfile? _profile;
  bool _loading = false;
  bool _changingPassword = false;
  String? _error;
  String? _passwordMessage;

  AccountProfile? get profile => _profile;
  bool get loading => _loading;
  bool get changingPassword => _changingPassword;
  String? get error => _error;
  String? get passwordMessage => _passwordMessage;

  Future<void> fetchProfile() async {
    _loading = true;
    _error = null;
    notifyListeners();

    try {
      final res = await ApiClient.get('/auth/me');
      if (res.statusCode == 200) {
        _profile = AccountProfile.fromJson(jsonDecode(res.body) as Map<String, dynamic>);
      } else {
        _error = _extractError(res.body, 'Hesap bilgileri alınamadı.');
      }
    } catch (_) {
      _error = 'Sunucuya bağlanılamadı.';
    } finally {
      _loading = false;
      notifyListeners();
    }
  }

  Future<bool> changePassword({
    required String currentPassword,
    required String newPassword,
  }) async {
    _changingPassword = true;
    _passwordMessage = null;
    _error = null;
    notifyListeners();

    try {
      final res = await ApiClient.put(
        '/auth/change-password',
        body: jsonEncode({
          'currentPassword': currentPassword,
          'newPassword': newPassword,
        }),
      );

      if (res.statusCode == 200) {
        _passwordMessage = 'Şifre güncellendi.';
        _changingPassword = false;
        notifyListeners();
        return true;
      }

      _error = _extractError(res.body, 'Şifre güncellenemedi.');
      _changingPassword = false;
      notifyListeners();
      return false;
    } catch (_) {
      _error = 'Sunucuya bağlanılamadı.';
      _changingPassword = false;
      notifyListeners();
      return false;
    }
  }

  void clearMessages() {
    _error = null;
    _passwordMessage = null;
    notifyListeners();
  }

  String _extractError(String body, String fallback) {
    try {
      final json = jsonDecode(body) as Map<String, dynamic>;
      final error = json['error'];
      if (error is String && error.isNotEmpty) return error;
      final errors = json['errors'];
      if (errors is List && errors.isNotEmpty) {
        return errors.join('\n');
      }
    } catch (_) {}
    return fallback;
  }
}
