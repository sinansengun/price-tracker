import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import '../models/product.dart';
import '../providers/products_provider.dart';

final _priceFormat = NumberFormat('#,##0.00', 'tr_TR');

/// "100.000,34" formatında string döndürür
String fmtPrice(double v) => _priceFormat.format(v);

/// Ana rakam normal, ",XX TL" kısmı daha küçük
class PriceText extends StatelessWidget {
  final double value;
  final double fontSize;
  final FontWeight fontWeight;
  final Color? color;

  const PriceText({
    super.key,
    required this.value,
    this.fontSize = 15,
    this.fontWeight = FontWeight.bold,
    this.color,
  });

  @override
  Widget build(BuildContext context) {
    final formatted = fmtPrice(value);
    final commaIdx = formatted.indexOf(',');
    final intPart = commaIdx >= 0 ? formatted.substring(0, commaIdx) : formatted;
    final decPart = commaIdx >= 0 ? formatted.substring(commaIdx) : '';
    final effectiveColor =
        color ?? DefaultTextStyle.of(context).style.color;

    return RichText(
      text: TextSpan(
        style: TextStyle(
            fontSize: fontSize,
            fontWeight: fontWeight,
            color: effectiveColor),
        children: [
          TextSpan(text: intPart),
          if (decPart.isNotEmpty)
            TextSpan(
              text: '$decPart ₺',
              style: TextStyle(
                  fontSize: fontSize * 0.72,
                  fontWeight: fontWeight,
                  color: effectiveColor),
            ),
          if (decPart.isEmpty)
            const TextSpan(text: ' ₺'),
        ],
      ),
    );
  }
}

Color? hexColor(String hex) {
  try {
    final h = hex.replaceAll('#', '');
    final val = int.parse(h.length == 6 ? 'FF$h' : h, radix: 16);
    return Color(val);
  } catch (_) {
    return null;
  }
}

String? faviconUrl(String url) {
  try {
    final host = Uri.parse(url).host;
    if (host.isEmpty) return null;
    return 'https://www.google.com/s2/favicons?domain=$host&sz=32';
  } catch (_) {
    return null;
  }
}

class StoreBadge extends StatelessWidget {
  final String store;
  final String url;
  const StoreBadge({super.key, required this.store, required this.url});

  @override
  Widget build(BuildContext context) {
    final favicon = faviconUrl(url);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 3),
      decoration: BoxDecoration(
        color: Theme.of(context).colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(20),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (favicon != null) ...[
            Image.network(
              favicon,
              width: 14,
              height: 14,
              errorBuilder: (_, __, ___) => const SizedBox.shrink(),
            ),
            const SizedBox(width: 4),
          ],
          Text(store,
              style: TextStyle(
                  fontSize: 11,
                  fontWeight: FontWeight.w500,
                  color: Theme.of(context).colorScheme.onSurfaceVariant)),
        ],
      ),
    );
  }
}

class LabelSheet extends StatefulWidget {
  final int userProductId;
  const LabelSheet({super.key, required this.userProductId});

  @override
  State<LabelSheet> createState() => _LabelSheetState();
}

class _LabelSheetState extends State<LabelSheet> {
  final _nameCtrl = TextEditingController();
  String _pickedColor = '#6366f1';
  bool _saving = false;

  static const _palette = [
    '#6366f1', '#ec4899', '#f97316', '#eab308',
    '#22c55e', '#14b8a6', '#3b82f6', '#8b5cf6',
  ];

