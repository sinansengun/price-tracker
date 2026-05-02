import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../models/app_notification.dart';
import '../providers/notifications_provider.dart';

class NotificationsScreen extends StatefulWidget {
  const NotificationsScreen({super.key});

  @override
  State<NotificationsScreen> createState() => _NotificationsScreenState();
}

class _NotificationsScreenState extends State<NotificationsScreen> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      context.read<NotificationsProvider>().fetch();
    });
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<NotificationsProvider>();

    return Scaffold(
      appBar: AppBar(
        title: const Text('Bildirimlerim'),
      ),
      body: provider.loading && provider.items.isEmpty
          ? const Center(child: CircularProgressIndicator())
          : provider.error != null
              ? Center(
                  child: Padding(
                    padding: const EdgeInsets.all(20),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Text(provider.error!, textAlign: TextAlign.center),
                        const SizedBox(height: 12),
                        FilledButton(
                          onPressed: () => provider.fetch(),
                          child: const Text('Tekrar Dene'),
                        ),
                      ],
                    ),
                  ),
                )
              : RefreshIndicator(
                  onRefresh: () => provider.fetch(),
                  child: provider.items.isEmpty
                      ? ListView(
                          children: const [
                            SizedBox(height: 120),
                            Center(child: Text('Henüz bildirim yok.')),
                          ],
                        )
                      : ListView.separated(
                          padding: const EdgeInsets.fromLTRB(12, 12, 12, 20),
                          itemCount: provider.items.length,
                          separatorBuilder: (_, __) => const SizedBox(height: 8),
                          itemBuilder: (context, index) {
                            final item = provider.items[index];
                            return _NotificationCard(item: item);
                          },
                        ),
                ),
    );
  }
}

class _NotificationCard extends StatelessWidget {
  final AppNotification item;

  const _NotificationCard({required this.item});

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final statusColor = item.isSuccess ? Colors.green : cs.error;

    return Card(
      child: InkWell(
        borderRadius: BorderRadius.circular(12),
        onTap: item.userProductId == null
            ? null
            : () => context.push('/products/${item.userProductId}'),
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Icon(
                    item.isSuccess
                        ? Icons.notifications_active_outlined
                        : Icons.error_outline,
                    color: statusColor,
                    size: 18,
                  ),
                  const SizedBox(width: 6),
                  Expanded(
                    child: Text(
                      item.title.isEmpty ? 'Bildirim' : item.title,
                      style: const TextStyle(fontWeight: FontWeight.w700),
                    ),
                  ),
                  Text(
                    _formatDate(item.sentAt),
                    style: TextStyle(
                      color: cs.onSurface.withValues(alpha: 0.55),
                      fontSize: 11,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 6),
              Text(item.body),
              if (item.oldPrice != null && item.newPrice != null) ...[
                const SizedBox(height: 8),
                Wrap(
                  spacing: 8,
                  runSpacing: 4,
                  children: [
                    _PriceChip(
                      label: 'Eski',
                      value: item.oldPrice!,
                    ),
                    _PriceChip(
                      label: 'Yeni',
                      value: item.newPrice!,
                    ),
                  ],
                ),
              ],
              if (!item.isSuccess && item.error != null && item.error!.isNotEmpty) ...[
                const SizedBox(height: 8),
                Text(
                  item.error!,
                  style: TextStyle(color: cs.error, fontSize: 12),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }

  String _formatDate(DateTime date) {
    final d = date.toLocal();
    final dd = d.day.toString().padLeft(2, '0');
    final mm = d.month.toString().padLeft(2, '0');
    final hh = d.hour.toString().padLeft(2, '0');
    final min = d.minute.toString().padLeft(2, '0');
    return '$dd.$mm $hh:$min';
  }
}

class _PriceChip extends StatelessWidget {
  final String label;
  final double value;

  const _PriceChip({required this.label, required this.value});

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(
        color: Theme.of(context).colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(6),
      ),
      child: Text(
        '$label: ${value.toStringAsFixed(2)} ₺',
        style: const TextStyle(fontSize: 11, fontWeight: FontWeight.w600),
      ),
    );
  }
}
