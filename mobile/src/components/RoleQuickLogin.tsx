import { Pressable, StyleSheet, Text, View, ActivityIndicator } from 'react-native';
import { Colors, Radius, Spacing, FontSize, FontWeight, Shadow } from '@/lib/theme';

export type DemoRole = 'Customer' | 'CustomerFresh' | 'BarberOwner' | 'BarberOwnerDraft' | 'Admin';

type RoleQuickLoginProps = {
  role: DemoRole;
  loading?: boolean;
  onPress?: () => void;
};

const META: Record<
  DemoRole,
  { emoji: string; title: string; subtitle: string; bg: string; fg: string }
> = {
  Customer: {
    emoji: '👤',
    title: 'Müşteri olarak gir',
    subtitle: 'Randevu al, kuaför keşfet',
    bg: Colors.primary,
    fg: Colors.textInverse,
  },
  CustomerFresh: {
    emoji: '🔐',
    title: 'Müşteri (rıza ekranı)',
    subtitle: 'Her girişte KVKK rıza akışı yeniden sorulur',
    bg: '#8B5CF6',
    fg: Colors.textInverse,
  },
  BarberOwner: {
    emoji: '✂️',
    title: 'Kuaför olarak gir',
    subtitle: 'Salonunu yönet, randevuları gör',
    bg: '#0EA5E9',
    fg: Colors.textInverse,
  },
  BarberOwnerDraft: {
    emoji: '🆕',
    title: 'Yeni kuaför (Draft)',
    subtitle: 'KYC başvurusu test akışı',
    bg: '#F59E0B',
    fg: Colors.textInverse,
  },
  Admin: {
    emoji: '🛡️',
    title: 'Admin olarak gir',
    subtitle: 'KYC, kullanıcı, yorum yönetimi',
    bg: Colors.text,
    fg: Colors.textInverse,
  },
};

export function RoleQuickLogin({ role, loading, onPress }: RoleQuickLoginProps) {
  const meta = META[role];
  return (
    <Pressable
      onPress={onPress}
      disabled={loading}
      style={({ pressed }) => [
        styles.card,
        { backgroundColor: meta.bg },
        pressed && !loading && styles.pressed,
        loading && styles.disabled,
      ]}
      accessibilityRole="button"
      accessibilityLabel={meta.title}
      accessibilityHint={meta.subtitle}
    >
      <View style={styles.iconWrap}>
        <Text style={styles.icon}>{meta.emoji}</Text>
      </View>
      <View style={styles.body}>
        <Text style={[styles.title, { color: meta.fg }]}>{meta.title}</Text>
        <Text style={[styles.subtitle, { color: meta.fg, opacity: 0.85 }]}>{meta.subtitle}</Text>
      </View>
      {loading ? (
        <ActivityIndicator color={meta.fg} />
      ) : (
        <Text style={[styles.arrow, { color: meta.fg }]}>›</Text>
      )}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  card: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.md,
    padding: Spacing.lg,
    borderRadius: Radius.lg,
    minHeight: 76,
    ...Shadow.md,
  },
  pressed: { opacity: 0.92, transform: [{ scale: 0.99 }] },
  disabled: { opacity: 0.7 },
  iconWrap: {
    width: 48,
    height: 48,
    borderRadius: 24,
    backgroundColor: 'rgba(255,255,255,0.18)',
    alignItems: 'center',
    justifyContent: 'center',
  },
  icon: { fontSize: 26 },
  body: { flex: 1 },
  title: { fontSize: FontSize.md, fontWeight: FontWeight.bold, marginBottom: 2 },
  subtitle: { fontSize: FontSize.xs, fontWeight: FontWeight.regular },
  arrow: { fontSize: 28, fontWeight: FontWeight.regular, marginRight: 4 },
});