  @override
  void dispose() {
    _nameCtrl.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<ProductsProvider>();
    final allLabels = provider.labels;
    final up = provider.products
        .cast<UserProduct?>()
        .firstWhere((p) => p?.id == widget.userProductId, orElse: () => null);
    final attached = up?.labels ?? [];

    return Padding(
      padding:
          EdgeInsets.only(bottom: MediaQuery.of(context).viewInsets.bottom),
      child: SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
              child: Text('Etiketler',
                  style: Theme.of(context).textTheme.titleMedium),
            ),
            if (allLabels.isNotEmpty) ...[
              ...allLabels.map((l) {
                final c = hexColor(l.color) ?? Colors.indigo;
                final isAttached = attached.any((a) => a.id == l.id);
                return ListTile(
                  dense: true,
                  leading: Container(
                      width: 14,
                      height: 14,
                      decoration: BoxDecoration(
                          color: c, borderRadius: BorderRadius.circular(3))),
                  title: Text(l.name, style: const TextStyle(fontSize: 14)),
                  trailing: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      if (isAttached)
                        const Icon(Icons.check,
                            color: Colors.green, size: 20),
                      IconButton(
                        icon: const Icon(Icons.delete_outline, size: 18),
                        color: Colors.red.shade300,
                        onPressed: () async {
                          await context
                              .read<ProductsProvider>()
                              .deleteLabel(l.id);
                        },
                      ),
                    ],
                  ),
                  onTap: () async {
                    final p = context.read<ProductsProvider>();
                    if (isAttached) {
                      await p.removeProductLabel(widget.userProductId, l.id);
                    } else {
                      await p.addProductLabel(widget.userProductId, l.id);
                    }
                  },
                );
              }),
              const Divider(height: 1),
            ],
            Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Yeni etiket',
                      style: Theme.of(context)
                          .textTheme
                          .labelMedium
                          ?.copyWith(color: Colors.grey)),
                  const SizedBox(height: 8),
                  Row(
                    children: [
                      Expanded(
                        child: TextField(
                          controller: _nameCtrl,
                          decoration: const InputDecoration(
                            hintText: 'Etiket adı',
                            isDense: true,
                            border: OutlineInputBorder(),
                            contentPadding: EdgeInsets.symmetric(
                                horizontal: 12, vertical: 10),
                          ),
                        ),
                      ),
                      const SizedBox(width: 8),
                      GestureDetector(
                        onTap: () => _pickColor(context),
                        child: Container(
                          width: 36,
                          height: 36,
                          decoration: BoxDecoration(
                            color: hexColor(_pickedColor) ?? Colors.indigo,
                            borderRadius: BorderRadius.circular(8),
                            border:
                                Border.all(color: Colors.grey.shade300),
                          ),
                        ),
                      ),
                      const SizedBox(width: 8),
                      FilledButton(
                        onPressed: _saving ? null : _createLabel,
                        child: _saving
                            ? const SizedBox(
                                width: 16,
                                height: 16,
                                child: CircularProgressIndicator(
                                    strokeWidth: 2))
                            : const Text('Ekle'),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _createLabel() async {
    final name = _nameCtrl.text.trim();
    if (name.isEmpty) return;
    setState(() => _saving = true);
    final provider = context.read<ProductsProvider>();
    final label = await provider.createLabel(name, _pickedColor);
    if (label != null && mounted) {
      await provider.addProductLabel(widget.userProductId, label.id);
      _nameCtrl.clear();
    }
    if (mounted) setState(() => _saving = false);
  }

  void _pickColor(BuildContext context) {
    showDialog(
      context: context,
      builder: (_) => AlertDialog(
        title: const Text('Renk Seç'),
        content: Wrap(
          spacing: 10,
          runSpacing: 10,
          children: _palette.map((hex) {
            final c = hexColor(hex)!;
            final selected = hex == _pickedColor;
            return GestureDetector(
              onTap: () {
                setState(() => _pickedColor = hex);
                Navigator.pop(context);
              },
              child: Container(
                width: 36,
                height: 36,
                decoration: BoxDecoration(
                  color: c,
                  shape: BoxShape.circle,
                  border: selected
                      ? Border.all(width: 3, color: Colors.black87)
                      : null,
                ),
              ),
            );
          }).toList(),
        ),
      ),
    );
  }
}
