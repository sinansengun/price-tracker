import 'dart:convert';

import 'package:flutter/foundation.dart';

import '../api/api_client.dart';
import '../models/app_notification.dart';

class NotificationsProvider extends ChangeNotifier {
  List<AppNotification> _items = [];
  bool _loading = false;
  String? _error;
  int _total = 0;

  List<AppNotification> get items => _items;
  bool get loading => _loading;
  String? get error => _error;
  int get total => _total;

  Future<void> fetch({int page = 1, int pageSize = 50}) async {
    _loading = true;
    _error = null;
    notifyListeners();

    try {
      final res = await ApiClient.get('/notifications?page=$page&pageSize=$pageSize');
      if (res.statusCode == 200) {
        final data = jsonDecode(res.body) as Map<String, dynamic>;
        final list = (data['items'] as List<dynamic>? ?? []);
        _items = list
            .map((e) => AppNotification.fromJson(e as Map<String, dynamic>))
            .toList();
        _total = (data['total'] as num?)?.toInt() ?? _items.length;
      } else {
        _error = 'Bildirimler alınamadı.';
      }
    } catch (_) {
      _error = 'Sunucuya bağlanılamadı.';
    } finally {
      _loading = false;
      notifyListeners();
    }
  }
}
