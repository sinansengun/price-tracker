import 'dart:convert';
import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;
import '../api/api_client.dart';
import '../models/product.dart';

class ProductsProvider extends ChangeNotifier {
  List<UserProduct> _products = [];
  bool _loading = false;
  String? _error;

  List<UserProduct> get products => _products;
  bool get loading => _loading;
  String? get error => _error;

  Future<void> fetchAll() async {
    _loading = true;
    _error = null;
    notifyListeners();

    try {
      final res = await http.get(
        ApiClient.uri('/products'),
        headers: ApiClient.headers,
      );
      if (res.statusCode == 200) {
        final list = jsonDecode(res.body) as List<dynamic>;
        _products = list
            .map((e) => UserProduct.fromJson(e as Map<String, dynamic>))
            .toList();
      } else {
        _error = 'Ürünler yüklenemedi.';
      }
    } catch (_) {
      _error = 'Sunucuya bağlanılamadı.';
    } finally {
      _loading = false;
      notifyListeners();
    }
  }

  Future<UserProduct?> fetchById(int id) async {
    try {
      final res = await http.get(
        ApiClient.uri('/products/$id'),
        headers: ApiClient.headers,
      );
      if (res.statusCode == 200) {
        return UserProduct.fromJson(
            jsonDecode(res.body) as Map<String, dynamic>);
      }
    } catch (_) {}
    return null;
  }

  Future<String?> addProduct(String url,
      {String? name, double? targetPrice}) async {
    try {
      final res = await http.post(
        ApiClient.uri('/products'),
        headers: ApiClient.headers,
        body: jsonEncode({
          'url': url,
          if (name != null) 'name': name,
          if (targetPrice != null) 'targetPrice': targetPrice,
        }),
      );

      if (res.statusCode == 201) {
        await fetchAll();
        return null;
      } else {
        final j = jsonDecode(res.body);
        return j['error'] ?? 'Ürün eklenemedi.';
      }
    } catch (_) {
      return 'Sunucuya bağlanılamadı.';
    }
  }

  Future<void> deleteProduct(int id) async {
    try {
      await http.delete(
        ApiClient.uri('/products/$id'),
        headers: ApiClient.headers,
      );
      _products.removeWhere((p) => p.id == id);
      notifyListeners();
    } catch (_) {}
  }

  Future<String?> updateTargetPrice(int id, double? price) async {
    try {
      final res = await http.patch(
        ApiClient.uri('/products/$id/target-price'),
        headers: ApiClient.headers,
        body: jsonEncode({'targetPrice': price}),
      );
      if (res.statusCode == 200) {
        await fetchAll();
        return null;
      }
      return 'Güncelleme başarısız.';
    } catch (_) {
      return 'Sunucuya bağlanılamadı.';
    }
  }

  Future<void> manualCheck(int id) async {
    try {
      await http.post(
        ApiClient.uri('/products/$id/check'),
        headers: ApiClient.headers,
      );
    } catch (_) {}
  }
}
