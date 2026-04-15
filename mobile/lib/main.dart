import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';
import 'api/api_client.dart';
import 'providers/auth_provider.dart';
import 'providers/products_provider.dart';
import 'screens/login_screen.dart';
import 'screens/product_detail_screen.dart';
import 'screens/products_screen.dart';
import 'screens/register_screen.dart';

// Arka planda gelen mesajları işle (top-level fonksiyon olmalı)
@pragma('vm:entry-point')
Future<void> _firebaseMessagingBackgroundHandler(RemoteMessage message) async {
  await Firebase.initializeApp();
}

const _shareChannel = MethodChannel('com.pricetracker.mobile/share');

// Ürün ekle dialog'unu dışarıdan tetiklemek için global key
final addProductKey = GlobalKey<ProductsScreenState>();

void main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await Firebase.initializeApp();
  FirebaseMessaging.onBackgroundMessage(_firebaseMessagingBackgroundHandler);

  runApp(
    MultiProvider(
      providers: [
        ChangeNotifierProvider(create: (_) => AuthProvider()),
        ChangeNotifierProvider(create: (_) => ProductsProvider()),
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

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _setupFcm();
    // Warm start: native URL scheme üzerinden geldiğinde
    _shareChannel.setMethodCallHandler((call) async {
      if (call.method == 'sharedUrl') {
        final url = call.arguments as String?;
        if (url != null && url.isNotEmpty) {
          await _handleSharedUrl(url);
        }
      }
    });
    final auth = context.read<AuthProvider>();
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
        GoRoute(path: '/login', builder: (_, __) => const LoginScreen()),
        GoRoute(path: '/register', builder: (_, __) => const RegisterScreen()),
        GoRoute(path: '/products', builder: (_, __) => ProductsScreen(key: addProductKey)),
        GoRoute(
          path: '/products/:id',
          builder: (_, state) => ProductDetailScreen(
              userProductId: int.parse(state.pathParameters['id']!)),
        ),
      ],
    );

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
    await messaging.requestPermission(alert: true, badge: true, sound: true);

    // iOS'ta APNs token hazır olana kadar bekle (maks 10 saniye)
    String? token;
    for (int i = 0; i < 10; i++) {
      try {
        token = await messaging.getToken();
        if (token != null) break;
      } catch (_) {
        await Future.delayed(const Duration(seconds: 1));
      }
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
      // İsteğe bağlı: snackbar veya yerel bildirim gösterilebilir
    });
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

  // Çift URL işlemeyi engeller: URL scheme ve UserDefaults yolları
  // aynı anda tetiklendiğinde modal iki kez açılıp kapanmasın.
  String? _lastHandledUrl;
  DateTime? _lastHandledUrlTime;

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
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
