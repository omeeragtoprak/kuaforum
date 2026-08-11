import { Pressable, StyleSheet, Text, View, ActivityIndicator } from 'react-native';
import { Colors, Radius, Spacing, FontSize, FontWeight } from '@/lib/theme';
import { Icon, IconName } from './Icon';

export type DemoRole = 'Customer' | 'CustomerFresh' | 'BarberOwner' | 'BarberOwnerDraft' | 'Admin';

type RoleQuickLoginProps = {
  role: DemoRole;
  loading?: boolean;
  onPress?: () => void;
};

/**
 * Önceki sürüm her rolü ayrı bir dolgu rengiyle (mor, mavi, amber…) kodluyordu —
 * "Vitrin"in tam karşıtı: renk kodlaması yerine ikon + isim ayırt edici olmalı.
 * Bu yalnız geliştirici/test aracı bir ekran (__DEV__ hızlı-giriş), ama aynı
 * bileşen kütüphanesini kullandığı için görsel dilden sapmamalı.
 */
const META: Record<DemoRole, { icon: IconName; title: string; subtitle: string }> = {
  Customer: {
    icon: 'user',
    title: 'Müşteri olarak gir',
    subtitle: 'Randevu al, kuaför keşfet',
  },
  CustomerFresh: {
    icon: 'lock',
    title: 'Müşteri (rıza ekranı)',
    subtitle: 'Her girişte KVKK rıza akışı yeniden sorulur',
  },
  BarberOwner: {
    icon: 'scissors',
    title: 'Kuaför olarak gir',
    subtitle: 'Salonunu yönet, randevuları gör',
  },
  BarberOwnerDraft: {
    icon: 'edit',
    title: 'Yeni kuaför (Draft)',
    subtitle: 'KYC başvurusu test akışı',
  },
  Admin: {
    icon: 'shield',
    title: 'Admin olarak gir',
    subtitle: 'KYC, kullanıcı, yorum yönetimi',
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
        pressed && !loading && styles.pressed,
        loading && styles.disabled,
      ]}
      accessibilityRole="button"
      accessibilityLabel={meta.title}
      accessibilityHint={meta.subtitle}
      accessibilityState={{ disabled: !!loading, busy: !!loading }}
    >
      <View style={styles.iconWrap}>
        <Icon name={meta.icon} size={24} color={Colors.ink} strokeWidth={1.6} />
      </View>
      <View style={styles.body}>
        <Text style={styles.title}>{meta.title}</Text>
        <Text style={styles.subtitle}>{meta.subtitle}</Text>
      </View>
      {loading ? (
        <ActivityIndicator color={Colors.ink} />
      ) : (
        <Icon name="chevronRight" size={20} color={Colors.textMuted} strokeWidth={1.6} />
      )}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  // Beyaz zemin + çizgi — degrade/renk-kodlama yok, ayrım ikon + rol adından gelir.
  card: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.md,
    padding: Spacing.lg,
    borderRadius: Radius.lg,
    minHeight: 76,
    backgroundColor: Colors.surface,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  // Basılı hâl: opaklık yerine hafif zemin koyulaşması.
  pressed: { backgroundColor: Colors.surfaceAlt },
  disabled: { opacity: 0.6 },
  iconWrap: {
    width: 48,
    height: 48,
    borderRadius: 24,
    backgroundColor: Colors.surfaceAlt,
    alignItems: 'center',
    justifyContent: 'center',
  },
  body: { flex: 1 },
  title: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, color: Colors.ink, marginBottom: 2 },
  subtitle: { fontSize: FontSize.xs, fontWeight: FontWeight.regular, color: Colors.textSecondary },
});
