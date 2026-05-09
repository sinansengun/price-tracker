import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../providers/auth_provider.dart';

class AccountScreen extends StatefulWidget {
  const AccountScreen({super.key});

  @override
  State<AccountScreen> createState() => _AccountScreenState();
}

class _AccountScreenState extends State<AccountScreen> {
  bool _loggingOut = false;

  Future<void> _confirmAndLogout() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) {
        return AlertDialog(
          icon: const Icon(Icons.logout),
          title: const Text('Çıkış Yap'),
          content: const Text('Oturumunu kapatmak istediğine emin misin?'),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(dialogContext).pop(false),
              child: const Text('Vazgeç'),
            ),
            FilledButton(
              onPressed: () => Navigator.of(dialogContext).pop(true),
              child: const Text('Çıkış Yap'),
            ),
          ],
        );
      },
    );

    if (confirmed == true) {
      await _logout();
    }
  }

  Future<void> _logout() async {
    if (_loggingOut) return;
    setState(() {
      _loggingOut = true;
    });

    final auth = context.read<AuthProvider>();
    await auth.logout();
    if (!mounted) return;
    context.go('/login');
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cs = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Hesabım'),
      ),
      body: Stack(
        children: [
          Positioned(
            top: -90,
            right: -40,
            child: DecoratedBox(
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: cs.primary.withValues(alpha: 0.10),
              ),
              child: const SizedBox(width: 240, height: 240),
            ),
          ),
          Positioned(
            top: 50,
            left: -70,
            child: DecoratedBox(
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: cs.secondary.withValues(alpha: 0.10),
              ),
              child: const SizedBox(width: 180, height: 180),
            ),
          ),
          ListView(
            padding: const EdgeInsets.all(16),
            children: [
              Container(
                padding: const EdgeInsets.fromLTRB(16, 16, 16, 14),
                decoration: BoxDecoration(
                  gradient: LinearGradient(
                    colors: [
                      cs.primaryContainer,
                      cs.primaryContainer.withValues(alpha: 0.78),
                    ],
                    begin: Alignment.topLeft,
                    end: Alignment.bottomRight,
                  ),
                  borderRadius: BorderRadius.circular(16),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'Hesap Merkezi',
                      style: theme.textTheme.titleLarge?.copyWith(
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    const SizedBox(height: 4),
                    Text(
                      'Profil, güvenlik ve bildirim ayarlarını tek yerden yönet.',
                      style: theme.textTheme.bodyMedium,
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 14),
              Card(
                clipBehavior: Clip.antiAlias,
                child: Column(
                  children: [
                    _AnimatedMenuTile(
                      delay: 0,
                      child: _AccountMenuTile(
                        icon: Icons.person_outline,
                        title: 'Kişisel Bilgilerim',
                        subtitle: 'E-posta ve hesap bilgilerini görüntüle',
                        badge: const _TileBadge(
                          label: 'Profil',
                          icon: Icons.verified_user_outlined,
                        ),
                        onTap: () => context.push('/account/profile'),
                      ),
                    ),
                    const Divider(height: 1),
                    _AnimatedMenuTile(
                      delay: 70,
                      child: _AccountMenuTile(
                        icon: Icons.lock_outline,
                        title: 'Şifre Değiştirme',
                        subtitle: 'Hesap şifreni güncelle',
                        badge: const _TileBadge(
                          label: 'Güvenlik',
                          icon: Icons.shield_outlined,
                        ),
                        onTap: () => context.push('/account/password'),
                      ),
                    ),
                    const Divider(height: 1),
                    _AnimatedMenuTile(
                      delay: 140,
                      child: _AccountMenuTile(
                        icon: Icons.notifications_none_rounded,
                        title: 'Bildirimlerim',
                        subtitle: 'Geçmiş bildirimleri görüntüle',
                        badge: const _TileBadge(
                          label: 'Geçmiş',
                          icon: Icons.history,
                        ),
                        onTap: () => context.push('/notifications'),
                      ),
                    ),
                    const Divider(height: 1),
                    _AnimatedMenuTile(
                      delay: 210,
                      child: _AccountMenuTile(
                        icon: Icons.logout,
                        title: 'Çıkış Yap',
                        subtitle: 'Oturumu güvenli şekilde kapat',
                        iconColor: cs.error,
                        textColor: cs.error,
                        badge: _TileBadge(
                          label: 'Oturum',
                          icon: Icons.login,
                          toneColor: cs.error,
                        ),
                        trailing: _loggingOut
                            ? SizedBox(
                                width: 20,
                                height: 20,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                  color: cs.error,
                                ),
                              )
                            : Icon(Icons.chevron_right, color: cs.error),
                        onTap: _confirmAndLogout,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _AnimatedMenuTile extends StatelessWidget {
  final int delay;
  final Widget child;

  const _AnimatedMenuTile({required this.delay, required this.child});

  @override
  Widget build(BuildContext context) {
    return TweenAnimationBuilder<double>(
      tween: Tween(begin: 0, end: 1),
      duration: Duration(milliseconds: 260 + delay),
      curve: Curves.easeOutCubic,
      builder: (context, value, builtChild) {
        return Transform.translate(
          offset: Offset(0, (1 - value) * 10),
          child: Opacity(
            opacity: value,
            child: builtChild,
          ),
        );
      },
      child: child,
    );
  }
}

class _AccountMenuTile extends StatelessWidget {
  final IconData icon;
  final String title;
  final String subtitle;
  final Color? iconColor;
  final Color? textColor;
  final Widget? trailing;
  final Widget? badge;
  final VoidCallback onTap;

  const _AccountMenuTile({
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.onTap,
    this.iconColor,
    this.textColor,
    this.trailing,
    this.badge,
  });

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final resolvedColor = iconColor ?? cs.primary;
    return ListTile(
      leading: Container(
        width: 38,
        height: 38,
        decoration: BoxDecoration(
          color: resolvedColor.withValues(alpha: 0.12),
          borderRadius: BorderRadius.circular(12),
        ),
        child: Icon(icon, color: resolvedColor),
      ),
      title: Text(
        title,
        style: TextStyle(fontWeight: FontWeight.w600, color: textColor),
      ),
      trailing: trailing ?? const Icon(Icons.chevron_right),
      onLongPress: badge == null
          ? null
          : () {
              ScaffoldMessenger.of(context)
                ..hideCurrentSnackBar()
                ..showSnackBar(
                  SnackBar(
                    content: Text(title),
                    duration: const Duration(milliseconds: 900),
                  ),
                );
            },
      titleAlignment: ListTileTitleAlignment.threeLine,
      minVerticalPadding: 10,
      onTap: onTap,
      isThreeLine: badge != null,
      dense: false,
      horizontalTitleGap: 12,
      visualDensity: const VisualDensity(vertical: 0.5),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(14),
      ),
      selectedColor: cs.primary,
      selected: false,
      // Keep badge close to text without creating another nested card.
      contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
      subtitleTextStyle:
          Theme.of(context).textTheme.bodySmall?.copyWith(height: 1.25),
      subtitle: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(subtitle),
          if (badge != null) ...[
            const SizedBox(height: 6),
            badge!,
          ],
        ],
      ),
    );
  }
}

class _TileBadge extends StatelessWidget {
  final String label;
  final IconData icon;
  final Color? toneColor;

  const _TileBadge({
    required this.label,
    required this.icon,
    this.toneColor,
  });

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final color = toneColor ?? cs.primary;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.10),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 13, color: color),
          const SizedBox(width: 4),
          Text(
            label,
            style: TextStyle(
              color: color,
              fontSize: 11,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
      ),
    );
  }
}
