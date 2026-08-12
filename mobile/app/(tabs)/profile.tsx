import { View, Text, StyleSheet, ScrollView, Alert, Pressable, Share } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { Card } from '@/components/Card';
import { Badge } from '@/components/Badge';
import { Button } from '@/components/Button';
import { AuthPrompt } from '@/components/AuthPrompt';
import { Icon, IconName } from '@/components/Icon';
import { Colors, FontSize, FontWeight, LetterSpacing, Spacing, Radius, Layout } from '@/lib/theme';
import { AuthApi } from '@/api/auth';
import { extractApiError } from '@/api/client';
import { useAuthStore } from '@/store/auth-store';
import { maskPhone } from '@/lib/format';
import { Haptics } from '@/lib/haptics';

export default function ProfileScreen() {
  const router = useRouter();
  const signOut = useAuthStore((s) => s.signOut);
  const storeUser = useAuthStore((s) => s.user);
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const isCustomer = useAuthStore((s) => s.isCustomer());
  const role = storeUser?.role;

  const profileQuery = useQuery({
    queryKey: ['my-profile'],
    queryFn: () => AuthApi.getMyProfile(),
    enabled: isAuthenticated,
  });

  const displayName = profileQuery.data?.displayName ?? storeUser?.displayName ?? 'Misafir';
  const phone = profileQuery.data?.phone ?? storeUser?.phone ?? null;
  const email = profileQuery.data?.email ?? storeUser?.email ?? null;

  async function handleLogout() {
    Alert.alert('Çıkış yap', 'Çıkış yapmak istediğine emin misin?', [
      { text: 'Vazgeç', style: 'cancel' },
      {
        text: 'Çıkış',
        style: 'destructive',
        onPress: async () => {
          await signOut();
          router.replace('/(auth)/login');
        },
      },
    ]);
  }

  async function handleDeleteAccount() {
    // KVKK gereği iki kademeli onay — kazara silmeyi engellemek için.
    Alert.alert(
      'Hesabını sileceksin',
      'Profilin gizlenir, randevularına erişimini kaybedersin. 30 gün içinde tekrar giriş yaparsan hesabın geri yüklenir; bu süre dolduktan sonra veriler kalıcı olarak siliniyor (KVKK m.7).',
      [
        { text: 'Vazgeç', style: 'cancel' },
        {
          text: 'Devam et',
          style: 'destructive',
          onPress: () => {
            Alert.alert(
              'Son onay',
              'Bu işlem hemen başlatılır. Emin misin?',
              [
                { text: 'Vazgeç', style: 'cancel' },
                {
                  text: 'Hesabı sil',
                  style: 'destructive',
                  onPress: confirmDeleteAccount,
                },
              ],
            );
          },
        },
      ],
    );
  }

  async function confirmDeleteAccount() {
    try {
      await AuthApi.deleteMyAccount();
      Haptics.warning();
      Alert.alert(
        'Hesabın silindi',
        '30 gün içinde aynı telefon/e-posta ile giriş yaparsan otomatik geri yüklenir. Görüşmek üzere.',
        [{ text: 'Tamam', onPress: async () => {
          await signOut();
          router.replace('/(auth)/login');
        }}],
      );
    } catch (err) {
      Haptics.error();
      Alert.alert('Hata', extractApiError(err));
    }
  }

  // KVKK m.11.1.b — kullanıcı kendi kişisel verilerine erişim hakkı.
  // Share API büyük string'lerde iOS'ta sessizce başarısız olur (paste-board limit
  // ~64KB), bu yüzden >50KB'lik export'ları özet + sayım gösterip Alert.alert
  // ile yönlendiriyoruz (V1.5: dosya olarak indirme için expo-file-system).
  async function handleExportData() {
    try {
      const data = await AuthApi.exportMyData();
      const json = JSON.stringify(data, null, 2);
      // RN-safe byte count: UTF-8 ASCII karakterler 1, çok-bayt karakterler ~2.
      // Blob/Buffer Hermes'te yok; URI-encode tahmini yeterince hassas.
      const bytes = encodeURIComponent(json).replace(/%[0-9A-F]{2}/g, '_').length;
      Haptics.success();
      if (bytes > 50_000) {
        const counts = data as { bookings?: unknown[]; reviews?: unknown[]; consents?: unknown[]; sessions?: unknown[] };
        Alert.alert(
          'Verilerin hazır',
          `Toplam ${Math.round(bytes / 1024)} KB · ${counts.bookings?.length ?? 0} randevu · ${counts.reviews?.length ?? 0} yorum · ${counts.consents?.length ?? 0} rıza · ${counts.sessions?.length ?? 0} oturum.\n\nUygulama içinden tek seferde paylaşmak yerine yardım@kuaforum.com adresine destek talebi açarak tam dosyayı isteyebilirsin.`,
          [{ text: 'Tamam' }],
        );
        return;
      }
      await Share.share({
        title: 'Kuaforum verilerim',
        message: json,
      });
    } catch (err) {
      Haptics.error();
      Alert.alert('Hata', extractApiError(err));
    }
  }

  // RFC-010 — misafir "profil" sekmesine geldiğinde global redirect yerine burada
  // AuthPrompt gösterilir; girişten sonra aynı sekmeye döner. Onboarding/owner
  // akışları da bu sekmedeki linklerden başladığı için doğal olarak korunuyor.
  if (!isAuthenticated) {
    return (
      <AuthPrompt
        description="Kayıtlı bilgilerin, tercihlerin ve hesap ayarların burada."
        redirectTo="/(tabs)/profile"
      />
    );
  }

  const initial = displayName.slice(0, 1).toUpperCase();
  const roleLabel: Record<string, string> = {
    Customer: 'Müşteri',
    BarberOwner: 'Kuaför Sahibi',
    Stylist: 'Stilist',
    Admin: 'Yönetici',
  };
  const displayRole = roleLabel[role ?? 'Customer'] ?? role ?? '—';

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScrollView
        contentContainerStyle={styles.scroll}
        showsVerticalScrollIndicator={false}
      >
        <View style={styles.header}>
          <View style={styles.avatar}>
            <Text style={styles.avatarText}>{initial}</Text>
          </View>
          <Text style={styles.name}>{displayName}</Text>
          <Text style={styles.phone}>
            {phone ? maskPhone(phone) : email ?? '—'}
          </Text>
          <View style={{ marginTop: Spacing.sm }}>
            <Badge label={displayRole} tone="brand" />
          </View>
        </View>

        {isCustomer ? (
          <>
            <Text style={styles.sectionTitle}>İşletme</Text>
            <Card style={styles.section} padded={false}>
              <ListRow
                icon="store"
                label="Kuaför müsün? Salonunu ekle"
                onPress={() => router.push('/onboarding/shop' as never)}
              />
            </Card>
          </>
        ) : null}

        <Text style={styles.sectionTitle}>Hesap</Text>
        <Card style={styles.section} padded={false}>
          <ListRow icon="edit" label="Profili düzenle" onPress={() => router.push('/account/edit-profile' as never)} />
          <Divider />
          <ListRow icon="phone" label="Telefon değiştir" onPress={() => router.push('/account/change-phone' as never)} />
          <Divider />
          <ListRow icon="lock" label="Aktif oturumlar" onPress={() => router.push('/account/sessions' as never)} />
          <Divider />
          <ListRow icon="shield" label="Gizlilik & Rızalar (KVKK)" onPress={() => router.push('/account/consents' as never)} />
          <Divider />
          <ListRow icon="document" label="Verilerimi indir" hint="KVKK m.11" onPress={handleExportData} />
          <Divider />
          <ListRow icon="globe" label="Dil" value="Türkçe" disabled />
        </Card>

        <Text style={styles.sectionTitle}>Yardım</Text>
        <Card style={styles.section} padded={false}>
          <ListRow icon="document" label="Destek" hint="Yakında" disabled />
          <Divider />
          <ListRow icon="document" label="Kullanım Koşulları" onPress={() => router.push('/legal/kullanim-kosullari' as never)} />
          <Divider />
          <ListRow icon="info" label="Sürüm" value="0.0.1" disabled />
        </Card>

        <View style={styles.actions}>
          <Button label="Çıkış yap" variant="secondary" onPress={handleLogout} />
          <Button label="Hesabı sil" variant="ghost" onPress={handleDeleteAccount} />
        </View>

        <Text style={styles.footer}>Kuaforum • Türkiye'nin kuaför pazaryeri</Text>
      </ScrollView>
    </SafeAreaView>
  );
}

