import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import '../models/product.dart';
import '../providers/products_provider.dart';

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

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    _up = await context
        .read<ProductsProvider>()
        .fetchById(widget.userProductId);
    setState(() => _loading = false);
  }

  Future<void> _manualCheck() async {
    setState(() => _checkLoading = true);
    await context.read<ProductsProvider>().manualCheck(widget.userProductId);
    await Future.delayed(const Duration(seconds: 2)); // job biraz sürer
    await _load();
    setState(() => _checkLoading = false);
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
    final cs = Theme.of(context).colorScheme;
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
                if (p.imageUrl != null)
                  Center(
                    child: ClipRRect(
                      borderRadius: BorderRadius.circular(12),
                      child: Image.network(
                        p.imageUrl!,
                        height: 180,
                        fit: BoxFit.contain,
                        errorBuilder: (_, __, ___) => const SizedBox.shrink(),
                      ),
                    ),
                  ),
                if (p.imageUrl != null) const SizedBox(height: 12),
                Text(p.name.isEmpty ? p.url : p.name,
                    style: const TextStyle(
                        fontSize: 16, fontWeight: FontWeight.w600)),
                if (p.store != null) ...[
                  const SizedBox(height: 4),
                  Text(p.store!,
                      style: TextStyle(color: cs.onSurface.withOpacity(.6))),
                ],
                const SizedBox(height: 16),
                Row(
                  children: [
                    _PriceTile(
                        label: 'Güncel Fiyat',
                        value: p.currentPrice,
                        color: isPriceDrop ? Colors.green : null),
                    const SizedBox(width: 16),
                    _PriceTile(
                        label: 'Başlangıç Fiyatı', value: p.initialPrice),
                    const SizedBox(width: 16),
                    _PriceTile(
                        label: 'Hedef Fiyat',
                        value: up.targetPrice,
                        color: cs.primary),
                  ],
                ),
                if (p.lastCheckedAt != null) ...[
                  const SizedBox(height: 8),
                  Text(
                    'Son kontrol: ${DateFormat('dd.MM.yyyy HH:mm').format(p.lastCheckedAt!)}',
                    style: TextStyle(
                        fontSize: 12, color: cs.onSurface.withOpacity(.5)),
                  ),
                ],
              ],
            ),
          ),
        ),
        const SizedBox(height: 12),

        // Hedef fiyat güncelle
        Card(
          child: ListTile(
            leading: const Icon(Icons.local_offer_outlined),
            title: const Text('Hedef Fiyatı Güncelle'),
            trailing: const Icon(Icons.chevron_right),
            onTap: () => _showTargetPriceDialog(context, up),
          ),
        ),
        const SizedBox(height: 12),

        // Fiyat grafiği
        if (p.priceHistories.isNotEmpty) ...[
          Text('Fiyat Geçmişi',
              style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          Card(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(8, 16, 16, 8),
              child: SizedBox(
                height: 200,
                child: _PriceChart(histories: p.priceHistories),
              ),
            ),
          ),
          const SizedBox(height: 12),

          // Fiyat geçmişi listesi
          Text('Kayıtlar', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          ...p.priceHistories.take(20).map((h) => ListTile(
                dense: true,
                title: Text('₺${h.price.toStringAsFixed(2)}'),
                subtitle: Text(
                    DateFormat('dd.MM.yyyy HH:mm').format(h.checkedAt)),
              )),
        ],
      ],
    );
  }

  void _showTargetPriceDialog(BuildContext context, UserProduct up) {
    final ctrl = TextEditingController(
        text: up.targetPrice?.toStringAsFixed(2) ?? '');

    showDialog(
      context: context,
      builder: (_) => AlertDialog(
        title: const Text('Hedef Fiyat'),
        content: TextField(
          controller: ctrl,
          keyboardType:
              const TextInputType.numberWithOptions(decimal: true),
          decoration: const InputDecoration(
              prefixText: '₺ ', border: OutlineInputBorder()),
          autofocus: true,
        ),
        actions: [
          TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('İptal')),
          FilledButton(
            onPressed: () async {
              final price = ctrl.text.isNotEmpty
                  ? double.tryParse(ctrl.text.replaceAll(',', '.'))
                  : null;
              Navigator.pop(context);
              await context
                  .read<ProductsProvider>()
                  .updateTargetPrice(up.id, price);
              await _load();
            },
            child: const Text('Kaydet'),
          ),
        ],
      ),
    );
  }
}

class _PriceTile extends StatelessWidget {
  final String label;
  final double? value;
  final Color? color;

  const _PriceTile({required this.label, this.value, this.color});

  @override
  Widget build(BuildContext context) {
    return Expanded(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label,
              style: TextStyle(
                  fontSize: 11,
                  color: Theme.of(context).colorScheme.onSurface.withOpacity(.6))),
          const SizedBox(height: 2),
          Text(
            value != null ? '₺${value!.toStringAsFixed(2)}' : '—',
            style: TextStyle(
                fontSize: 15,
                fontWeight: FontWeight.bold,
                color: color),
          ),
        ],
      ),
    );
  }
}

class _PriceChart extends StatelessWidget {
  final List<PricePoint> histories;
  const _PriceChart({required this.histories});

  @override
  Widget build(BuildContext context) {
    // En son 30 veri noktasını göster (eski → yeni)
    final data = histories.reversed.take(30).toList().reversed.toList();
    if (data.isEmpty) return const SizedBox.shrink();

    final spots = data.asMap().entries.map((e) {
      return FlSpot(e.key.toDouble(), e.value.price);
    }).toList();

    final minY = data.map((h) => h.price).reduce((a, b) => a < b ? a : b);
    final maxY = data.map((h) => h.price).reduce((a, b) => a > b ? a : b);
    final padding = (maxY - minY) * 0.1 + 1;

    return LineChart(
      LineChartData(
        minY: minY - padding,
        maxY: maxY + padding,
        gridData: const FlGridData(show: true),
        borderData: FlBorderData(show: false),
        titlesData: FlTitlesData(
          topTitles:
              const AxisTitles(sideTitles: SideTitles(showTitles: false)),
          rightTitles:
              const AxisTitles(sideTitles: SideTitles(showTitles: false)),
          bottomTitles:
              const AxisTitles(sideTitles: SideTitles(showTitles: false)),
          leftTitles: AxisTitles(
            sideTitles: SideTitles(
              showTitles: true,
              reservedSize: 52,
              getTitlesWidget: (v, _) => Text(
                '₺${v.toInt()}',
                style: const TextStyle(fontSize: 10),
              ),
            ),
          ),
        ),
        lineBarsData: [
          LineChartBarData(
            spots: spots,
            isCurved: true,
            color: Theme.of(context).colorScheme.primary,
            barWidth: 2.5,
            dotData: const FlDotData(show: false),
            belowBarData: BarAreaData(
              show: true,
              color: Theme.of(context).colorScheme.primary.withOpacity(0.1),
            ),
          ),
        ],
        lineTouchData: LineTouchData(
          touchTooltipData: LineTouchTooltipData(
            getTooltipItems: (spots) => spots
                .map((s) => LineTooltipItem(
                      '₺${s.y.toStringAsFixed(2)}\n${DateFormat('dd.MM HH:mm').format(data[s.x.toInt()].checkedAt)}',
                      const TextStyle(color: Colors.white, fontSize: 11),
                    ))
                .toList(),
          ),
        ),
      ),
    );
  }
}
