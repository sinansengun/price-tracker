class AppNotification {
  final int id;
  final String title;
  final String body;
  final DateTime sentAt;
  final bool isSuccess;
  final String? error;
  final double? oldPrice;
  final double? newPrice;
  final int? productId;
  final int? userProductId;

  const AppNotification({
    required this.id,
    required this.title,
    required this.body,
    required this.sentAt,
    required this.isSuccess,
    this.error,
    this.oldPrice,
    this.newPrice,
    this.productId,
    this.userProductId,
  });

  factory AppNotification.fromJson(Map<String, dynamic> json) {
    return AppNotification(
      id: (json['id'] as num).toInt(),
      title: (json['title'] as String?) ?? '',
      body: (json['body'] as String?) ?? '',
      sentAt: DateTime.parse(json['sentAt'] as String).toLocal(),
      isSuccess: (json['isSuccess'] as bool?) ?? false,
      error: json['error'] as String?,
      oldPrice: (json['oldPrice'] as num?)?.toDouble(),
      newPrice: (json['newPrice'] as num?)?.toDouble(),
      productId: (json['productId'] as num?)?.toInt(),
      userProductId: (json['userProductId'] as num?)?.toInt(),
    );
  }
}
