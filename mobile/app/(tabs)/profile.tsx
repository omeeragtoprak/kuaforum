import { View, Text, StyleSheet, ScrollView, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { Colors, FontSize, Spacing, Radius } from '@/lib/theme';
import { AuthApi } from '@/api/auth';
import { useAuthStore } from '@/store/auth-store';
import { maskPhone } from '@/lib/format';

export default function ProfileScreen() {
  const router = useRouter();
  const signOut = useAuthStore((s) => s.signOut);

  const profileQuery = useQuery({
    queryKey: ['my-profile'],
    queryFn: () => AuthApi.getMyProfile(),
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

  async function handleDeleteAccount() {
    Alert.alert(
      'Hesabı sil',
      '30 gün soft delete penceresi var, bu sürede geri yükleyebilirsin. Devam ediyor musun?',
      [
        { text: 'Vazgeç', style: 'cancel' },
        {
          text: 'Sil',
          style: 'destructive',
          onPress: async () => {
            // TODO: DELETE /v1/users/me + signOut
            Alert.alert('Henüz wired değil', 'Backend hazır, mobile UI henüz silme butonunu çağırmıyor (bir sonraki turn).');
          },
        },
      ],
    );
  }

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScrollView contentContainerStyle={styles.scroll}>
        <View style={styles.header}>
          <View style={styles.avatar}>
            <Text style={styles.avatarText}>
              {profileQuery.data?.displayName?.[0]?.toUpperCase() ?? '👤'}
            </Text>
          </View>
          <Text style={styles.name}>{profileQuery.data?.displayName ?? 'Misafir'}</Text>
          <Text style={styles.phone}>
            {profileQuery.data?.phone ? maskPhone(profileQuery.data.phone) : '—'}
          </Text>
        </View>

        <Card style={styles.section}>
          <SectionRow label="Rol" value={profileQuery.data?.role ?? '—'} />
          <SectionRow label="Şehir" value={profileQuery.data?.locale ?? 'tr-TR'} />
          <SectionRow label="Üyelik" value={
            profileQuery.data?.createdAt
              ? new Date(profileQuery.data.createdAt).toLocaleDateString('tr-TR')
              : '—'
          } />
        </Card>

        <View style={styles.actions}>
          <Button label="Çıkış yap" variant="secondary" onPress={handleLogout} />
          <Button label="Hesabı sil (KVKK)" variant="destructive" onPress={handleDeleteAccount} />
        </View>

        <Text style={styles.footer}>Kuaforum • v0.0.1</Text>
      </ScrollView>
    </SafeAreaView>
  );
}

function SectionRow({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.row}>
      <Text style={styles.rowLabel}>{label}</Text>
      <Text style={styles.rowValue}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  scroll: { padding: Spacing.lg, gap: Spacing.lg },
  header: { alignItems: 'center', paddingVertical: Spacing.lg },
  avatar: {
    width: 80,
    height: 80,
    borderRadius: Radius.full,
    backgroundColor: Colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: Spacing.md,
  },
  avatarText: { color: '#fff', fontSize: 36, fontWeight: '700' },
  name: { fontSize: FontSize.xl, fontWeight: '600', color: Colors.text },
  phone: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: Spacing.xs },
  section: { gap: Spacing.sm },
  row: { flexDirection: 'row', justifyContent: 'space-between', paddingVertical: Spacing.sm },
  rowLabel: { fontSize: FontSize.sm, color: Colors.textSecondary },
  rowValue: { fontSize: FontSize.sm, color: Colors.text, fontWeight: '500' },
  actions: { gap: Spacing.sm },
  footer: { fontSize: FontSize.xs, color: Colors.textMuted, textAlign: 'center', marginTop: Spacing.lg },
});
