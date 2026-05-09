import 'dart:async';
import 'dart:convert';

import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';
import 'api/api_client.dart';
import 'providers/account_provider.dart';
import 'providers/auth_provider.dart';
import 'providers/notifications_provider.dart';
import 'providers/products_provider.dart';
import 'screens/account_password_screen.dart';
import 'screens/account_profile_screen.dart';
import 'screens/account_screen.dart';
import 'screens/login_screen.dart';
import 'screens/notifications_screen.dart';
import 'screens/product_detail_screen.dart';
import 'screens/products_screen.dart';
import 'screens/register_screen.dart';
import 'services/analytics_service.dart';

// Arka planda gelen mesajları işle (top-level fonksiyon olmalı)
@pragma('vm:entry-point')
Future<void> _firebaseMessagingBackgroundHandler(RemoteMessage message) async {
  await Firebase.initializeApp();
}

const _shareChannel = MethodChannel('com.cufica.pricetracker/share');

// Ürün ekle dialog'unu dışarıdan tetiklemek için global key
final addProductKey = GlobalKey<ProductsScreenState>();

const AndroidNotificationChannel _androidChannel = AndroidNotificationChannel(
  'price_tracker_high_importance',
  'Price Alerts',
  description: 'Foreground notifications for price alerts',
  importance: Importance.high,
);

final FlutterLocalNotificationsPlugin _localNotifications =
    FlutterLocalNotificationsPlugin();
final StreamController<String> _localNotificationTapStream =
  StreamController<String>.broadcast();
String? _queuedLocalNotificationPayload;

void main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await Firebase.initializeApp();
  await AnalyticsService.instance.initialize();
  FirebaseMessaging.onBackgroundMessage(_firebaseMessagingBackgroundHandler);

  const androidInit = AndroidInitializationSettings('@mipmap/ic_launcher');
  const iosInit = DarwinInitializationSettings(
    requestAlertPermission: false,
    requestBadgePermission: false,
    requestSoundPermission: false,
  );
  const initSettings = InitializationSettings(
    android: androidInit,
    iOS: iosInit,
  );
  await _localNotifications.initialize(
    initSettings,
    onDidReceiveNotificationResponse: (response) {
      final payload = response.payload;
      if (payload == null || payload.isEmpty) return;

      _queuedLocalNotificationPayload = payload;
      _localNotificationTapStream.add(payload);
    },
  );
  await _localNotifications
      .resolvePlatformSpecificImplementation<
          AndroidFlutterLocalNotificationsPlugin>()
      ?.createNotificationChannel(_androidChannel);

  runApp(
    MultiProvider(
      providers: [
        ChangeNotifierProvider(create: (_) => AuthProvider()),
        ChangeNotifierProvider(create: (_) => ProductsProvider()),
        ChangeNotifierProvider(create: (_) => AccountProvider()),
        ChangeNotifierProvider(create: (_) => NotificationsProvider()),
      ],
      child: const PriceTrackerApp(),
    ),
  );
}

class PriceTrackerApp extends StatefulWidget {
  const PriceTrackerApp({super.key});

  @override
  State<PriceTrackerApp> createState() => _PriceTrackerAppState();
}

