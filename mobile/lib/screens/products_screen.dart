import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';
import '../models/product.dart';
import '../providers/auth_provider.dart';
import '../providers/products_provider.dart';

class ProductsScreen extends StatefulWidget {
  const ProductsScreen({super.key});

  @override
  State<ProductsScreen> createState() => _ProductsScreenState();
}

class _ProductsScreenState extends State<ProductsScreen> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback(
        (_) => context.read<ProductsProvider>().fetchAll());
  }

  @override
  Widget build(BuildContext context) {
    final products = context.watch<ProductsProvider>();
    final auth = context.read<AuthProvider>();

    return Scaffold(
      appBar: AppBar(
        title: const Text('Takip Listesi'),
        actions: [
          IconButton(
            icon: const Icon(Icons.logout),
            tooltip: 'Çıkış Yap',
            onPressed: () async {
              await auth.logout();
              if (context.mounted) context.go('/login');
            },
          ),
        ],
      ),
      body: products.loading
          ? const Center(child: CircularProgressIndicator())
          : products.error != null
              ? Center(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(products.error!),
                      const SizedBox(height: 12),
                      FilledButton(
                          onPressed: () => products.fetchAll(),
                          child: const Text('Tekrar Dene')),
                    ],
                  ),
                )
              : products.products.isEmpty
                  ? const Center(child: Text('Henüz ürün eklenmedi.'))
                  : RefreshIndicator(
                      onRefresh: products.fetchAll,
                      child: ListView.separated(
                        padding: const EdgeInsets.all(12),
                        itemCount: products.products.length,
                        separatorBuilder: (_, __) => const SizedBox(height: 8),
                        itemBuilder: (ctx, i) =>
                            _ProductCard(up: products.products[i]),
                      ),
                    ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _showAddDialog(context),
        icon: const Icon(Icons.add),
        label: const Text('Ürün Ekle'),
      ),
    );
  }

  void _showAddDialog(BuildContext context) {
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      builder: (_) => const _AddProductSheet(),
    );
  }
}

class _ProductCard extends StatelessWidget {
  final UserProduct up;
  const _ProductCard({required this.up});

  @override
  Widget build(BuildContext context) {
    final p = up.product;
    final cs = Theme.of(context).colorScheme;
    final isPriceDrop = p.currentPrice != null &&
        p.initialPrice != null &&
        p.currentPrice! < p.initialPrice!;

    return Card(
      child: InkWell(
        borderRadius: BorderRadius.circular(12),
        onTap: () => context.push('/products/${up.id}'),
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Row(
            children: [
              // Ürün resmi
              ClipRRect(
                borderRadius: BorderRadius.circular(8),
                child: p.imageUrl != null
                    ? Image.network(p.imageUrl!,
                        width: 90, height: 90, fit: BoxFit.cover,
                        errorBuilder: (_, __, ___) =>
                            const _PlaceholderImage())
                    : const _PlaceholderImage(),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(p.name.isEmpty ? p.url : p.name,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(fontWeight: FontWeight.w600)),
                    if (p.store != null)
                      Text(p.store!,
                          style: TextStyle(
                              fontSize: 12, color: cs.onSurface.withOpacity(.6))),
                    const SizedBox(height: 4),
                    Row(
                      children: [
                        Text(
                          p.currentPrice != null
                              ? '₺${p.currentPrice!.toStringAsFixed(2)}'
                              : '—',
                          style: TextStyle(
                              fontSize: 16,
                              fontWeight: FontWeight.bold,
                              color: isPriceDrop ? Colors.green : null),
                        ),
                        if (up.targetPrice != null) ...[
                          const SizedBox(width: 8),
                          Text(
                            'Hedef: ₺${up.targetPrice!.toStringAsFixed(2)}',
                            style: TextStyle(
                                fontSize: 12,
                                color: cs.onSurface.withOpacity(.6)),
                          ),
                        ],
                      ],
                    ),
                  ],
                ),
              ),
              IconButton(
                icon: const Icon(Icons.delete_outline),
                onPressed: () => _confirmDelete(context),
              ),
            ],
          ),
        ),
      ),
    );
  }

  void _confirmDelete(BuildContext context) {
    showDialog(
      context: context,
      builder: (_) => AlertDialog(
        title: const Text('Ürünü Kaldır'),
        content: const Text('Bu ürünü takip listenizden kaldırmak istediğinizden emin misiniz?'),
        actions: [
          TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('İptal')),
          TextButton(
            onPressed: () {
              Navigator.pop(context);
              context.read<ProductsProvider>().deleteProduct(up.id);
            },
            child: const Text('Kaldır', style: TextStyle(color: Colors.red)),
          ),
        ],
      ),
    );
  }
}

