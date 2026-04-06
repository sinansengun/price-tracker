import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:fl_chart/fl_chart.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';
import '../models/product.dart';
import '../providers/auth_provider.dart';
import '../providers/products_provider.dart';
import 'label_sheet.dart';

class ProductsScreen extends StatefulWidget {
  const ProductsScreen({super.key});

  @override
  State<ProductsScreen> createState() => ProductsScreenState();
}

class ProductsScreenState extends State<ProductsScreen> {
  int? _filterLabelId;

  void openAddSheet({String? initialUrl}) {
    if (!mounted) return;
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      builder: (_) => _AddProductSheet(initialUrl: initialUrl),
    );
  }

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

    final allLabels = products.labels;
    final filtered = _filterLabelId == null
        ? products.products
        : products.products
            .where((up) => up.labels.any((l) => l.id == _filterLabelId))
            .toList();

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
              : Column(
                  children: [
                    // Label filtre şeridi
                    if (allLabels.isNotEmpty)
                      SingleChildScrollView(
                        scrollDirection: Axis.horizontal,
                        padding: const EdgeInsets.fromLTRB(12, 8, 12, 4),
                        child: Row(
                          children: [
                            FilterChip(
                              label: const Text('Tümü'),
                              labelStyle: const TextStyle(fontSize: 12),
                              selected: _filterLabelId == null,
                              onSelected: (_) =>
                                  setState(() => _filterLabelId = null),
                            ),
                            const SizedBox(width: 6),
                            ...allLabels.map((l) {
                              final color =
                                  hexColor(l.color) ?? Colors.indigo;
                              final selected = _filterLabelId == l.id;
                              return Padding(
                                padding: const EdgeInsets.only(right: 6),
                                child: FilterChip(
                                  label: Text(l.name),
                                  selected: selected,
                                  selectedColor: color.withValues(alpha: 0.2),
                                  checkmarkColor: color,
                                  labelStyle: TextStyle(
                                      color: selected ? color : null,
                                      fontSize: 12),
                                  side: BorderSide(
                                      color: selected
                                          ? color
                                          : Colors.grey.shade300),
                                  onSelected: (_) => setState(() =>
                                      _filterLabelId =
                                          selected ? null : l.id),
                                ),
                              );
                            }),
                          ],
                        ),
                      ),
                    Expanded(
                      child: filtered.isEmpty
                          ? const Center(child: Text('Henüz ürün eklenmedi.'))
                          : RefreshIndicator(
                              onRefresh: products.fetchAll,
                              child: ListView.separated(
                                padding: const EdgeInsets.all(12),
                                itemCount: filtered.length,
                                separatorBuilder: (_, __) =>
                                    const SizedBox(height: 8),
                                itemBuilder: (ctx, i) {
                                  final up = filtered[i];
                                  return Dismissible(
                                    key: Key('product_${up.id}'),
                                    direction: DismissDirection.endToStart,
                                    background: Container(
                                      alignment: Alignment.centerRight,
                                      padding:
                                          const EdgeInsets.only(right: 24),
                                      decoration: BoxDecoration(
                                        color: Colors.red,
                                        borderRadius:
                                            BorderRadius.circular(12),
                                      ),
                                      child: const Icon(
                                          Icons.delete_outline,
                                          color: Colors.white,
                                          size: 28),
                                    ),
                                    confirmDismiss: (_) async {
                                      return await showDialog<bool>(
                                            context: ctx,
                                            builder: (_) => AlertDialog(
                                              title:
                                                  const Text('Ürünü Kaldır'),
                                              content: const Text(
                                                  'Bu ürünü takip listenizden kaldırmak istediğinizden emin misiniz?'),
                                              actions: [
                                                TextButton(
                                                    onPressed: () =>
                                                        Navigator.pop(
                                                            ctx, false),
                                                    child: const Text(
                                                        'İptal')),
                                                TextButton(
                                                  onPressed: () =>
                                                      Navigator.pop(
                                                          ctx, true),
                                                  child: const Text(
                                                      'Kaldır',
                                                      style: TextStyle(
                                                          color: Colors.red)),
                                                ),
                                              ],
                                            ),
                                          ) ??
                                          false;
                                    },
                                    onDismissed: (_) => ctx
                                        .read<ProductsProvider>()
                                        .deleteProduct(up.id),
                                    child: _ProductCard(up: up),
                                  );
                                },
                              ),
                            ),
                    ),
                  ],
                ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _showAddDialog(context),
        icon: const Icon(Icons.add),
        label: const Text('Ürün Ekle'),
      ),
    );
  }

  void _showAddDialog(BuildContext context) {
    openAddSheet();
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
              SizedBox(
                width: 90,
                height: 90,
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(8),
                  child: p.imageUrl != null
                      ? Image.network(p.imageUrl!,
                          width: 90, height: 90, fit: BoxFit.cover,
                          errorBuilder: (_, __, ___) =>
                              const _PlaceholderImage())
                      : const _PlaceholderImage(),
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(p.name.isEmpty ? p.url : p.name,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13)),
                    if (p.store != null) ...[
                      const SizedBox(height: 2),
                      StoreBadge(store: p.store!, url: p.url),
                    ],
                    const SizedBox(height: 4),
                    Row(
                      children: [
                        if (p.currentPrice != null)
                          PriceText(
                            value: p.currentPrice!,
                            fontSize: 15,
                            color: isPriceDrop ? Colors.green : null,
                          )
                        else
                          const Text('—',
                              style: TextStyle(
                                  fontSize: 15, fontWeight: FontWeight.bold)),
                        if (up.targetPrice != null) ...[
                          const SizedBox(width: 8),
                          Text(
                            'Hedef: ${fmtPrice(up.targetPrice!)} ₺',
                            style: TextStyle(
                                fontSize: 11,
                                color: cs.onSurface.withValues(alpha: 0.55)),
                          ),
                        ],
                      ],
                    ),
                    if (p.initialPrice != null &&
                        p.currentPrice != null &&
                        p.initialPrice != p.currentPrice)
                      Text(
                        '${fmtPrice(p.initialPrice!)} ₺',
                        style: TextStyle(
                          fontSize: 11,
                          color: cs.onSurface.withValues(alpha: 0.4),
                          decoration: TextDecoration.lineThrough,
                        ),
                      ),
                    // Label chips
                    if (up.labels.isNotEmpty) ...[
                      const SizedBox(height: 4),
                      Wrap(
                        spacing: 4,
                        runSpacing: 2,
                        children: up.labels.map((l) {
                          final c = hexColor(l.color) ?? Colors.indigo;
                          return Container(
                            padding: const EdgeInsets.symmetric(
                                horizontal: 6, vertical: 2),
                            decoration: BoxDecoration(
                              color: c.withValues(alpha: 0.15),
                              borderRadius: BorderRadius.circular(4),
                            ),
                            child: Text(l.name,
                                style: TextStyle(
                                    fontSize: 10,
                                    fontWeight: FontWeight.w600,
                                    color: c)),
                          );
                        }).toList(),
                      ),
                    ],
                    const SizedBox(height: 2),
                    // "+ Etiket" butonu
                    GestureDetector(
                      onTap: () => _showLabelSheet(context),
                      child: Text('+ Etiket',
                          style: TextStyle(
                              fontSize: 11,
                              color: cs.onSurface.withValues(alpha: 0.4))),
                    ),
                  ],
                ),
              ),
              // Mini grafik + değişim oranı
              if (p.priceHistories.length >= 2)
                Padding(
                  padding: const EdgeInsets.only(left: 8),
                  child: _MiniChartWithPct(histories: p.priceHistories),
                ),
            ],
          ),
        ),
      ),
    );
  }

  void _showLabelSheet(BuildContext context) {
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      builder: (_) => LabelSheet(userProductId: up.id),
    );
  }
}

