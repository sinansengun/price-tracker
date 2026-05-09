import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../providers/account_provider.dart';

class AccountProfileScreen extends StatefulWidget {
  const AccountProfileScreen({super.key});

  @override
  State<AccountProfileScreen> createState() => _AccountProfileScreenState();
}

class _AccountProfileScreenState extends State<AccountProfileScreen> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      context.read<AccountProvider>().fetchProfile();
    });
  }

  @override
  Widget build(BuildContext context) {
    final account = context.watch<AccountProvider>();
    final profile = account.profile;
    final cs = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Kişisel Bilgilerim'),
      ),
      body: account.loading && profile == null
          ? const Center(child: CircularProgressIndicator())
          : RefreshIndicator(
              onRefresh: account.fetchProfile,
              child: ListView(
                padding: const EdgeInsets.all(16),
                children: [
                  if (account.error != null) ...[
                    Card(
                      child: Padding(
                        padding: const EdgeInsets.all(12),
                        child: Text(
                          account.error!,
                          style: TextStyle(color: cs.error),
                        ),
                      ),
                    ),
                    const SizedBox(height: 12),
                  ],
                  Card(
                    child: Padding(
                      padding: const EdgeInsets.all(16),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            'Hesap Bilgileri',
                            style: Theme.of(context)
                                .textTheme
                                .titleMedium
                                ?.copyWith(fontWeight: FontWeight.w700),
                          ),
                          const SizedBox(height: 14),
                          _InfoRow(label: 'E-posta', value: profile?.email ?? '-'),
                          const SizedBox(height: 10),
                          _InfoRow(
                            label: 'Kayıt Tarihi',
                            value: profile == null
                                ? '-'
                                : _formatDate(profile.createdAt),
                          ),
                          const SizedBox(height: 10),
                          _InfoRow(
                            label: 'Şifre Durumu',
                            value: profile == null
                                ? '-'
                                : profile.hasPassword
                                    ? 'Tanımlı'
                                    : 'Google hesabı (şifre tanımlı değil)',
                          ),
                        ],
                      ),
                    ),
                  ),
                ],
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
    return '$dd.$mm.${d.year} $hh:$min';
  }
}

class _InfoRow extends StatelessWidget {
  final String label;
  final String value;

  const _InfoRow({required this.label, required this.value});

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: textTheme.labelMedium),
        const SizedBox(height: 2),
        Text(value, style: textTheme.bodyLarge),
      ],
    );
  }
}