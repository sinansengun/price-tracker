const productPriceStatusAvailable = 'available';
const productPriceStatusOutOfStock = 'out_of_stock';
const productPriceStatusPriceNotFound = 'price_not_found';
const userProductAlertModeAutomatic = 'automatic';
const userProductAlertModePercentage = 'percentage';
const userProductAlertModeTargetPrice = 'target_price';

String _formatAlertNumber(double value) {
  final rounded = value.roundToDouble();
  if ((value - rounded).abs() < 0.001) {
    return rounded.toStringAsFixed(0);
  }

  final oneDecimal = double.parse(value.toStringAsFixed(1));
  if ((value - oneDecimal).abs() < 0.001) {
    return oneDecimal.toStringAsFixed(1).replaceAll('.', ',');
  }

  return value.toStringAsFixed(2).replaceAll('.', ',');
}

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
  final String? priceStatus;
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
    this.priceStatus,
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
          priceStatus: j['priceStatus'] as String?,
        lastCheckedAt: j['lastCheckedAt'] != null
            ? DateTime.parse(j['lastCheckedAt']).toLocal()
            : null,
        priceHistories: (j['priceHistories'] as List<dynamic>? ?? [])
            .map((e) => PricePoint.fromJson(e as Map<String, dynamic>))
            .toList(),
      );

        String? get missingPriceLabel {
          if (currentPrice != null) return null;

          return switch (priceStatus) {
        productPriceStatusOutOfStock => 'Stokta yok',
        productPriceStatusPriceNotFound => 'Fiyat bulunamadı',
        _ => lastCheckedAt == null
            ? 'Fiyat kontrolü bekleniyor'
            : 'Fiyat bulunamadı',
          };
        }

        bool get isOutOfStock => priceStatus == productPriceStatusOutOfStock;
}

class UserProduct {
  final int id;
  final String alertMode;
  final double? discountThresholdPercent;
  final double? targetPrice;
  final DateTime addedAt;
  final ProductInfo product;
  final List<Label> labels;

  const UserProduct({
    required this.id,
    this.alertMode = userProductAlertModeAutomatic,
    this.discountThresholdPercent,
    this.targetPrice,
    required this.addedAt,
    required this.product,
    this.labels = const [],
  });

  factory UserProduct.fromJson(Map<String, dynamic> j) => UserProduct(
        id: j['id'],
        alertMode: (j['alertMode'] as String?) ?? userProductAlertModeAutomatic,
        discountThresholdPercent:
            (j['discountThresholdPercent'] as num?)?.toDouble(),
        targetPrice: (j['targetPrice'] as num?)?.toDouble(),
        addedAt: DateTime.parse(j['addedAt']).toLocal(),
        product: ProductInfo.fromJson(j['product'] as Map<String, dynamic>),
        labels: (j['labels'] as List<dynamic>? ?? [])
            .map((e) => Label.fromJson(e as Map<String, dynamic>))
            .toList(),
      );

  String get normalizedAlertMode {
    return switch (alertMode) {
      userProductAlertModePercentage => userProductAlertModePercentage,
      userProductAlertModeTargetPrice => userProductAlertModeTargetPrice,
      _ => userProductAlertModeAutomatic,
    };
  }

  String get alertSummaryLabel {
    return switch (normalizedAlertMode) {
      userProductAlertModePercentage => discountThresholdPercent != null
          ? '%${_formatAlertNumber(discountThresholdPercent!)} indirim alarmı'
          : 'Yüzde indirim alarmı',
      userProductAlertModeTargetPrice => targetPrice != null
          ? '${_formatAlertNumber(targetPrice!)} TL hedef fiyat'
          : 'Hedef fiyat alarmı',
      _ => 'Otomatik takip',
    };
  }

  String get alertButtonLabel {
    return switch (normalizedAlertMode) {
      userProductAlertModePercentage => 'Yüzdeli takip',
      userProductAlertModeTargetPrice => 'Hedef fiyatlı takip',
      _ => 'Otomatik takip',
    };
  }
}
