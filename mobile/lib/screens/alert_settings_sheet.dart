import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../models/product.dart';
import '../providers/products_provider.dart';

Future<bool?> showAlertSettingsSheet(
  BuildContext context,
  UserProduct userProduct,
) {
  return showModalBottomSheet<bool>(
    context: context,
    isScrollControlled: true,
    builder: (_) => AlertSettingsSheet(userProduct: userProduct),
  );
}

class AlertSettingsSheet extends StatefulWidget {
  final UserProduct userProduct;

  const AlertSettingsSheet({super.key, required this.userProduct});

  @override
  State<AlertSettingsSheet> createState() => _AlertSettingsSheetState();
}

class _AlertSettingsSheetState extends State<AlertSettingsSheet> {
  late String _alertMode;
  late final TextEditingController _percentageCtrl;
  late final TextEditingController _targetPriceCtrl;
  bool _saving = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _alertMode = widget.userProduct.normalizedAlertMode;
    _percentageCtrl = TextEditingController(
      text: widget.userProduct.discountThresholdPercent?.toStringAsFixed(2) ?? '',
    );
    _targetPriceCtrl = TextEditingController(
      text: widget.userProduct.targetPrice?.toStringAsFixed(2) ?? '',
    );
  }

  @override
  void dispose() {
    _percentageCtrl.dispose();
    _targetPriceCtrl.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    final navigator = Navigator.of(context);

    setState(() {
      _saving = true;
      _error = null;
    });

    double? discountThresholdPercent;
    double? targetPrice;

    switch (_alertMode) {
      case userProductAlertModePercentage:
        discountThresholdPercent =
            double.tryParse(_percentageCtrl.text.trim().replaceAll(',', '.'));
        if (discountThresholdPercent == null) {
          setState(() {
            _saving = false;
            _error = 'İndirim yüzdesi gerekli.';
          });
          return;
        }
        if (discountThresholdPercent <= 0 || discountThresholdPercent > 100) {
          setState(() {
            _saving = false;
            _error = 'İndirim yüzdesi 0 ile 100 arasında olmalı.';
          });
          return;
        }
        break;

      case userProductAlertModeTargetPrice:
        targetPrice =
            double.tryParse(_targetPriceCtrl.text.trim().replaceAll(',', '.'));
        if (targetPrice == null) {
          setState(() {
            _saving = false;
            _error = 'Hedef fiyat gerekli.';
          });
          return;
        }
        if (targetPrice <= 0) {
          setState(() {
            _saving = false;
            _error = 'Hedef fiyat 0\'dan büyük olmalı.';
          });
          return;
        }
        break;
    }

    final err = await context.read<ProductsProvider>().updateAlertSettings(
          widget.userProduct.id,
          alertMode: _alertMode,
          discountThresholdPercent: discountThresholdPercent,
          targetPrice: targetPrice,
        );

    if (!mounted) return;

    if (err != null) {
      setState(() {
        _saving = false;
        _error = err;
      });
      return;
    }

    navigator.pop(true);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return SafeArea(
      child: Padding(
        padding: EdgeInsets.only(
          left: 20,
          right: 20,
          top: 20,
          bottom: MediaQuery.of(context).viewInsets.bottom + 20,
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Alarm Ayarı', style: theme.textTheme.titleLarge),
            const SizedBox(height: 8),
            Text(
              'Yeni ürünler varsayılan olarak otomatik takip ile başlar. İstersen daha agresif bir yüzde alarmı ya da sabit hedef fiyat seçebilirsin.',
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.onSurface.withValues(alpha: 0.7),
              ),
            ),
            const SizedBox(height: 16),
            _ModeOptionCard(
              title: 'Otomatik takip',
              subtitle:
                  'Mevcut davranış: fiyat yeni 7 günlük dip seviyeye yeterince indiğinde bildir.',
              selected: _alertMode == userProductAlertModeAutomatic,
              enabled: !_saving,
              onTap: () => setState(
                () => _alertMode = userProductAlertModeAutomatic,
              ),
            ),
            _ModeOptionCard(
              title: 'Belirli indirim yüzdesi',
              subtitle:
                  '7 günlük en düşük fiyata göre istediğin yüzde kadar düşünce bildir.',
              selected: _alertMode == userProductAlertModePercentage,
              enabled: !_saving,
              onTap: () => setState(
                () => _alertMode = userProductAlertModePercentage,
              ),
            ),
            if (_alertMode == userProductAlertModePercentage) ...[
              const SizedBox(height: 8),
              TextField(
                controller: _percentageCtrl,
                keyboardType:
                    const TextInputType.numberWithOptions(decimal: true),
                decoration: const InputDecoration(
                  labelText: 'İndirim yüzdesi',
                  prefixText: '% ',
                  border: OutlineInputBorder(),
                ),
              ),
            ],
            const SizedBox(height: 8),
            _ModeOptionCard(
              title: 'Sabit hedef fiyat',
              subtitle:
                  'Fiyat belirlediğin tutara indiğinde ya da altına düştüğünde bildir.',
              selected: _alertMode == userProductAlertModeTargetPrice,
              enabled: !_saving,
              onTap: () => setState(
                () => _alertMode = userProductAlertModeTargetPrice,
              ),
            ),
            if (_alertMode == userProductAlertModeTargetPrice) ...[
              const SizedBox(height: 8),
              TextField(
                controller: _targetPriceCtrl,
                keyboardType:
                    const TextInputType.numberWithOptions(decimal: true),
                decoration: const InputDecoration(
                  labelText: 'Hedef fiyat',
                  prefixText: '₺ ',
                  border: OutlineInputBorder(),
                ),
              ),
            ],
            if (_error != null) ...[
              const SizedBox(height: 8),
              Text(
                _error!,
                style: TextStyle(
                  color: theme.colorScheme.error,
                  fontSize: 12,
                ),
              ),
            ],
            const SizedBox(height: 16),
            Row(
              children: [
                Expanded(
                  child: OutlinedButton(
                    onPressed: _saving ? null : () => Navigator.of(context).pop(false),
                    child: const Text('İptal'),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: FilledButton(
                    onPressed: _saving ? null : _save,
                    child: _saving
                        ? const SizedBox(
                            width: 20,
                            height: 20,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Text('Kaydet'),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _ModeOptionCard extends StatelessWidget {
  final String title;
  final String subtitle;
  final bool selected;
  final bool enabled;
  final VoidCallback onTap;

  const _ModeOptionCard({
    required this.title,
    required this.subtitle,
    required this.selected,
    required this.enabled,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          onTap: enabled ? onTap : null,
          borderRadius: BorderRadius.circular(12),
          child: AnimatedContainer(
            duration: const Duration(milliseconds: 160),
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(12),
              border: Border.all(
                color: selected
                    ? colorScheme.primary
                    : colorScheme.outline.withValues(alpha: 0.35),
              ),
              color: selected
                  ? colorScheme.primaryContainer.withValues(alpha: 0.5)
                  : colorScheme.surface,
            ),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(
                  selected
                      ? Icons.radio_button_checked
                      : Icons.radio_button_off,
                  size: 20,
                  color: selected
                      ? colorScheme.primary
                      : colorScheme.onSurface.withValues(alpha: 0.45),
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(title, style: theme.textTheme.titleSmall),
                      const SizedBox(height: 2),
                      Text(
                        subtitle,
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: colorScheme.onSurface.withValues(alpha: 0.72),
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}