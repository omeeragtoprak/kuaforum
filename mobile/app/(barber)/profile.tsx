import { View, Text, StyleSheet, ScrollView, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { Colors, FontSize, Radius, Spacing } from '@/lib/theme';
import { apiClient } from '@/api/client';
import { AuthApi } from '@/api/auth';
import { useAuthStore } from '@/store/auth-store';

export default function BarberProfileScreen() {
  const router = useRouter();
  const signOut = useAuthStore((s) => s.signOut);
  const user = useAuthStore((s) => s.user);

  const shopQuery = useQuery({
    queryKey: ['my-shop'],
    queryFn: async () => {
      const res = await apiClient.get('/v1/owner/barber-shop');
      return res.data;
    },
  });

  async function handleLogout() {
    Alert.alert('Çıkış yap', 'Çıkış yapmak istediğine emin misin?', [
      { text: 'Vazgeç', style: 'cancel' },
      {
        text: 'Çıkış',
        style: 'destructive',
        onPress: async () => {
          await AuthApi.logout();
          await signOut();
          router.replace('/(auth)/login');
        },
      },
    ]);
  }

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScrollView contentContainerStyle={styles.scroll}>
        <View style={styles.header}>
          <View style={styles.avatar}>
            <Text style={styles.avatarText}>{user?.displayName?.[0]?.toUpperCase() ?? '✂️'}</Text>
          </View>
          <Text style={styles.name}>{user?.displayName ?? 'Kuaför'}</Text>
          <Text style={styles.role}>{user?.role}</Text>
        </View>

        {shopQuery.data ? (
          <Card style={styles.section}>
            <Text style={styles.sectionTitle}>İşletme</Text>
            <Row label="Ünvan" value={shopQuery.data.name ?? '—'} />
            <Row label="Tip" value={genderLabel(shopQuery.data.genderType)} />
            <Row label="Durum" value={statusLabel(shopQuery.data.status)} />
            {shopQuery.data.addressLine1 ? <Row label="Adres" value={shopQuery.data.addressLine1} /> : null}
          </Card>
        ) : (
          <Card style={styles.section}>
            <Text style={styles.sectionTitle}>İşletme</Text>
            <Text style={styles.muted}>Henüz işletme oluşturulmamış. Web'den oluşturun.</Text>
          </Card>
        )}

        <Card style={styles.section}>
          <Text style={styles.sectionTitle}>Yapılacaklar (V1.5)</Text>
          <Text style={styles.muted}>• Hizmet katalog yönetimi</Text>
          <Text style={styles.muted}>• Çalışma saatleri + izin</Text>
          <Text style={styles.muted}>• Stilist ekleme/çıkarma</Text>
          <Text style={styles.muted}>• Walk-in toggle</Text>
          <Text style={styles.muted}>• Gelir raporu</Text>
        </Card>

        <Button label="Çıkış yap" variant="secondary" onPress={handleLogout} />

        <Text style={styles.footer}>Kuaforum Pro • v0.0.1</Text>
      </ScrollView>
    </SafeAreaView>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.row}>
      <Text style={styles.rowLabel}>{label}</Text>
      <Text style={styles.rowValue}>{value}</Text>
    </View>
  );
}

function genderLabel(g: number) {
  return g === 1 ? 'Sadece Kadın' : g === 2 ? 'Sadece Erkek' : 'Unisex';
}
function statusLabel(s: number) {
  return s === 0 ? 'Taslak' : s === 1 ? 'KYC bekliyor' : s === 2 ? 'Aktif' : s === 3 ? 'Askıya alındı' : 'Kapalı';
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  scroll: { padding: Spacing.lg, gap: Spacing.lg },
  header: { alignItems: 'center', paddingVertical: Spacing.lg },
  avatar: {
    width: 80,
    height: 80,
    borderRadius: Radius.full,
    backgroundColor: Colors.primaryDark,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: Spacing.md,
  },
  avatarText: { color: '#fff', fontSize: 36, fontWeight: '700' },
  name: { fontSize: FontSize.xl, fontWeight: '600', color: Colors.text },
  role: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: Spacing.xs },
  section: { gap: Spacing.sm },
  sectionTitle: { fontSize: FontSize.md, fontWeight: '700', color: Colors.text, marginBottom: Spacing.xs },
  row: { flexDirection: 'row', justifyContent: 'space-between', paddingVertical: Spacing.sm },
  rowLabel: { fontSize: FontSize.sm, color: Colors.textSecondary },
  rowValue: { fontSize: FontSize.sm, color: Colors.text, fontWeight: '500' },
  muted: { fontSize: FontSize.sm, color: Colors.textMuted, paddingVertical: 2 },
  footer: { fontSize: FontSize.xs, color: Colors.textMuted, textAlign: 'center', marginTop: Spacing.lg },
});
