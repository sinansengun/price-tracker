import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:fl_chart/fl_chart.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';
import 'package:url_launcher/url_launcher.dart';
import '../models/product.dart';
import '../providers/products_provider.dart';
import '../services/analytics_service.dart';
import 'label_sheet.dart';

class ProductsScreen extends StatefulWidget {
  const ProductsScreen({super.key});

  @override
  State<ProductsScreen> createState() => ProductsScreenState();
}

class ProductsScreenState extends State<ProductsScreen> {
  int? _filterLabelId;
  bool _viewTracked = false;

  void openAddSheet({String? initialUrl}) {
    if (!mounted) return;
    AnalyticsService.instance.logAddProductOpened(
      source: initialUrl == null ? 'manual_fab' : 'share_intent',
      url: initialUrl,
    );
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      builder: (_) => _AddProductSheet(initialUrl: initialUrl),
    );
  }

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      final provider = context.read<ProductsProvider>();
      provider.fetchAll();
      if (!_viewTracked) {
        _viewTracked = true;
        AnalyticsService.instance
            .logProductsScreenViewed(productCount: provider.products.length);
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final products = context.watch<ProductsProvider>();

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
            icon: const Icon(Icons.notifications_none_rounded),
            tooltip: 'Bildirimlerim',
            onPressed: () => context.push('/notifications'),
          ),
          IconButton(
            icon: const Icon(Icons.account_circle_outlined),
            tooltip: 'Hesabım',
            onPressed: () => context.push('/account'),
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
                              label: const Text('TÜMÜ'),
                              labelStyle: const TextStyle(
                                  fontSize: 10,
                                  fontWeight: FontWeight.bold,
                                  letterSpacing: 0.6),
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
                                  label: Text(l.name.toUpperCase()),
                                  selected: selected,
                                  selectedColor: color.withValues(alpha: 0.2),
                                  checkmarkColor: color,
                                  labelStyle: TextStyle(
                                      color: selected ? color : null,
                                      fontSize: 10,
                                      fontWeight: FontWeight.bold,
                                      letterSpacing: 0.6),
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
                                padding: const EdgeInsets.fromLTRB(12, 12, 12, 96),
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
      floatingActionButton: FloatingActionButton(
        onPressed: () => _showAddDialog(context),
        child: const Icon(Icons.add),
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

  Future<void> _openProductUrl(BuildContext context, String rawUrl) async {
    final uri = Uri.tryParse(rawUrl.trim());
    if (uri == null) return;

    final ok = await launchUrl(uri, mode: LaunchMode.externalApplication);
    if (!ok && context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Ürün linki açılamadı.')),
      );
    }
  }

  String? _resolveImageUrl(String? raw) {
    if (raw == null) return null;
    final trimmed = raw.trim();
    if (trimmed.isEmpty) return null;

    final sized = trimmed.replaceAll('{size}', '200');
    if (sized.startsWith('//')) return 'https:$sized';
    return sized;
  }

  String _formatAddedDate(DateTime dt) {
    final d = dt.toLocal();
    final day = d.day.toString().padLeft(2, '0');
    final month = d.month.toString().padLeft(2, '0');
    return '$day.$month.${d.year}';
  }

  @override
  Widget build(BuildContext context) {
    final p = up.product;
    final imageUrl = _resolveImageUrl(p.imageUrl);
    final cs = Theme.of(context).colorScheme;
    final isPriceDrop = p.currentPrice != null &&
        p.initialPrice != null &&
        p.currentPrice! < p.initialPrice!;

    return Card(
      child: InkWell(
        borderRadius: BorderRadius.circular(12),
        onTap: () => context.push('/products/${up.id}'),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  SizedBox(
                    width: 92,
                    height: 92,
                    child: ClipRRect(
                      borderRadius: BorderRadius.circular(8),
                      child: imageUrl != null
                          ? Image.network(
                              imageUrl,
                              width: 92,
                              height: 92,
                                fit: BoxFit.contain,
                              errorBuilder: (_, __, ___) =>
                                  const _PlaceholderImage(),
                            )
                          : const _PlaceholderImage(),
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          p.name.isEmpty ? p.url : p.name,
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(
                              fontWeight: FontWeight.w600, fontSize: 13),
                        ),
                        if (p.store != null) ...[
                          const SizedBox(height: 2),
                          Row(
                            children: [
                              Flexible(
                                child: StoreBadge(store: p.store!, url: p.url),
                              ),
                              const SizedBox(width: 8),
                              GestureDetector(
                                onTap: () => _openProductUrl(context, p.url),
                                child: Text(
                                  'Ürün sayfası →',
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  style: TextStyle(
                                    fontSize: 11,
                                    color: cs.onSurface.withValues(alpha: 0.55),
                                  ),
                                ),
                              ),
                            ],
                          ),
                        ],
                        const SizedBox(height: 4),
                        if (p.currentPrice != null)
                          PriceText(
                            value: p.currentPrice!,
                            fontSize: 15,
                            color: isPriceDrop ? Colors.green : null,
                          )
                        else
                          const Text('—',
                              style: TextStyle(
                                  fontSize: 15,
                                  fontWeight: FontWeight.bold)),
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
                        const SizedBox(height: 6),
                        Wrap(
                          spacing: 6,
                          runSpacing: 2,
                          crossAxisAlignment: WrapCrossAlignment.center,
                          children: [
                            ...up.labels.map((l) {
                              final c = hexColor(l.color) ?? Colors.indigo;
                              return Container(
                                padding: const EdgeInsets.symmetric(
                                    horizontal: 6, vertical: 2),
                                decoration: BoxDecoration(
                                  color: c.withValues(alpha: 0.15),
                                  borderRadius: BorderRadius.circular(4),
                                ),
                                child: Text(l.name.toUpperCase(),
                                    style: TextStyle(
                                        fontSize: 10,
                                        fontWeight: FontWeight.bold,
                                        letterSpacing: 0.6,
                                        color: c)),
                              );
                            }),
                            GestureDetector(
                              onTap: () => _showLabelSheet(context),
                              child: Text('+ Etiket',
                                  style: TextStyle(
                                      fontSize: 11,
                                      color: cs.onSurface.withValues(alpha: 0.4))),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 8),
              Row(
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Eklendi: ${_formatAddedDate(up.addedAt)}',
                          style: TextStyle(
                            fontSize: 11,
                            color: cs.onSurface.withValues(alpha: 0.55),
                          ),
                        ),
                        Text(
                          'Ekleme fiyatı: ${fmtPrice(p.initialPrice ?? p.currentPrice ?? 0)} ₺',
                          style: TextStyle(
                            fontSize: 11,
                            color: cs.onSurface.withValues(alpha: 0.7),
                          ),
                        ),
                        Text(
                          up.targetPrice != null
                              ? 'Hedef: ${fmtPrice(up.targetPrice!)} ₺'
                              : 'Hedef: —',
                          style: TextStyle(
                            fontSize: 11,
                            color: cs.onSurface.withValues(alpha: 0.65),
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ],
                    ),
                  ),
                  if (p.priceHistories.isNotEmpty)
                    _MiniChartWithPct(histories: p.priceHistories),
                ],
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

  String _localDayKey(DateTime date) {
    final d = date.toLocal();
    final mm = d.month.toString().padLeft(2, '0');
    final dd = d.day.toString().padLeft(2, '0');
    return '${d.year}-$mm-$dd';
  }

  List<double?> _buildLast30DayValues() {
    final end = DateTime.now();
    final endDay = DateTime(end.year, end.month, end.day);
    final startMs =
        endDay.millisecondsSinceEpoch - 29 * Duration.millisecondsPerDay;
    final endExclusive =
        endDay.millisecondsSinceEpoch + Duration.millisecondsPerDay;

    final byDay = <String, PricePoint>{};
    for (final h in histories) {
      final local = h.checkedAt.toLocal();
      final t = local.millisecondsSinceEpoch;
      if (t < startMs || t >= endExclusive) continue;
      final key = _localDayKey(local);
      final existing = byDay[key];
      if (existing == null || local.isAfter(existing.checkedAt)) {
        byDay[key] = h;
      }
    }

    return List<double?>.generate(30, (i) {
      final day = DateTime.fromMillisecondsSinceEpoch(
          startMs + i * Duration.millisecondsPerDay);
      final key = _localDayKey(day);
      return byDay[key]?.price;
    });
  }

  @override
  Widget build(BuildContext context) {
    final values = _buildLast30DayValues();
    final prices = values.whereType<double>().toList();
    if (prices.isEmpty) {
      return const SizedBox.shrink();
    }

    final minP = prices.reduce((a, b) => a < b ? a : b);
    final maxP = prices.reduce((a, b) => a > b ? a : b);
    final flat = minP == maxP;
    final firstPrice = prices.first;
    final maxDeviation = prices
      .map((p) => (p - firstPrice).abs())
      .reduce((a, b) => a > b ? a : b);
    final basePadding = (firstPrice * 0.03).abs().clamp(1.0, double.infinity);
    final halfRange = (maxDeviation * 1.15) > basePadding
      ? (maxDeviation * 1.15)
      : basePadding;
    final yMin = firstPrice - halfRange;
    final yMax = firstPrice + halfRange;
    final color = flat ? const Color(0xFF94A3B8) : const Color(0xFF2563EB);

    final spots = prices.length == 1
        ? () {
            final idx = values.indexWhere((v) => v != null);
            final left = (idx - 0.35).clamp(0, values.length - 1).toDouble();
            final right = (idx + 0.35).clamp(0, values.length - 1).toDouble();
            return [
              FlSpot(left, firstPrice),
              FlSpot(right, firstPrice),
            ];
          }()
        : List<FlSpot>.generate(values.length, (i) {
            final v = values[i];
            if (v == null) return FlSpot.nullSpot;
            return FlSpot(i.toDouble(), v);
          });

    return Container(
      width: 112,
      height: 46,
      padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 4),
      decoration: BoxDecoration(
        color: Colors.white.withValues(alpha: 0.7),
        borderRadius: BorderRadius.circular(8),
        border: Border.all(
          color: Colors.grey.withValues(alpha: 0.45),
          width: 1,
          strokeAlign: BorderSide.strokeAlignInside,
        ),
      ),
      child: LineChart(
        LineChartData(
          minX: 0,
          maxX: (values.length - 1).toDouble(),
          minY: yMin,
          maxY: yMax,
          lineBarsData: [
            LineChartBarData(
              spots: spots,
              isCurved: false,
              color: color,
              barWidth: 1.8,
              dotData: const FlDotData(show: false),
              belowBarData: BarAreaData(show: false),
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

  String _localDayKey(DateTime date) {
    final d = date.toLocal();
    final mm = d.month.toString().padLeft(2, '0');
    final dd = d.day.toString().padLeft(2, '0');
    return '${d.year}-$mm-$dd';
  }

  List<double?> _buildLast30DayValues() {
    final end = DateTime.now();
    final endDay = DateTime(end.year, end.month, end.day);
    final startMs =
        endDay.millisecondsSinceEpoch - 29 * Duration.millisecondsPerDay;
    final endExclusive =
        endDay.millisecondsSinceEpoch + Duration.millisecondsPerDay;

    final byDay = <String, PricePoint>{};
    for (final h in histories) {
      final local = h.checkedAt.toLocal();
      final t = local.millisecondsSinceEpoch;
      if (t < startMs || t >= endExclusive) continue;
      final key = _localDayKey(local);
      final existing = byDay[key];
      if (existing == null || local.isAfter(existing.checkedAt)) {
        byDay[key] = h;
      }
    }

    return List<double?>.generate(30, (i) {
      final day = DateTime.fromMillisecondsSinceEpoch(
          startMs + i * Duration.millisecondsPerDay);
      final key = _localDayKey(day);
      return byDay[key]?.price;
    });
  }

  @override
  Widget build(BuildContext context) {
    final values = _buildLast30DayValues();
    final prices = values.whereType<double>().toList();
    if (prices.isEmpty) {
      return const SizedBox.shrink();
    }

    final firstPrice = prices.first;
    final lastPrice = prices.last;
    final flat = prices.reduce((a, b) => a < b ? a : b) ==
        prices.reduce((a, b) => a > b ? a : b);
    final isUp = lastPrice > firstPrice;
    final color = flat
      ? const Color(0xFF94A3B8)
      : (isUp ? Colors.red : Colors.green);

    // Yüzde değişim
    double? pct;
    if (firstPrice > 0) {
      pct = ((lastPrice - firstPrice) / firstPrice) * 100;
    }

    const periodLabel = 'Son 1 ay';

    return Column(
      crossAxisAlignment: CrossAxisAlignment.center,
      mainAxisSize: MainAxisSize.min,
      children: [
        if (pct != null)
          Row(
            crossAxisAlignment: CrossAxisAlignment.center,
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                periodLabel,
                style: TextStyle(
                    fontSize: 9,
                    fontWeight: FontWeight.w500,
                    color: color),
              ),
              const SizedBox(width: 4),
              Text(
                '${flat ? '—' : isUp ? '▲' : '▼'} %${pct.abs().toStringAsFixed(1)}',
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
      width: double.infinity,
      height: double.infinity,
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
  int? _selectedLabelId;
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
    final productsProvider = context.read<ProductsProvider>();
    final navigator = Navigator.of(context);

    setState(() {
      _loading = true;
      _error = null;
    });

    final targetPrice = _targetCtrl.text.isNotEmpty
        ? double.tryParse(_targetCtrl.text.replaceAll(',', '.'))
        : null;
    final normalizedUrl = _urlCtrl.text.trim();

    await AnalyticsService.instance.logAddProductSubmitted(
      url: normalizedUrl,
      hasTargetPrice: targetPrice != null,
    );

    final err = await productsProvider.addProduct(
      normalizedUrl,
      targetPrice: targetPrice,
      initialLabelId: _selectedLabelId,
    );

    if (!mounted) return;

    if (err == null) {
      await AnalyticsService.instance
          .logAddProductSuccess(hasTargetPrice: targetPrice != null);
      if (!mounted) return;
      final infoMessage = productsProvider.lastAddProductMessage ??
          'Urun eklendi. Fiyat bilgisi aliniyor...';
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(infoMessage)),
      );
      navigator.pop();
    } else {
      await AnalyticsService.instance
          .logAddProductFailed(reason: _mapAddProductFailure(err));
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = err;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final labels = context.watch<ProductsProvider>().labels;

    if (_selectedLabelId != null && !labels.any((l) => l.id == _selectedLabelId)) {
      _selectedLabelId = null;
    }

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
            const SizedBox(height: 12),
            DropdownButtonFormField<int?>(
              value: _selectedLabelId,
              decoration: const InputDecoration(
                labelText: 'Label (opsiyonel)',
                prefixIcon: Icon(Icons.label_outline),
                border: OutlineInputBorder(),
              ),
              items: [
                const DropdownMenuItem<int?>(
                  value: null,
                  child: Text('Etiket Seç'),
                ),
                ...labels.map((l) => DropdownMenuItem<int?>(
                        value: l.id,
                        child: Text(l.name),
                      )),
              ],
              onChanged: (value) {
                setState(() {
                  _selectedLabelId = value;
                });
              },
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

  String _mapAddProductFailure(String err) {
    final normalized = err.toLowerCase();
    if (normalized.contains('url')) return 'invalid_url';
    if (normalized.contains('bağlan') || normalized.contains('baglan')) {
      return 'network';
    }
    if (normalized.contains('zaten')) return 'duplicate_or_exists';
    return 'request_rejected';
  }
}
