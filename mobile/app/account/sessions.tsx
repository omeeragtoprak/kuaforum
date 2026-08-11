import { View, Text, StyleSheet, ScrollView, Alert, Pressable, RefreshControl } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card } from '@/components/Card';
import { Badge } from '@/components/Badge';
import { EmptyState } from '@/components/EmptyState';
import { Icon } from '@/components/Icon';
import { SkeletonList, SkeletonBookingRow } from '@/components/Skeleton';
import { Colors, FontSize, FontWeight, LetterSpacing, Radius, Spacing, Layout } from '@/lib/theme';
import { AuthApi, Session } from '@/api/auth';
import { extractApiError } from '@/api/client';
import { Haptics } from '@/lib/haptics';

export default function SessionsScreen() {
  const router = useRouter();
  const qc = useQueryClient();
  const sessionsQuery = useQuery({
    queryKey: ['my-sessions'],
    queryFn: () => AuthApi.getMySessions(),
  });

  const revokeMut = useMutation({
    mutationFn: (sessionId: string) => AuthApi.revokeSession(sessionId),
    onSuccess: () => {
      Haptics.success();
      qc.invalidateQueries({ queryKey: ['my-sessions'] });
    },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });

  function handleRevoke(s: Session) {
    Alert.alert(
      'Oturumu kapat',
      `${s.deviceName ?? 'Bilinmeyen cihaz'} cihazındaki oturumu sonlandır. Cihaz tekrar giriş yapmak zorunda kalır.`,
      [
        { text: 'Vazgeç', style: 'cancel' },
        { text: 'Kapat', style: 'destructive', onPress: () => revokeMut.mutate(s.id) },
      ],
    );
  }

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <View style={styles.header}>
        <Pressable
          onPress={() => router.back()}
          style={styles.backBtn}
          hitSlop={8}
          accessibilityRole="button"
          accessibilityLabel="Geri dön"
        >
          <Icon name="chevronLeft" size={18} color={Colors.ink} strokeWidth={2} />
          <Text style={styles.back}>Geri</Text>
        </Pressable>
        <Text style={styles.title}>Aktif oturumlar</Text>
        <Text style={styles.subtitle}>
          Hesabına giriş yapan tüm cihazlar. Tanımadığın bir cihaz görürsen "Kapat"a bas — token iptal olur.
        </Text>
      </View>

      <ScrollView
        contentContainerStyle={styles.scroll}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl
            refreshing={sessionsQuery.isFetching && !sessionsQuery.isLoading}
            onRefresh={() => sessionsQuery.refetch()}
            tintColor={Colors.ink}
          />
        }
      >
        {sessionsQuery.isLoading ? (
          <SkeletonList count={3} render={() => <SkeletonBookingRow />} />
        ) : sessionsQuery.isError ? (
          <EmptyState
            icon="alert"
            title="Bağlantı hatası"
            description="Oturumlar yüklenemedi."
            actionLabel="Tekrar dene"
            onAction={() => sessionsQuery.refetch()}
          />
        ) : (sessionsQuery.data ?? []).length === 0 ? (
          <EmptyState icon="lock" title="Aktif oturum yok" description="Şu an hiçbir cihaz hesabına bağlı değil." />
        ) : (
          (sessionsQuery.data ?? []).map((s) => (
            <Card key={s.id} style={{ gap: Spacing.sm }}>
              <View style={styles.row}>
                <View style={{ flex: 1 }}>
                  <View style={styles.deviceLine}>
                    <Text style={styles.deviceName}>
                      {s.deviceName ?? 'Bilinmeyen cihaz'}
                    </Text>
                    {s.isCurrent ? <Badge label="Bu cihaz" tone="success" /> : null}
                  </View>
                  <Text style={styles.metaLine}>
                    {s.lastUsedAt
                      ? `Son kullanım: ${formatLocal(s.lastUsedAt)}${s.lastUsedIp ? ` · ${s.lastUsedIp}` : ''}`
                      : `Oluşturma: ${formatLocal(s.createdAt)}`}
                  </Text>
                  <Text style={styles.metaLine}>
                    Cihaz parmak izi: {s.deviceFingerprint.slice(0, 12)}…
                  </Text>
                </View>
                {!s.isCurrent ? (
                  <Pressable
                    onPress={() => handleRevoke(s)}
                    disabled={revokeMut.isPending}
                    style={styles.revokeBtn}
                  >
                    <Text style={styles.revokeBtnText}>Kapat</Text>
                  </Pressable>
                ) : null}
              </View>
            </Card>
          ))
        )}
      </ScrollView>
    </SafeAreaView>
  );
}

function formatLocal(iso: string): string {
  const d = new Date(iso);
  const today = new Date();
  if (d.toDateString() === today.toDateString()) {
    return `Bugün ${d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })}`;
  }
  return d.toLocaleDateString('tr-TR', { day: '2-digit', month: 'short' })
    + ' · ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' });
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: { paddingHorizontal: Layout.screenPaddingX, paddingTop: Spacing.md, paddingBottom: Spacing.sm },
  backBtn: { flexDirection: 'row', alignItems: 'center', gap: 2, alignSelf: 'flex-start', paddingVertical: 4, marginLeft: -4 },
  back: { color: Colors.ink, fontSize: FontSize.sm, fontWeight: FontWeight.medium },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text, marginTop: 6, letterSpacing: LetterSpacing.title },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 6, lineHeight: 20 },
  scroll: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingBottom: Spacing.xxl,
    gap: Spacing.md,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
    flexGrow: 1,
  },
  row: { flexDirection: 'row', alignItems: 'flex-start', gap: Spacing.md },
  deviceLine: { flexDirection: 'row', alignItems: 'center', gap: Spacing.sm },
  deviceName: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, color: Colors.text },
  metaLine: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 4 },
  revokeBtn: {
    paddingHorizontal: Spacing.md,
    paddingVertical: 8,
    borderRadius: Radius.md,
    backgroundColor: Colors.errorSoft,
  },
  revokeBtnText: { fontSize: FontSize.xs, color: Colors.error, fontWeight: FontWeight.bold },
});
