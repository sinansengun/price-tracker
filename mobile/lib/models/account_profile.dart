class AccountProfile {
  final String email;
  final DateTime createdAt;
  final bool hasPassword;

  const AccountProfile({
    required this.email,
    required this.createdAt,
    required this.hasPassword,
  });

  factory AccountProfile.fromJson(Map<String, dynamic> json) {
    return AccountProfile(
      email: (json['email'] as String?) ?? '',
      createdAt: DateTime.parse(json['createdAt'] as String).toLocal(),
      hasPassword: (json['hasPassword'] as bool?) ?? true,
    );
  }
}
