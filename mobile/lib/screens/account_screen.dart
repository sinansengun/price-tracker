import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../providers/account_provider.dart';
import '../providers/auth_provider.dart';

class AccountScreen extends StatefulWidget {
  const AccountScreen({super.key});

  @override
  State<AccountScreen> createState() => _AccountScreenState();
}

class _AccountScreenState extends State<AccountScreen> {
  final _formKey = GlobalKey<FormState>();
  final _currentPasswordCtrl = TextEditingController();
  final _newPasswordCtrl = TextEditingController();
  final _confirmPasswordCtrl = TextEditingController();

  bool _currentObscure = true;
  bool _newObscure = true;
  bool _confirmObscure = true;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      context.read<AccountProvider>().fetchProfile();
    });
  }

  @override
  void dispose() {
    _currentPasswordCtrl.dispose();
    _newPasswordCtrl.dispose();
    _confirmPasswordCtrl.dispose();
    super.dispose();
  }

  Future<void> _changePassword() async {
    final account = context.read<AccountProvider>();
    account.clearMessages();

    if (!_formKey.currentState!.validate()) return;

    final ok = await account.changePassword(
      currentPassword: _currentPasswordCtrl.text,
      newPassword: _newPasswordCtrl.text,
    );

    if (!mounted) return;
    if (ok) {
      _currentPasswordCtrl.clear();
      _newPasswordCtrl.clear();
      _confirmPasswordCtrl.clear();
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Şifren güncellendi.')),
      );
    }
  }

  Future<void> _logout() async {
    final auth = context.read<AuthProvider>();
    await auth.logout();
    if (!mounted) return;
    context.go('/login');
  }

  @override
  Widget build(BuildContext context) {
    final account = context.watch<AccountProvider>();
    final profile = account.profile;
    final cs = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Hesabım'),
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
                        ],
                      ),
                    ),
                  ),
                  const SizedBox(height: 12),
                  Card(
                    child: Padding(
                      padding: const EdgeInsets.all(16),
                      child: Form(
                        key: _formKey,
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              'Şifre Değiştir',
                              style: Theme.of(context)
                                  .textTheme
                                  .titleMedium
                                  ?.copyWith(fontWeight: FontWeight.w700),
                            ),
                            const SizedBox(height: 12),
                            if (profile != null && !profile.hasPassword)
                              Text(
                                'Bu hesap Google ile oluşturulmuş. Şifre değiştirme için önce şifre tanımlama akışı gerekir.',
                                style: TextStyle(color: cs.onSurface.withValues(alpha: 0.7)),
                              )
                            else ...[
                              TextFormField(
                                controller: _currentPasswordCtrl,
                                obscureText: _currentObscure,
                                decoration: InputDecoration(
                                  labelText: 'Mevcut Şifre',
                                  border: const OutlineInputBorder(),
                                  suffixIcon: IconButton(
                                    onPressed: () => setState(() {
                                      _currentObscure = !_currentObscure;
                                    }),
                                    icon: Icon(_currentObscure
                                        ? Icons.visibility_outlined
                                        : Icons.visibility_off_outlined),
                                  ),
                                ),
                                validator: (v) {
                                  if (v == null || v.isEmpty) {
                                    return 'Mevcut şifre zorunlu.';
                                  }
                                  return null;
                                },
                              ),
                              const SizedBox(height: 10),
                              TextFormField(
                                controller: _newPasswordCtrl,
                                obscureText: _newObscure,
                                decoration: InputDecoration(
                                  labelText: 'Yeni Şifre',
                                  border: const OutlineInputBorder(),
                                  suffixIcon: IconButton(
                                    onPressed: () => setState(() {
                                      _newObscure = !_newObscure;
                                    }),
                                    icon: Icon(_newObscure
                                        ? Icons.visibility_outlined
                                        : Icons.visibility_off_outlined),
                                  ),
                                ),
                                validator: (v) {
                                  if (v == null || v.length < 6) {
                                    return 'Yeni şifre en az 6 karakter olmalı.';
                                  }
                                  return null;
                                },
                              ),
                              const SizedBox(height: 10),
                              TextFormField(
                                controller: _confirmPasswordCtrl,
                                obscureText: _confirmObscure,
                                decoration: InputDecoration(
                                  labelText: 'Yeni Şifre (Tekrar)',
                                  border: const OutlineInputBorder(),
                                  suffixIcon: IconButton(
                                    onPressed: () => setState(() {
                                      _confirmObscure = !_confirmObscure;
                                    }),
                                    icon: Icon(_confirmObscure
                                        ? Icons.visibility_outlined
                                        : Icons.visibility_off_outlined),
                                  ),
                                ),
                                validator: (v) {
                                  if (v != _newPasswordCtrl.text) {
                                    return 'Yeni şifreler eşleşmiyor.';
                                  }
                                  return null;
                                },
                              ),
                              if (account.passwordMessage != null) ...[
                                const SizedBox(height: 10),
                                Text(
                                  account.passwordMessage!,
                                  style: TextStyle(color: Colors.green.shade700),
                                ),
                              ],
                              const SizedBox(height: 12),
                              SizedBox(
                                width: double.infinity,
                                child: FilledButton(
                                  onPressed: account.changingPassword
                                      ? null
                                      : _changePassword,
                                  child: account.changingPassword
                                      ? const SizedBox(
                                          width: 18,
                                          height: 18,
                                          child: CircularProgressIndicator(strokeWidth: 2),
                                        )
                                      : const Text('Şifreyi Güncelle'),
                                ),
                              ),
                            ],
                          ],
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(height: 12),
                  OutlinedButton.icon(
                    onPressed: _logout,
                    icon: const Icon(Icons.logout),
                    label: const Text('Çıkış Yap'),
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
