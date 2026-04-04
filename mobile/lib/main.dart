import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';
import 'providers/auth_provider.dart';
import 'providers/products_provider.dart';
import 'screens/login_screen.dart';
import 'screens/product_detail_screen.dart';
import 'screens/products_screen.dart';
import 'screens/register_screen.dart';

const _shareChannel = MethodChannel('com.pricetracker.mobile/share');

// Ürün ekle dialog'unu dışarıdan tetiklemek için global key
final addProductKey = GlobalKey<ProductsScreenState>();

void main() {
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

  Future<void> _handleSharedUrl(String url) async {
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
