import 'dart:convert';
import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;
import '../api/api_client.dart';
import '../models/product.dart';

class ProductsProvider extends ChangeNotifier {
  List<UserProduct> _products = [];
  List<Label> _labels = [];
  bool _loading = false;
  String? _error;

  List<UserProduct> get products => _products;
  List<Label> get labels => _labels;
  bool get loading => _loading;
  String? get error => _error;

  Future<void> fetchAll() async {
    _loading = true;
    _error = null;
    notifyListeners();

    try {
      final results = await Future.wait([
        http.get(ApiClient.uri('/products'), headers: ApiClient.headers),
        http.get(ApiClient.uri('/labels'), headers: ApiClient.headers),
      ]);
      final productsRes = results[0];
      final labelsRes = results[1];
      if (productsRes.statusCode == 200) {
        final list = jsonDecode(productsRes.body) as List<dynamic>;
        _products = list
            .map((e) => UserProduct.fromJson(e as Map<String, dynamic>))
            .toList();
      } else {
        _error = 'Ürünler yüklenemedi.';
      }
      if (labelsRes.statusCode == 200) {
        final list = jsonDecode(labelsRes.body) as List<dynamic>;
        _labels = list
            .map((e) => Label.fromJson(e as Map<String, dynamic>))
            .toList();
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

  // ── Label methods ──────────────────────────────────────────────

  Future<Label?> createLabel(String name, String color) async {
    try {
      final res = await http.post(
        ApiClient.uri('/labels'),
        headers: ApiClient.headers,
        body: jsonEncode({'name': name, 'color': color}),
      );
      if (res.statusCode == 201) {
        final label = Label.fromJson(jsonDecode(res.body) as Map<String, dynamic>);
        _labels = [..._labels, label];
        notifyListeners();
        return label;
      }
    } catch (_) {}
    return null;
  }

  Future<void> deleteLabel(int id) async {
    try {
      await http.delete(ApiClient.uri('/labels/$id'), headers: ApiClient.headers);
      _labels = _labels.where((l) => l.id != id).toList();
      // Remove from all products
      _products = _products.map((up) {
        final newLabels = up.labels.where((l) => l.id != id).toList();
        return UserProduct(
          id: up.id,
          targetPrice: up.targetPrice,
          addedAt: up.addedAt,
          product: up.product,
          labels: newLabels,
        );
      }).toList();
      notifyListeners();
    } catch (_) {}
  }

  Future<bool> addProductLabel(int productId, int labelId) async {
    try {
      final res = await http.post(
        ApiClient.uri('/products/$productId/labels/$labelId'),
        headers: ApiClient.headers,
      );
      if (res.statusCode == 200 || res.statusCode == 204) {
        _updateProductLabels(
            productId, [..._productById(productId)!.labels, _labelById(labelId)!]);
        return true;
      }
    } catch (_) {}
    return false;
  }

  Future<bool> removeProductLabel(int productId, int labelId) async {
    try {
      final res = await http.delete(
        ApiClient.uri('/products/$productId/labels/$labelId'),
        headers: ApiClient.headers,
      );
      if (res.statusCode == 200 || res.statusCode == 204) {
        _updateProductLabels(productId,
            _productById(productId)!.labels.where((l) => l.id != labelId).toList());
        return true;
      }
    } catch (_) {}
    return false;
  }

  UserProduct? _productById(int id) =>
      _products.cast<UserProduct?>().firstWhere((p) => p?.id == id, orElse: () => null);

  Label? _labelById(int id) =>
      _labels.cast<Label?>().firstWhere((l) => l?.id == id, orElse: () => null);

  void _updateProductLabels(int productId, List<Label> newLabels) {
    _products = _products.map((up) {
      if (up.id != productId) return up;
      return UserProduct(
        id: up.id,
        targetPrice: up.targetPrice,
        addedAt: up.addedAt,
        product: up.product,
        labels: newLabels,
      );
    }).toList();
    notifyListeners();
  }
}
