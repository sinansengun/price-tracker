import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import 'package:url_launcher/url_launcher.dart';
import '../models/product.dart';
import '../providers/products_provider.dart';
import '../services/analytics_service.dart';
import 'alert_settings_sheet.dart';
import 'label_sheet.dart';

class ProductDetailScreen extends StatefulWidget {
  final int userProductId;
  const ProductDetailScreen({super.key, required this.userProductId});

  @override
  State<ProductDetailScreen> createState() => _ProductDetailScreenState();
}

class _ProductDetailScreenState extends State<ProductDetailScreen> {
  UserProduct? _up;
  bool _loading = true;
  bool _checkLoading = false;
  bool _viewTracked = false;
  _ChartRange _chartRange = _ChartRange.oneMonth;

  String? _resolveImageUrl(String? raw) {
    if (raw == null) return null;
    final trimmed = raw.trim();
    if (trimmed.isEmpty) return null;

    final sized = trimmed.replaceAll('{size}', '375');
    if (sized.startsWith('//')) return 'https:$sized';
    return sized;
  }

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    final provider = context.read<ProductsProvider>();
    _up = await provider.fetchById(widget.userProductId);

    if (_up != null && !_viewTracked) {
      _viewTracked = true;
      await AnalyticsService.instance.logProductDetailViewed(
        hasTargetPrice: _up!.normalizedAlertMode == userProductAlertModeTargetPrice &&
            _up!.targetPrice != null,
        labelCount: _up!.labels.length,
        historyPointCount: _up!.product.priceHistories.length,
      );
    }

