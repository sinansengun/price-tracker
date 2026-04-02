class Label {
  final int id;
  final String name;
  final String color;

  const Label({required this.id, required this.name, required this.color});

  factory Label.fromJson(Map<String, dynamic> j) =>
      Label(id: j['id'], name: j['name'], color: j['color'] ?? '#6366f1');
}

class PricePoint {
  final double price;
  final DateTime checkedAt;

  const PricePoint({required this.price, required this.checkedAt});

  factory PricePoint.fromJson(Map<String, dynamic> j) => PricePoint(
        price: (j['price'] as num).toDouble(),
        checkedAt: DateTime.parse(j['checkedAt']).toLocal(),
      );
}

class ProductInfo {
  final int id;
  final String name;
  final String url;
  final String? imageUrl;
  final String? store;
  final double? initialPrice;
  final double? currentPrice;
  final DateTime? lastCheckedAt;
  final List<PricePoint> priceHistories;

  const ProductInfo({
    required this.id,
    required this.name,
    required this.url,
    this.imageUrl,
    this.store,
    this.initialPrice,
    this.currentPrice,
    this.lastCheckedAt,
    this.priceHistories = const [],
  });

  factory ProductInfo.fromJson(Map<String, dynamic> j) => ProductInfo(
        id: j['id'],
        name: j['name'] ?? '',
        url: j['url'] ?? '',
        imageUrl: j['imageUrl'],
        store: j['store'],
        initialPrice: (j['initialPrice'] as num?)?.toDouble(),
        currentPrice: (j['currentPrice'] as num?)?.toDouble(),
        lastCheckedAt: j['lastCheckedAt'] != null
            ? DateTime.parse(j['lastCheckedAt']).toLocal()
            : null,
        priceHistories: (j['priceHistories'] as List<dynamic>? ?? [])
            .map((e) => PricePoint.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}

class UserProduct {
  final int id;
  final double? targetPrice;
  final DateTime addedAt;
  final ProductInfo product;
  final List<Label> labels;

  const UserProduct({
    required this.id,
    this.targetPrice,
    required this.addedAt,
    required this.product,
    this.labels = const [],
  });

  factory UserProduct.fromJson(Map<String, dynamic> j) => UserProduct(
        id: j['id'],
        targetPrice: (j['targetPrice'] as num?)?.toDouble(),
        addedAt: DateTime.parse(j['addedAt']).toLocal(),
        product: ProductInfo.fromJson(j['product'] as Map<String, dynamic>),
        labels: (j['labels'] as List<dynamic>? ?? [])
            .map((e) => Label.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}