class _MiniChart extends StatelessWidget {
  final List<PricePoint> histories;
  const _MiniChart({required this.histories});

  @override
  Widget build(BuildContext context) {
    final sorted = [...histories]
      ..sort((a, b) => a.checkedAt.compareTo(b.checkedAt));
    final prices = sorted.map((h) => h.price).toList();
    final minP = prices.reduce((a, b) => a < b ? a : b);
    final maxP = prices.reduce((a, b) => a > b ? a : b);
    final flat = minP == maxP;
    final isDown = prices.last < prices.first;
    final color = flat
        ? Colors.grey
        : isDown
            ? Colors.green
            : Colors.red;

    final spots = sorted
        .asMap()
        .entries
        .map((e) => FlSpot(e.key.toDouble(), e.value.price))
        .toList();

    return SizedBox(
      width: 72,
      height: 44,
      child: LineChart(
        LineChartData(
          minY: flat ? minP - 1 : minP,
          maxY: flat ? maxP + 1 : maxP,
          lineBarsData: [
            LineChartBarData(
              spots: spots,
              isCurved: true,
              color: color,
              barWidth: 2,
              dotData: const FlDotData(show: false),
              belowBarData: BarAreaData(
                show: true,
                color: color.withValues(alpha: 0.1),
              ),
            ),
          ],
          titlesData: const FlTitlesData(show: false),
          gridData: const FlGridData(show: false),
          borderData: FlBorderData(show: false),
          lineTouchData: const LineTouchData(enabled: false),
        ),
      ),
    );
  }
}

