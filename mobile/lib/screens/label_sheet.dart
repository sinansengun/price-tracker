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
  final _searchCtrl = TextEditingController();
  String _pickedColor = '#6366f1';
  bool _saving = false;

  static const _palette = [
    '#6366f1', '#ec4899', '#f97316', '#eab308',
    '#22c55e', '#14b8a6', '#3b82f6', '#8b5cf6',
  ];

  bool get _canCreate => !_saving && _nameCtrl.text.trim().isNotEmpty;

  @override
  void initState() {
    super.initState();
    _nameCtrl.addListener(_onInputChanged);
    _searchCtrl.addListener(_onInputChanged);
  }

  void _onInputChanged() {
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
    _nameCtrl.removeListener(_onInputChanged);
    _searchCtrl.removeListener(_onInputChanged);
    _nameCtrl.dispose();
    _searchCtrl.dispose();
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
    final attachedIds = attached.map((l) => l.id).toSet();
    final query = _searchCtrl.text.trim().toLowerCase();
    final visibleLabels = allLabels
        .where((l) => l.name.toLowerCase().contains(query))
        .toList()
      ..sort((a, b) {
        final aAttached = attachedIds.contains(a.id);
        final bAttached = attachedIds.contains(b.id);
        if (aAttached != bAttached) {
          return aAttached ? -1 : 1;
        }
        return a.name.toLowerCase().compareTo(b.name.toLowerCase());
      });
    final cs = Theme.of(context).colorScheme;
    final subtitle = allLabels.isEmpty
        ? 'Henüz etiket yok. İlk etiketi aşağıdan oluşturabilirsin.'
        : '${attached.length}/${allLabels.length} etiket bu ürüne bağlı';

    return AnimatedPadding(
      duration: const Duration(milliseconds: 180),
      curve: Curves.easeOut,
      padding: EdgeInsets.only(bottom: MediaQuery.of(context).viewInsets.bottom),
      child: SafeArea(
        top: false,
        child: ConstrainedBox(
          constraints: BoxConstraints(
            maxHeight: MediaQuery.of(context).size.height * 0.82,
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const SizedBox(height: 8),
              Center(
                child: Container(
                  width: 38,
                  height: 4,
                  decoration: BoxDecoration(
                    color: cs.outlineVariant.withValues(alpha: 0.8),
                    borderRadius: BorderRadius.circular(12),
                  ),
                ),
              ),
              const SizedBox(height: 10),
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 0, 16, 0),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Icon(Icons.label_outline, size: 18, color: cs.primary),
                        const SizedBox(width: 8),
                        Text(
                          'Etiketleri Yönet',
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                      ],
                    ),
                    const SizedBox(height: 4),
                    Text(
                      subtitle,
                      style: Theme.of(context).textTheme.bodySmall?.copyWith(
                        color: cs.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 12),
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 16),
                child: TextField(
                  controller: _searchCtrl,
                  textInputAction: TextInputAction.search,
                  decoration: InputDecoration(
                    hintText: 'Etiket ara...',
                    prefixIcon: const Icon(Icons.search, size: 20),
                    suffixIcon: query.isEmpty
                        ? null
                        : IconButton(
                            tooltip: 'Temizle',
                            onPressed: () => _searchCtrl.clear(),
                            icon: const Icon(Icons.close, size: 18),
                          ),
                    isDense: true,
                    border: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(12),
                    ),
                  ),
                ),
              ),
              const SizedBox(height: 10),
              Expanded(
                child: allLabels.isEmpty
                    ? _buildEmptyState(
                        icon: Icons.sell_outlined,
                        title: 'Henüz etiket yok',
                        subtitle: 'Aşağıdan ilk etiketini oluşturabilirsin.',
                      )
                    : visibleLabels.isEmpty
                        ? _buildEmptyState(
                            icon: Icons.search_off_outlined,
                            title: 'Eşleşen etiket bulunamadı',
                            subtitle: 'Aramayı temizleyip tekrar dene.',
                          )
                        : ListView.separated(
                            padding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
                            itemCount: visibleLabels.length,
                            separatorBuilder: (_, __) => const SizedBox(height: 8),
                            itemBuilder: (_, i) {
                              final l = visibleLabels[i];
                              final c = hexColor(l.color) ?? Colors.indigo;
                              final isAttached = attachedIds.contains(l.id);

                              return Material(
                                color: Colors.transparent,
                                child: InkWell(
                                  borderRadius: BorderRadius.circular(14),
                                  onTap: () => _toggleLabel(l, isAttached),
                                  child: Ink(
                                    padding: const EdgeInsets.symmetric(
                                      horizontal: 12,
                                      vertical: 10,
                                    ),
                                    decoration: BoxDecoration(
                                      borderRadius: BorderRadius.circular(14),
                                      color: isAttached
                                          ? c.withValues(alpha: 0.1)
                                          : cs.surface,
                                      border: Border.all(
                                        color: isAttached
                                            ? c.withValues(alpha: 0.45)
                                            : cs.outlineVariant.withValues(alpha: 0.7),
                                      ),
                                    ),
                                    child: Row(
                                      children: [
                                        Container(
                                          width: 11,
                                          height: 11,
                                          decoration: BoxDecoration(
                                            color: c,
                                            shape: BoxShape.circle,
                                          ),
                                        ),
                                        const SizedBox(width: 10),
                                        Expanded(
                                          child: Column(
                                            crossAxisAlignment: CrossAxisAlignment.start,
                                            children: [
                                              Text(
                                                l.name.toUpperCase(),
                                                maxLines: 1,
                                                overflow: TextOverflow.ellipsis,
                                                style: TextStyle(
                                                  fontSize: 13,
                                                  fontWeight: FontWeight.w700,
                                                  color: c,
                                                ),
                                              ),
                                              const SizedBox(height: 2),
                                              Text(
                                                isAttached
                                                    ? 'Bu ürüne eklendi'
                                                    : 'Eklemek için dokun',
                                                style: Theme.of(context)
                                                    .textTheme
                                                    .bodySmall
                                                    ?.copyWith(
                                                      color: cs.onSurfaceVariant,
                                                    ),
                                              ),
                                            ],
                                          ),
                                        ),
                                        if (isAttached)
                                          Container(
                                            margin: const EdgeInsets.only(right: 4),
                                            padding: const EdgeInsets.symmetric(
                                              horizontal: 8,
                                              vertical: 4,
                                            ),
                                            decoration: BoxDecoration(
                                              color: Colors.green.withValues(alpha: 0.12),
                                              borderRadius: BorderRadius.circular(999),
                                            ),
                                            child: const Text(
                                              'Eklendi',
                                              style: TextStyle(
                                                fontSize: 10,
                                                color: Colors.green,
                                                fontWeight: FontWeight.w700,
                                              ),
                                            ),
                                          ),
                                        IconButton(
                                          tooltip: 'Etiketi sil',
                                          visualDensity: VisualDensity.compact,
                                          icon: const Icon(Icons.delete_outline, size: 18),
                                          color: Colors.red.shade300,
                                          onPressed: () => _confirmDeleteLabel(l),
                                        ),
                                      ],
                                    ),
                                  ),
                                ),
                              );
                            },
                          ),
              ),
              const Divider(height: 1),
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 12, 16, 16),
                child: Container(
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    borderRadius: BorderRadius.circular(14),
                    color: cs.surfaceContainerHighest.withValues(alpha: 0.35),
                    border: Border.all(
                      color: cs.outlineVariant.withValues(alpha: 0.75),
                    ),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'Yeni etiket',
                        style: Theme.of(context).textTheme.labelLarge?.copyWith(
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      const SizedBox(height: 3),
                      Text(
                        'Seçtiğin renk: ${_pickedColor.toUpperCase()}',
                        style: Theme.of(context).textTheme.bodySmall?.copyWith(
                          color: cs.onSurfaceVariant,
                        ),
                      ),
                      const SizedBox(height: 10),
                      Row(
                        children: [
                          Expanded(
                            child: TextField(
                              controller: _nameCtrl,
                              textInputAction: TextInputAction.done,
                              onSubmitted: (_) {
                                if (_canCreate) _createLabel();
                              },
                              decoration: InputDecoration(
                                hintText: 'Etiket adı',
                                isDense: true,
                                border: OutlineInputBorder(
                                  borderRadius: BorderRadius.circular(10),
                                ),
                                contentPadding: const EdgeInsets.symmetric(
                                  horizontal: 12,
                                  vertical: 11,
                                ),
                              ),
                            ),
                          ),
                          const SizedBox(width: 8),
                          InkWell(
                            onTap: () => _pickColor(context),
                            borderRadius: BorderRadius.circular(10),
                            child: Container(
                              width: 42,
                              height: 42,
                              decoration: BoxDecoration(
                                color: hexColor(_pickedColor) ?? Colors.indigo,
                                borderRadius: BorderRadius.circular(10),
                                border: Border.all(
                                  color: cs.outlineVariant,
                                ),
                              ),
                              child: const Icon(Icons.palette_outlined,
                                  size: 18, color: Colors.white),
                            ),
                          ),
                          const SizedBox(width: 8),
                          FilledButton(
                            onPressed: _canCreate ? _createLabel : null,
                            style: FilledButton.styleFrom(
                              minimumSize: const Size(68, 42),
                              padding: const EdgeInsets.symmetric(horizontal: 14),
                            ),
                            child: _saving
                                ? const SizedBox(
                                    width: 16,
                                    height: 16,
                                    child: CircularProgressIndicator(strokeWidth: 2),
                                  )
                                : const Text('Ekle'),
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildEmptyState({
    required IconData icon,
    required String title,
    required String subtitle,
  }) {
    final cs = Theme.of(context).colorScheme;
    return Center(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 16),
        child: Container(
          width: double.infinity,
          padding: const EdgeInsets.all(18),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(14),
            border: Border.all(color: cs.outlineVariant.withValues(alpha: 0.7)),
            color: cs.surfaceContainerHighest.withValues(alpha: 0.22),
          ),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(icon, size: 22, color: cs.onSurfaceVariant),
              const SizedBox(height: 8),
              Text(
                title,
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.labelLarge?.copyWith(
                  fontWeight: FontWeight.w700,
                ),
              ),
              const SizedBox(height: 3),
              Text(
                subtitle,
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  color: cs.onSurfaceVariant,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Future<void> _toggleLabel(Label label, bool isAttached) async {
    final p = context.read<ProductsProvider>();
    if (isAttached) {
      await p.removeProductLabel(widget.userProductId, label.id);
    } else {
      await p.addProductLabel(widget.userProductId, label.id);
    }
  }

  Future<void> _confirmDeleteLabel(Label label) async {
    final provider = context.read<ProductsProvider>();
    final usedCount = provider.products
        .where((p) => p.labels.any((l) => l.id == label.id))
        .length;

    final approved = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Etiketi Sil'),
        content: Text(
          '"${label.name.toUpperCase()}" etiketi silinsin mi?\n'
          'Bu etiket $usedCount üründen de kaldırılacak.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('İptal'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(dialogContext, true),
            style: FilledButton.styleFrom(backgroundColor: Colors.red),
            child: const Text('Sil'),
          ),
        ],
      ),
    );

    if (approved != true) return;

    await provider.deleteLabel(label.id);
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text('"${label.name.toUpperCase()}" etiketi silindi.')),
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
    } else if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Etiket oluşturulamadı. Tekrar dene.')),
      );
    }
    if (mounted) setState(() => _saving = false);
  }

  void _pickColor(BuildContext context) {
    showDialog(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Renk Seç'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Container(
                  width: 16,
                  height: 16,
                  decoration: BoxDecoration(
                    color: hexColor(_pickedColor) ?? Colors.indigo,
                    shape: BoxShape.circle,
                  ),
                ),
                const SizedBox(width: 8),
                Text('Seçili renk: ${_pickedColor.toUpperCase()}'),
              ],
            ),
            const SizedBox(height: 14),
            Wrap(
              spacing: 10,
              runSpacing: 10,
              children: _palette.map((hex) {
                final c = hexColor(hex)!;
                final selected = hex == _pickedColor;
                return Tooltip(
                  message: hex.toUpperCase(),
                  child: InkWell(
                    borderRadius: BorderRadius.circular(999),
                    onTap: () {
                      setState(() => _pickedColor = hex);
                      Navigator.pop(dialogContext);
                    },
                    child: Container(
                      width: 38,
                      height: 38,
                      decoration: BoxDecoration(
                        color: c,
                        shape: BoxShape.circle,
                        border: Border.all(
                          width: selected ? 3 : 1.5,
                          color: selected ? Colors.black87 : Colors.white,
                        ),
                      ),
                    ),
                  ),
                );
              }).toList(),
            ),
          ],
        ),
      ),
    );
  }
}