class _PlaceholderImage extends StatelessWidget {
  const _PlaceholderImage();

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 90,
      height: 90,
      color: Theme.of(context).colorScheme.surfaceContainerHighest,
      child: const Icon(Icons.image_not_supported_outlined),
    );
  }
}

class _AddProductSheet extends StatefulWidget {
  const _AddProductSheet();

  @override
  State<_AddProductSheet> createState() => _AddProductSheetState();
}

class _AddProductSheetState extends State<_AddProductSheet> {
  final _formKey = GlobalKey<FormState>();
  final _urlCtrl = TextEditingController();
  final _targetCtrl = TextEditingController();
  bool _loading = false;
  String? _error;

  @override
  void dispose() {
    _urlCtrl.dispose();
    _targetCtrl.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _loading = true;
      _error = null;
    });

    final targetPrice = _targetCtrl.text.isNotEmpty
        ? double.tryParse(_targetCtrl.text.replaceAll(',', '.'))
        : null;

    final err = await context
        .read<ProductsProvider>()
        .addProduct(_urlCtrl.text.trim(), targetPrice: targetPrice);

    if (mounted) {
      if (err == null) {
        Navigator.pop(context);
      } else {
        setState(() {
          _loading = false;
          _error = err;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: EdgeInsets.only(
        left: 20,
        right: 20,
        top: 20,
        bottom: MediaQuery.of(context).viewInsets.bottom + 20,
      ),
      child: Form(
        key: _formKey,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Ürün Ekle',
                style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 16),
            TextFormField(
              controller: _urlCtrl,
              keyboardType: TextInputType.url,
              enableInteractiveSelection: true,
              autocorrect: false,
              decoration: InputDecoration(
                labelText: 'Ürün URL',
                hintText: 'https://www.hepsiburada.com/...',
                prefixIcon: const Icon(Icons.link),
                border: const OutlineInputBorder(),
                suffixIcon: IconButton(
                  icon: const Icon(Icons.content_paste),
                  tooltip: 'Panodan Yapıştır',
                  onPressed: () async {
                    final data = await Clipboard.getData(Clipboard.kTextPlain);
                    if (data?.text != null) {
                      _urlCtrl.text = data!.text!.trim();
                      _urlCtrl.selection = TextSelection.fromPosition(
                          TextPosition(offset: _urlCtrl.text.length));
                    }
                  },
                ),
              ),
              validator: (v) {
                if (v == null || v.isEmpty) return 'URL boş olamaz';
                if (!v.startsWith('http')) return 'Geçerli URL giriniz';
                return null;
              },
            ),
            const SizedBox(height: 12),
            TextFormField(
              controller: _targetCtrl,
              keyboardType:
                  const TextInputType.numberWithOptions(decimal: true),
              decoration: const InputDecoration(
                labelText: 'Hedef Fiyat (opsiyonel)',
                prefixIcon: Icon(Icons.local_offer_outlined),
                prefixText: '₺ ',
                border: OutlineInputBorder(),
              ),
            ),
            if (_error != null) ...[
              const SizedBox(height: 8),
              Text(_error!,
                  style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                      fontSize: 12)),
            ],
            const SizedBox(height: 16),
            SizedBox(
              width: double.infinity,
              child: FilledButton(
                onPressed: _loading ? null : _submit,
                child: _loading
                    ? const SizedBox(
                        height: 20,
                        width: 20,
                        child: CircularProgressIndicator(strokeWidth: 2))
                    : const Text('Ekle'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