class _MiniChartWithPct extends StatelessWidget {
  final List<PricePoint> histories;
  const _MiniChartWithPct({required this.histories});

  @override
  Widget build(BuildContext context) {
    final sorted = [...histories]
      ..sort((a, b) => a.checkedAt.compareTo(b.checkedAt));
    final first = sorted.first;
    final last = sorted.last;
    final prices = sorted.map((h) => h.price).toList();
    final flat = prices.reduce((a, b) => a < b ? a : b) ==
        prices.reduce((a, b) => a > b ? a : b);
    final isDown = last.price < first.price;
    final color = flat
        ? Colors.grey
        : isDown
            ? Colors.green
            : Colors.red;

    // Yüzde değişim
    double? pct;
    if (first.price > 0) {
      pct = ((last.price - first.price) / first.price) * 100;
    }

    // Dönem etiketi
    String periodLabel = '';
    final days = last.checkedAt.difference(first.checkedAt).inDays;
    if (days < 2) {
      periodLabel = 'Son 1 gün';
    } else if (days < 31) {
      periodLabel = 'Son $days gün';
    } else if (days < 365) {
      periodLabel = 'Son ${(days / 30).round()} ay';
    } else {
      periodLabel = 'Son ${(days / 365).round()} yıl';
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.center,
      mainAxisSize: MainAxisSize.min,
      children: [
        if (pct != null)
          Column(
            crossAxisAlignment: CrossAxisAlignment.center,
            children: [
              Text(
                periodLabel,
                style: TextStyle(
                    fontSize: 9,
                    fontWeight: FontWeight.w500,
                    color: color),
              ),
              Text(
                '${isDown ? '▼' : flat ? '—' : '▲'} %${pct.abs().toStringAsFixed(1)}',
                style: TextStyle(
                    fontSize: 10,
                    fontWeight: FontWeight.w700,
                    color: color),
              ),
            ],
          ),
        const SizedBox(height: 2),
        _MiniChart(histories: histories),
      ],
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
  final String? initialUrl;
  const _AddProductSheet({this.initialUrl});

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
  void initState() {
    super.initState();
    if (widget.initialUrl != null && widget.initialUrl!.isNotEmpty) {
      _urlCtrl.text = widget.initialUrl!;
    } else {
      _autoPasteUrl();
    }
  }

  Future<void> _autoPasteUrl() async {
    try {
      final data = await Clipboard.getData(Clipboard.kTextPlain);
      final text = data?.text?.trim() ?? '';
      if (text.startsWith('http') && _urlCtrl.text.isEmpty) {
        _urlCtrl.text = text;
        _urlCtrl.selection = TextSelection.fromPosition(
            TextPosition(offset: _urlCtrl.text.length));
      }
    } catch (_) {}
  }

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
                    try {
                      final data =
                          await Clipboard.getData(Clipboard.kTextPlain);
                      if (data?.text != null) {
                        _urlCtrl.text = data!.text!.trim();
                        _urlCtrl.selection = TextSelection.fromPosition(
                            TextPosition(offset: _urlCtrl.text.length));
                      }
                    } catch (_) {}
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