function ListRow({
  icon,
  label,
  value,
  hint,
  disabled,
  onPress,
}: {
  icon: IconName;
  label: string;
  value?: string;
  hint?: string;
  disabled?: boolean;
  onPress?: () => void;
}) {
  return (
    <Pressable
      onPress={onPress}
      disabled={disabled}
      accessibilityRole={onPress ? 'button' : undefined}
      accessibilityLabel={label}
      style={({ pressed }) => [styles.listRow, pressed && !disabled && styles.pressed]}
    >
      <View style={styles.listIconWrap}>
        <Icon name={icon} size={18} color={disabled ? Colors.textMuted : Colors.textSecondary} />
      </View>
      <Text style={[styles.listLabel, disabled && { color: Colors.textMuted }]}>{label}</Text>
      <View style={{ flex: 1 }} />
      {value ? <Text style={styles.listValue}>{value}</Text> : null}
      {hint ? <Text style={styles.listHint}>{hint}</Text> : null}
      {!value && !hint && onPress ? <Icon name="chevronRight" size={16} color={Colors.textMuted} /> : null}
    </Pressable>
  );
}

function Divider() {
  return <View style={styles.divider} />;
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  scroll: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingBottom: Spacing.xxl,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  header: { alignItems: 'center', paddingVertical: Spacing.xl },
  avatar: {
    width: 88,
    height: 88,
    borderRadius: Radius.full,
    backgroundColor: Colors.ink,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: Spacing.md,
  },
  avatarText: { color: Colors.textInverse, fontSize: 38, fontWeight: FontWeight.bold },
  name: { fontSize: FontSize.xl, fontWeight: FontWeight.semibold, color: Colors.text, letterSpacing: LetterSpacing.title },
  phone: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 4 },
  sectionTitle: {
    fontSize: FontSize.xs,
    color: Colors.textMuted,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    marginTop: Spacing.lg,
    marginBottom: Spacing.sm,
    paddingHorizontal: Spacing.xs,
    fontWeight: FontWeight.semibold,
  },
  section: { paddingVertical: 4 },
  listRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingVertical: 14,
    paddingHorizontal: Spacing.lg,
    gap: Spacing.md,
    minHeight: 52,
  },
  pressed: { backgroundColor: Colors.surfaceAlt },
  listIconWrap: { width: 24, alignItems: 'center', justifyContent: 'center' },
  listLabel: { fontSize: FontSize.md, color: Colors.text, fontWeight: FontWeight.medium },
  listValue: { fontSize: FontSize.sm, color: Colors.textSecondary },
  listHint: { fontSize: FontSize.xs, color: Colors.textMuted },
  divider: { height: 1, backgroundColor: Colors.divider, marginLeft: 56 },
  actions: { gap: Spacing.sm, marginTop: Spacing.xl },
  footer: {
    fontSize: FontSize.xs,
    color: Colors.textMuted,
    textAlign: 'center',
    marginTop: Spacing.xl,
  },
});