    setState(() => _loading = false);
  }

  Future<void> _manualCheck() async {
    setState(() => _checkLoading = true);
    await context.read<ProductsProvider>().manualCheck(widget.userProductId);
    await Future.delayed(const Duration(seconds: 2)); // job biraz sürer
    await _load();
    setState(() => _checkLoading = false);
  }

  Future<void> _openProductUrl(String rawUrl) async {
    final uri = Uri.tryParse(rawUrl.trim());
    if (uri == null) return;

    final ok = await launchUrl(uri, mode: LaunchMode.externalApplication);
    if (!ok && mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Ürün linki açılamadı.')),
      );
    }
  }

  Duration _lookbackForRange(_ChartRange range) {
    switch (range) {
      case _ChartRange.oneWeek:
        return const Duration(days: 7);
      case _ChartRange.oneMonth:
        return const Duration(days: 30);
      case _ChartRange.threeMonths:
        return const Duration(days: 90);
      case _ChartRange.sixMonths:
        return const Duration(days: 180);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(_up?.product.name.isNotEmpty == true
            ? _up!.product.name
            : 'Ürün Detay'),
        actions: [
          IconButton(
            icon: const Icon(Icons.delete_outline),
            tooltip: 'Ürünü Kaldır',
            onPressed: _up == null ? null : () => _confirmDelete(context),
          ),
          IconButton(
            icon: _checkLoading
                ? const SizedBox(
                    width: 20,
                    height: 20,
                    child: CircularProgressIndicator(strokeWidth: 2))
                : const Icon(Icons.refresh),
            onPressed: _checkLoading ? null : _manualCheck,
            tooltip: 'Fiyatı Güncelle',
          ),
        ],
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _up == null
              ? const Center(child: Text('Ürün bulunamadı.'))
              : _buildContent(context, _up!),
    );
  }

  Widget _buildContent(BuildContext context, UserProduct up) {
    final p = up.product;
    final filteredHistory = _filterPriceHistory(
      p.priceHistories,
      lookback: _lookbackForRange(_chartRange),
    );
    final imageUrl = _resolveImageUrl(p.imageUrl);
    final cs = Theme.of(context).colorScheme;
    final missingPriceLabel = p.missingPriceLabel ?? 'Fiyat kontrolü bekleniyor';
    final isPriceDrop = p.currentPrice != null &&
        p.initialPrice != null &&
        p.currentPrice! < p.initialPrice!;

    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        // Ürün başlık kartı
        Card(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                if (imageUrl != null)
                  Center(
                    child: ClipRRect(
                      borderRadius: BorderRadius.circular(12),
                      child: Container(
                        width: double.infinity,
                        constraints: const BoxConstraints(maxHeight: 280),
                        color: Colors.white,
                        child: Image.network(
                          imageUrl,
                          height: 260,
                          fit: BoxFit.contain,
                          errorBuilder: (context, error, stackTrace) =>
                              const SizedBox.shrink(),
                        ),
                      ),
                    ),
                  ),
                if (imageUrl != null) const SizedBox(height: 12),
                Text(p.name.isEmpty ? p.url : p.name,
                    style: const TextStyle(
                        fontSize: 16, fontWeight: FontWeight.w600)),
                if (p.store != null) ...[
                  const SizedBox(height: 6),
                  Row(
                    children: [
                      Flexible(
                        child: StoreBadge(store: p.store!, url: p.url),
                      ),
                      const SizedBox(width: 8),
                      GestureDetector(
                        onTap: () => _openProductUrl(p.url),
                        child: Text(
                          'Ürün sayfası →',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(
                            fontSize: 12,
                            color: cs.onSurface.withValues(alpha: 0.55),
                          ),
                        ),
                      ),
                    ],
                  ),
                ],
                const SizedBox(height: 16),
                Row(
                  children: [
                    Expanded(
                      child: p.currentPrice != null
                          ? PriceText(
                              value: p.currentPrice!,
                              fontSize: 18,
                              color: isPriceDrop ? Colors.green : null,
                            )
                          : Text(
                              missingPriceLabel,
                              style: TextStyle(
                                fontSize: 16,
                                fontWeight: FontWeight.bold,
                                color: p.isOutOfStock
                                    ? Colors.orange.shade700
                                    : cs.onSurface.withValues(alpha: 0.72),
                              ),
                            ),
                    ),
                  ],
                ),
                const SizedBox(height: 8),
                Text(
                  DateFormat('dd.MM.yyyy HH:mm').format(up.addedAt),
                  style:
                      TextStyle(fontSize: 12, color: cs.onSurface.withValues(alpha: 0.5)),
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 12),

        // Etiketler
        Card(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    const Icon(Icons.label_outline, size: 18),
                    const SizedBox(width: 6),
                    Text('Etiketler',
                        style: Theme.of(context).textTheme.titleSmall),
                    const Spacer(),
                    TextButton.icon(
                      onPressed: () => _showLabelSheet(context, up),
                      icon: const Icon(Icons.add, size: 16),
                      label: const Text('Yönet', style: TextStyle(fontSize: 12)),
                      style: TextButton.styleFrom(
                          padding: const EdgeInsets.symmetric(
                              horizontal: 8, vertical: 4),
                          minimumSize: Size.zero,
                          tapTargetSize: MaterialTapTargetSize.shrinkWrap),
                    ),
                  ],
                ),
                if (up.labels.isEmpty)
                  const Padding(
                    padding: EdgeInsets.only(top: 6),
                    child: Text('Henüz etiket eklenmedi.',
                        style: TextStyle(fontSize: 12, color: Colors.grey)),
                  )
                else
                  Padding(
                    padding: const EdgeInsets.only(top: 8),
                    child: Wrap(
                      spacing: 6,
                      runSpacing: 4,
                      children: up.labels.map((l) {
                        final c = hexColor(l.color) ?? Colors.indigo;
                        return Chip(
                          label: Text(l.name.toUpperCase(),
                              style: TextStyle(
                                  fontSize: 10,
                                  fontWeight: FontWeight.bold,
                                  letterSpacing: 0.6,
                                  color: c)),
                          backgroundColor: c.withValues(alpha: 0.12),
                          side: BorderSide.none,
                          padding: const EdgeInsets.symmetric(horizontal: 4),
                          materialTapTargetSize:
                              MaterialTapTargetSize.shrinkWrap,
                          deleteIcon:
                              Icon(Icons.close, size: 14, color: c),
                          onDeleted: () async {
                            final p = context.read<ProductsProvider>();
                            await p.removeProductLabel(up.id, l.id);
                            await _load();
                          },
                        );
                      }).toList(),
                    ),
                  ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 12),

        // Fiyat grafiği
        if (p.priceHistories.isNotEmpty) ...[
          Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Row(
                          children: [
                            const Icon(Icons.show_chart, size: 18),
                            const SizedBox(width: 6),
                            Text(
                              'Fiyat Geçmişi',
                              style: Theme.of(context).textTheme.titleSmall,
                            ),
                          ],
                        ),
                      ),
                      TextButton.icon(
                        onPressed: () => _openAlertSettingsSheet(context, up),
                        style: TextButton.styleFrom(
                          padding: EdgeInsets.zero,
                          minimumSize: Size.zero,
                          tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                        ),
                        icon: const Icon(Icons.notifications_active_outlined, size: 15),
                        label: Text(
                          up.alertButtonLabel,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(
                            fontSize: 12,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 10),
                  SingleChildScrollView(
                    scrollDirection: Axis.horizontal,
                    child: Row(
                      children: [
                        _RangeChip(
                          label: '1H',
                          selected: _chartRange == _ChartRange.oneWeek,
                          onTap: () =>
                              setState(() => _chartRange = _ChartRange.oneWeek),
                        ),
                        const SizedBox(width: 8),
                        _RangeChip(
                          label: '1 Ay',
                          selected: _chartRange == _ChartRange.oneMonth,
                          onTap: () =>
                              setState(() => _chartRange = _ChartRange.oneMonth),
                        ),
                        const SizedBox(width: 8),
                        _RangeChip(
                          label: '3 Ay',
                          selected: _chartRange == _ChartRange.threeMonths,
                          onTap: () =>
                              setState(() => _chartRange = _ChartRange.threeMonths),
                        ),
                        const SizedBox(width: 8),
                        _RangeChip(
                          label: '6 Ay',
                          selected: _chartRange == _ChartRange.sixMonths,
                          onTap: () =>
                              setState(() => _chartRange = _ChartRange.sixMonths),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 10),
                  Padding(
                    padding: const EdgeInsets.fromLTRB(0, 0, 8, 0),
                    child: SizedBox(
                      height: 200,
                      child: _PriceChart(
                        histories: filteredHistory,
                        range: _chartRange,
                        fallbackPrice: p.currentPrice ?? p.initialPrice,
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ],

      ],
    );
  }

  void _showLabelSheet(BuildContext context, UserProduct up) {
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      builder: (_) => LabelSheet(userProductId: up.id),
    );
  }

  void _confirmDelete(BuildContext context) {
    showDialog(
      context: context,
      builder: (_) => AlertDialog(
        title: const Text('Ürünü Kaldır'),
        content: const Text(
            'Bu ürünü takip listenizden kaldırmak istediğinizden emin misiniz?'),
        actions: [
          TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('İptal')),
          TextButton(
            onPressed: () async {
              final navigator = Navigator.of(context);
              final provider = context.read<ProductsProvider>();
              Navigator.pop(context);
              await provider.deleteProduct(widget.userProductId);
              if (mounted) navigator.pop();
            },
            child: const Text('Kaldır',
                style: TextStyle(color: Colors.red)),
          ),
        ],
      ),
    );
  }

  Future<void> _openAlertSettingsSheet(BuildContext context, UserProduct up) async {
    final updated = await showAlertSettingsSheet(context, up);
    if (updated == true && mounted) {
      await _load();
    }
  }
}

enum _ChartRange {
  oneWeek,
  oneMonth,
  threeMonths,
  sixMonths,
}

/// Fiyat geçmişini filtreler:
/// - Aynı gün içinde fiyat değiştiyse her değişimi göster.
/// - Aynı gün içinde fiyat değişmediyse sadece bir kayıt göster.
List<PricePoint> _filterPriceHistory(
  List<PricePoint> all, {
  Duration? lookback,
}) {
  if (all.isEmpty) return all;
  final sorted = [...all]..sort((a, b) => a.checkedAt.compareTo(b.checkedAt));
  final cut = lookback == null ? null : DateTime.now().toLocal().subtract(lookback);
  final inRange = cut == null
      ? sorted
      : sorted.where((h) => !h.checkedAt.isBefore(cut)).toList();

  if (inRange.isEmpty) return const [];

  final result = <PricePoint>[];
  PricePoint? last;
  for (final h in inRange) {
    if (last == null) {
      result.add(h);
      last = h;
      continue;
    }
    final sameDay = last.checkedAt.year == h.checkedAt.year &&
        last.checkedAt.month == h.checkedAt.month &&
        last.checkedAt.day == h.checkedAt.day;
    final samePrice = last.price == h.price;
    if (!samePrice || !sameDay) {
      result.add(h);
      last = h;
    }
    // Aynı gün + aynı fiyat → atla
  }
  return result;
}

class _PriceChart extends StatelessWidget {
  final List<PricePoint> histories;
  final _ChartRange range;
  final double? fallbackPrice;

  const _PriceChart({
    required this.histories,
    required this.range,
    this.fallbackPrice,
  });

  Duration _durationForRange(_ChartRange selected) {
    switch (selected) {
      case _ChartRange.oneWeek:
        return const Duration(days: 7);
      case _ChartRange.oneMonth:
        return const Duration(days: 30);
      case _ChartRange.threeMonths:
        return const Duration(days: 90);
      case _ChartRange.sixMonths:
        return const Duration(days: 180);
    }
  }

  String _rangeLabel(double value) {
    final rounded = _roundToHundreds(value);
    return NumberFormat.decimalPattern('tr_TR').format(rounded);
  }

  int _roundToHundreds(double value) {
    return ((value / 100).round() * 100);
  }

  String _bottomLabel(DateTime dt) {
    switch (range) {
      case _ChartRange.oneWeek:
        return DateFormat('dd.MM').format(dt);
      case _ChartRange.oneMonth:
      case _ChartRange.threeMonths:
      case _ChartRange.sixMonths:
        return DateFormat('dd.MM').format(dt);
    }
  }

  int _tickCountForRange() {
    switch (range) {
      case _ChartRange.oneWeek:
        return 4;
      case _ChartRange.oneMonth:
        return 3;
      case _ChartRange.threeMonths:
        return 4;
      case _ChartRange.sixMonths:
        return 5;
    }
  }

  @override
  Widget build(BuildContext context) {
    final now = DateTime.now().toLocal();
    final start = now.subtract(_durationForRange(range));
    final totalMinutes = now.difference(start).inMinutes;
    final minX = 0.0;
    final maxX = totalMinutes.toDouble();

    final data = [...histories]..sort((a, b) => a.checkedAt.compareTo(b.checkedAt));

    final spots = data.isEmpty
        ? (fallbackPrice == null
            ? <FlSpot>[]
            : <FlSpot>[
                FlSpot(minX, fallbackPrice!),
                FlSpot(maxX, fallbackPrice!),
              ])
        : data
        .map((h) => FlSpot(h.checkedAt.difference(start).inMinutes.toDouble(), h.price))
            .toList();

    final yValues = spots.map((s) => s.y).toList();
    final base = fallbackPrice ?? 0;
    final minY = yValues.isEmpty
        ? base - 10
        : yValues.reduce((a, b) => a < b ? a : b);
    final maxY = yValues.isEmpty
        ? base + 10
        : yValues.reduce((a, b) => a > b ? a : b);
    var axisMinLabel = (minY / 100).floor() * 100;
    var axisMaxLabel = (maxY / 100).ceil() * 100;
    if (axisMaxLabel - axisMinLabel < 200) {
      final center = _roundToHundreds((minY + maxY) / 2);
      axisMinLabel = center - 100;
      axisMaxLabel = center + 100;
    }

    final axisMidLabel = _roundToHundreds((axisMinLabel + axisMaxLabel) / 2);
    final axisLabelValues = [axisMinLabel, axisMidLabel, axisMaxLabel];
    final chartMinY = axisMinLabel.toDouble();
    final chartMaxY = axisMaxLabel.toDouble();
    const intervalCount = 3;
    final yInterval = (chartMaxY - chartMinY) / (intervalCount - 1);

    final tickCount = _tickCountForRange();
    final xInterval = (maxX - minX) / (tickCount - 1);
    final xLabels = <String>[];
    final usedDayLabels = <String>{};
    for (int i = 0; i < tickCount; i++) {
      final x = minX + (xInterval * i);
      final dt = start.add(Duration(minutes: x.round()));
      final label = _bottomLabel(dt);
      final isDuplicate = usedDayLabels.contains(label);
      if (!isDuplicate) {
        usedDayLabels.add(label);
      }
      xLabels.add(isDuplicate ? '' : label);
    }

    return LineChart(
      LineChartData(
        minX: minX,
        maxX: maxX,
        minY: chartMinY,
        maxY: chartMaxY,
        gridData: FlGridData(
          show: true,
          drawVerticalLine: true,
          verticalInterval: xInterval,
          horizontalInterval: yInterval,
          getDrawingVerticalLine: (_) => FlLine(
            color: const Color(0xFFD1D5DB),
            strokeWidth: 1,
            dashArray: [3, 3],
          ),
          getDrawingHorizontalLine: (_) =>
              FlLine(
                color: const Color(0xFFD1D5DB),
                strokeWidth: 1,
                dashArray: [3, 3],
              ),
        ),
        borderData: FlBorderData(show: false),
        titlesData: FlTitlesData(
          topTitles:
              const AxisTitles(sideTitles: SideTitles(showTitles: false)),
          rightTitles:
              const AxisTitles(sideTitles: SideTitles(showTitles: false)),
          bottomTitles: AxisTitles(
            sideTitles: SideTitles(
              showTitles: true,
              reservedSize: 24,
              interval: xInterval,
              getTitlesWidget: (v, meta) {
                final slot = ((v - minX) / xInterval).round();
                if (slot < 0 || slot >= tickCount) {
                  return const SizedBox.shrink();
                }

                final target = minX + (xInterval * slot);
                if ((v - target).abs() > (xInterval * 0.1)) {
                  return const SizedBox.shrink();
                }

                final label = xLabels[slot];
                if (label.isEmpty) return const SizedBox.shrink();

                return Padding(
                  padding: const EdgeInsets.only(top: 4),
                  child: Text(
                    label,
                    style: const TextStyle(
                        fontSize: 8, color: Color(0xFF9CA3AF)),
                  ),
                );
              },
            ),
          ),
          leftTitles: AxisTitles(
            sideTitles: SideTitles(
              showTitles: true,
              reservedSize: 52,
              interval: yInterval,
              getTitlesWidget: (v, _) {
                // Sol eksende yalnızca: düşük, orta (ortalama), yüksek.
                final slot = ((v - chartMinY) / yInterval).round();
                if (slot < 0 || slot >= intervalCount) {
                  return const SizedBox.shrink();
                }

                final target = chartMinY + (yInterval * slot);
                if ((v - target).abs() > (yInterval * 0.45)) {
                  return const SizedBox.shrink();
                }

                final currentRounded = axisLabelValues[slot];
                final seenBefore = axisLabelValues
                    .take(slot)
                    .any((v) => v == currentRounded);
                if (seenBefore) return const SizedBox.shrink();

                return Text(
                  NumberFormat.decimalPattern('tr_TR').format(currentRounded),
                  style: const TextStyle(fontSize: 9, color: Color(0xFF9CA3AF)),
                );
              },
            ),
          ),
        ),
        lineBarsData: [
          if (spots.isNotEmpty)
            LineChartBarData(
              spots: spots,
              isCurved: false,
              color: Theme.of(context).colorScheme.primary,
              barWidth: 2.5,
              dotData: const FlDotData(show: false),
              belowBarData: BarAreaData(
                show: true,
                color: Theme.of(context).colorScheme.primary.withValues(alpha: 0.08),
              ),
            ),
        ],
        lineTouchData: LineTouchData(
          touchTooltipData: LineTouchTooltipData(
            getTooltipItems: (spots) => spots
                .map((s) => LineTooltipItem(
                      '${_rangeLabel(s.y)} ₺\n${DateFormat('dd.MM HH:mm').format(start.add(Duration(minutes: s.x.toInt())))}',
                      const TextStyle(color: Colors.white, fontSize: 11),
                    ))
                .toList(),
          ),
        ),
      ),
    );
  }
}

class _RangeChip extends StatelessWidget {
  final String label;
  final bool selected;
  final VoidCallback onTap;

  const _RangeChip({
    required this.label,
    required this.selected,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(16),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
        decoration: BoxDecoration(
          color: selected ? cs.primary.withValues(alpha: 0.14) : Colors.transparent,
          borderRadius: BorderRadius.circular(16),
          border: Border.all(
            color: selected ? cs.primary : cs.outline.withValues(alpha: 0.4),
          ),
        ),
        child: Text(
          label,
          style: TextStyle(
            fontSize: 11,
            fontWeight: FontWeight.w600,
            color: selected ? cs.primary : cs.onSurface.withValues(alpha: 0.75),
          ),
        ),
      ),
    );
  }
}
