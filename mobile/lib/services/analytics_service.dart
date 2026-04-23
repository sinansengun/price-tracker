import 'package:firebase_analytics/firebase_analytics.dart';
import 'package:flutter/foundation.dart';

class AnalyticsService {
  AnalyticsService._();

  static final AnalyticsService instance = AnalyticsService._();

  FirebaseAnalytics? _analytics;

  Future<void> initialize() async {
    _analytics ??= FirebaseAnalytics.instance;
    await _analytics!.setAnalyticsCollectionEnabled(true);
    await _analytics!.setDefaultEventParameters({
      'app_platform': defaultTargetPlatform.name,
    });
  }

  Future<void> logLoginAttempt({required bool hasEmailLikeInput}) {
    return _logEvent('login_attempt', {
      'auth_method': 'email_password',
      'has_email_like_input': hasEmailLikeInput,
    });
  }

  Future<void> logLoginSuccess() {
    return _logEvent('login_success', {
      'auth_method': 'email_password',
    });
  }

  Future<void> logLoginFailed({required String reason}) {
    return _logEvent('login_failed', {
      'auth_method': 'email_password',
      'reason': _sanitizeReason(reason),
    });
  }

  Future<void> logSignupAttempt({required bool hasEmailLikeInput}) {
    return _logEvent('signup_attempt', {
      'auth_method': 'email_password',
      'has_email_like_input': hasEmailLikeInput,
    });
  }

  Future<void> logSignupSuccess() {
    return _logEvent('signup_success', {
      'auth_method': 'email_password',
    });
  }

  Future<void> logSignupFailed({required String reason}) {
    return _logEvent('signup_failed', {
      'auth_method': 'email_password',
      'reason': _sanitizeReason(reason),
    });
  }

  Future<void> logProductsScreenViewed({required int productCount}) {
    return _logEvent('products_screen_viewed', {
      'product_count_bucket': _bucketCount(productCount),
    });
  }

  Future<void> logProductDetailViewed({
    required bool hasTargetPrice,
    required int labelCount,
    required int historyPointCount,
  }) {
    return _logEvent('product_detail_viewed', {
      'has_target_price': hasTargetPrice,
      'label_count_bucket': _bucketCount(labelCount),
      'history_count_bucket': _bucketCount(historyPointCount),
    });
  }

  Future<void> logAddProductOpened({required String source, String? url}) {
    return _logEvent('add_product_opened', {
      'source': _sanitizeReason(source),
      'has_prefilled_url': (url ?? '').trim().isNotEmpty,
      'url_domain_group': _urlDomainGroup(url),
    });
  }

  Future<void> logAddProductSubmitted({
    required String url,
    required bool hasTargetPrice,
  }) {
    return _logEvent('add_product_submitted', {
      'has_target_price': hasTargetPrice,
      'url_domain_group': _urlDomainGroup(url),
    });
  }

  Future<void> logAddProductSuccess({required bool hasTargetPrice}) {
    return _logEvent('add_product_success', {
      'has_target_price': hasTargetPrice,
    });
  }

  Future<void> logAddProductFailed({required String reason}) {
    return _logEvent('add_product_failed', {
      'reason': _sanitizeReason(reason),
    });
  }

  Future<void> logPushPermissionResult({required String status}) {
    return _logEvent('push_permission_result', {
      'status': _sanitizeReason(status),
    });
  }

  Future<void> logPushForegroundReceived({
    required bool hasNotification,
    required bool hasData,
  }) {
    return _logEvent('push_foreground_received', {
      'has_notification': hasNotification,
      'has_data': hasData,
    });
  }

  Future<void> logPushOpenedFromBackground({required String source}) {
    return _logEvent('push_opened_from_background', {
      'source': _sanitizeReason(source),
    });
  }

  Future<void> _logEvent(String name, Map<String, Object?> rawParams) async {
    final analytics = _analytics;
    if (analytics == null) return;

    final sanitized = <String, Object>{};
    rawParams.forEach((key, value) {
      if (value == null) return;
      final sanitizedKey = _sanitizeKey(key);
      if (sanitizedKey.isEmpty) return;

      if (value is String) {
        sanitized[sanitizedKey] = _sanitizeValue(value);
      } else if (value is bool) {
        // Firebase Analytics accepts only String or num parameter values.
        sanitized[sanitizedKey] = value ? 1 : 0;
      } else if (value is num) {
        sanitized[sanitizedKey] = value;
      } else {
        sanitized[sanitizedKey] = _sanitizeValue(value.toString());
      }
    });

    try {
      await analytics.logEvent(name: _sanitizeKey(name), parameters: sanitized);
    } catch (e) {
      debugPrint('Analytics logEvent failed for $name: $e');
    }
  }

  String _sanitizeKey(String value) {
    final normalized = value
        .toLowerCase()
        .replaceAll(RegExp(r'[^a-z0-9_]'), '_')
        .replaceAll(RegExp(r'_+'), '_')
        .replaceAll(RegExp(r'^_|_$'), '');
    return normalized.length > 40 ? normalized.substring(0, 40) : normalized;
  }

  String _sanitizeValue(String value) {
    final normalized = value
        .trim()
        .toLowerCase()
        .replaceAll(RegExp(r'[^a-z0-9_\-]'), '_')
        .replaceAll(RegExp(r'_+'), '_')
        .replaceAll(RegExp(r'^_|_$'), '');
    if (normalized.isEmpty) return 'unknown';
    return normalized.length > 40 ? normalized.substring(0, 40) : normalized;
  }

  String _sanitizeReason(String reason) {
    return _sanitizeValue(reason);
  }

  String _bucketCount(int count) {
    if (count <= 0) return '0';
    if (count <= 2) return '1_2';
    if (count <= 5) return '3_5';
    if (count <= 10) return '6_10';
    return '11_plus';
  }

  String _urlDomainGroup(String? url) {
    if (url == null || url.trim().isEmpty) return 'none';

    final host = Uri.tryParse(url.trim())?.host.toLowerCase() ?? '';
    if (host.isEmpty) return 'invalid';

    if (host.contains('amazon')) return 'amazon';
    if (host.contains('hepsiburada')) return 'hepsiburada';
    if (host.contains('trendyol')) return 'trendyol';
    if (host.contains('n11')) return 'n11';
    if (host.contains('saat')) return 'saat_store';
    if (host.contains('trabzonspor')) return 'trabzonspor';
    return 'other';
  }
}