class _PriceTrackerAppState extends State<PriceTrackerApp>
    with WidgetsBindingObserver {
  late final GoRouter _router;
  StreamSubscription<String>? _localNotificationTapSubscription;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    FirebaseMessaging.onMessageOpenedApp.listen((message) {
      _handleNotificationData(message.data, source: 'resume_tap');
    });

    WidgetsBinding.instance.addPostFrameCallback((_) async {
      final initialMessage = await FirebaseMessaging.instance.getInitialMessage();
      if (initialMessage != null) {
        await _handleNotificationData(
          initialMessage.data,
          source: 'cold_start_tap',
        );
      }
    });

    // FCM token kaydını login/restore sonrasına ertele
    final auth = context.read<AuthProvider>();
    auth.onAuthenticated = _setupFcm;
    // Uygulama zaten giriş yapılmış halde açıldıysa hemen tetikle
    if (auth.isAuthenticated) _setupFcm();
    // Warm start: native URL scheme üzerinden geldiğinde
    _shareChannel.setMethodCallHandler((call) async {
      if (call.method == 'sharedUrl') {
        final url = call.arguments as String?;
        if (url != null && url.isNotEmpty) {
          await _handleSharedUrl(url);
        }
      }
    });
    _router = GoRouter(
      initialLocation: '/login',
      onException: (_, state, router) {
        // pricetracker:// deep link gibi bilinmeyen rotalar için geri dön
        if (context.read<AuthProvider>().isAuthenticated) {
          router.go('/products');
        } else {
          router.go('/login');
        }
      },
      redirect: (ctx, state) {
        final loggedIn = auth.isAuthenticated;
        final onAuth = state.matchedLocation == '/login' ||
            state.matchedLocation == '/register';
        if (!loggedIn && !onAuth) return '/login';
        if (loggedIn && onAuth) return '/products';
        return null;
      },
      refreshListenable: auth,
      routes: [
        GoRoute(path: '/login', builder: (context, state) => const LoginScreen()),
        GoRoute(path: '/register', builder: (context, state) => const RegisterScreen()),
        GoRoute(path: '/products', builder: (context, state) => ProductsScreen(key: addProductKey)),
        GoRoute(path: '/account', builder: (context, state) => const AccountScreen()),
        GoRoute(
          path: '/account/profile',
          builder: (context, state) => const AccountProfileScreen(),
        ),
        GoRoute(
          path: '/account/password',
          builder: (context, state) => const AccountPasswordScreen(),
        ),
        GoRoute(path: '/notifications', builder: (context, state) => const NotificationsScreen()),
        GoRoute(
          path: '/products/:id',
          builder: (context, state) => ProductDetailScreen(
              userProductId: int.parse(state.pathParameters['id']!)),
        ),
      ],
    );

    _localNotificationTapSubscription =
        _localNotificationTapStream.stream.listen((payload) {
      _handleLocalNotificationTap(payload);
    });

    if (_queuedLocalNotificationPayload != null) {
      final payload = _queuedLocalNotificationPayload!;
      _queuedLocalNotificationPayload = null;
      _handleLocalNotificationTap(payload);
    }

    // Cold start: native'de saklanan bekleyen URL'yi çek
    WidgetsBinding.instance.addPostFrameCallback((_) async {
      try {
        final pending =
            await _shareChannel.invokeMethod<String>('getPendingUrl');
        if (pending != null && pending.isNotEmpty) {
          await _handleSharedUrl(pending);
        }
      } catch (_) {}
    });
  }

  Future<void> _setupFcm() async {
    final messaging = FirebaseMessaging.instance;
    final permissionSettings =
        await messaging.requestPermission(alert: true, badge: true, sound: true);
    await AnalyticsService.instance.logPushPermissionResult(
      status: permissionSettings.authorizationStatus.name,
    );
    await messaging.setForegroundNotificationPresentationOptions(
      alert: true,
      badge: true,
      sound: true,
    );

    // iOS'ta önce APNs token'ın gelmesini bekle
    if (defaultTargetPlatform == TargetPlatform.iOS) {
      String? apnsToken;
      for (int i = 0; i < 15; i++) {
        apnsToken = await messaging.getAPNSToken();
        if (apnsToken != null) break;
        await Future.delayed(const Duration(seconds: 1));
      }
      if (apnsToken == null) return; // APNs token gelmediyse çık
    }

    // FCM token'ı al
    String? token;
    try {
      token = await messaging.getToken();
    } catch (_) {
      return;
    }

    if (token != null) {
      await ApiClient.updateDeviceToken(token);
    }

    // Token yenilenirse tekrar gönder
    messaging.onTokenRefresh.listen((newToken) {
      ApiClient.updateDeviceToken(newToken);
    });

    // Uygulama açıkken gelen bildirimleri yönet
    FirebaseMessaging.onMessage.listen((RemoteMessage message) {
      AnalyticsService.instance.logPushForegroundReceived(
        hasNotification: message.notification != null,
        hasData: message.data.isNotEmpty,
      );

      final n = message.notification;
      final android = message.notification?.android;
      if (n == null) return;
      if (defaultTargetPlatform != TargetPlatform.android || android == null) {
        // iOS foreground'da sistem sunumu setForeground... ile yönetiliyor.
        return;
      }

      _localNotifications.show(
        n.hashCode,
        n.title,
        n.body,
        NotificationDetails(
          android: AndroidNotificationDetails(
            _androidChannel.id,
            _androidChannel.name,
            channelDescription: _androidChannel.description,
            importance: Importance.high,
            priority: Priority.high,
            icon: '@mipmap/ic_launcher',
          ),
        ),
        payload: jsonEncode(message.data),
      );
    });
  }

  Future<void> _handleLocalNotificationTap(String payload) async {
    try {
      final decoded = jsonDecode(payload);
      if (decoded is! Map) return;

      final data = decoded.map(
        (key, value) => MapEntry(key.toString(), value?.toString() ?? ''),
      );
      await _handleNotificationData(data, source: 'foreground_local_tap');
    } catch (_) {
      // Geçersiz payload durumunda sessizce devam et.
    }
  }

  Future<void> _handleNotificationData(
    Map<String, dynamic> data, {
    required String source,
  }) async {
    final auth = context.read<AuthProvider>();
    await AnalyticsService.instance.logPushOpenedFromBackground(source: source);

    final userProductId = _extractUserProductId(data);
    if (userProductId == null) return;

    if (!auth.isAuthenticated) {
      _pendingNotificationUserProductId = userProductId;
      return;
    }

    _openProductDetailFromNotification(userProductId);
  }

  void _openProductDetailFromNotification(int userProductId) {
    _router.go('/products/$userProductId');
  }

  int? _extractUserProductId(Map<String, dynamic> data) {
    final direct = int.tryParse((data['userProductId'] ?? '').toString());
    if (direct != null) return direct;

    return _extractUserProductIdFromDeepLink(data['deepLink']?.toString());
  }

  int? _extractUserProductIdFromDeepLink(String? deepLink) {
    if (deepLink == null || deepLink.isEmpty) return null;

    final uri = Uri.tryParse(deepLink);
    if (uri == null || uri.scheme != 'pricetracker') return null;

    if (uri.host == 'product' && uri.pathSegments.isNotEmpty) {
      return int.tryParse(uri.pathSegments.first);
    }

    if (uri.pathSegments.length >= 2 && uri.pathSegments.first == 'product') {
      return int.tryParse(uri.pathSegments[1]);
    }

    return null;
  }

  Future<void> _handleSharedUrl(String url) async {
    // Aynı URL 5 saniye içinde tekrar gelirse yoksay (çift işleme koruması).
    final now = DateTime.now();
    if (_lastHandledUrl == url &&
        _lastHandledUrlTime != null &&
        now.difference(_lastHandledUrlTime!) < const Duration(seconds: 5)) {
      return;
    }
    _lastHandledUrl = url;
    _lastHandledUrlTime = now;

    final auth = context.read<AuthProvider>();
    if (!auth.isAuthenticated) {
      // Giriş yapılmamış, URL'yi beklet ve login sonrası kullan
      _pendingSharedUrl = url;
      return;
    }
    _router.go('/products');
    // ProductsScreen'in mount olması için bekle, yoksa retry
    for (int i = 0; i < 10; i++) {
      await Future.delayed(const Duration(milliseconds: 200));
      if (addProductKey.currentState != null) {
        addProductKey.currentState!.openAddSheet(initialUrl: url);
        return;
      }
    }
  }

  String? _pendingSharedUrl;
  int? _pendingNotificationUserProductId;

  // Çift URL işlemeyi engeller: URL scheme ve UserDefaults yolları
  // aynı anda tetiklendiğinde modal iki kez açılıp kapanmasın.
  String? _lastHandledUrl;
  DateTime? _lastHandledUrlTime;

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _localNotificationTapSubscription?.cancel();
    super.dispose();
  }

  // App ön plana geldiğinde (share extension'dan sonra) kontrol et
  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      _checkPendingUrl();
    }
  }

  Future<void> _checkPendingUrl() async {
    try {
      final url = await _shareChannel.invokeMethod<String>('getPendingUrl');
      if (url != null && url.isNotEmpty) {
        await _handleSharedUrl(url);
      }
    } catch (_) {}
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    // AuthProvider değiştiğinde (login olunca) bekleyen URL varsa işle
    final auth = context.watch<AuthProvider>();
    if (auth.isAuthenticated && _pendingSharedUrl != null) {
      final url = _pendingSharedUrl!;
      _pendingSharedUrl = null;
      _handleSharedUrl(url);
    }

    if (auth.isAuthenticated && _pendingNotificationUserProductId != null) {
      final userProductId = _pendingNotificationUserProductId!;
      _pendingNotificationUserProductId = null;
      _openProductDetailFromNotification(userProductId);
    }
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp.router(
      title: 'Price Tracker',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(
          seedColor: const Color(0xFF6366F1),
          brightness: Brightness.light,
        ),
        useMaterial3: true,
        cardTheme: CardThemeData(
          elevation: 0,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
          color: const Color(0xFFF3F4F6),
        ),
      ),
      darkTheme: ThemeData(
        colorScheme: ColorScheme.fromSeed(
          seedColor: const Color(0xFF6366F1),
          brightness: Brightness.dark,
        ),
        useMaterial3: true,
      ),
      themeMode: ThemeMode.system,
      routerConfig: _router,
    );
  }
}
