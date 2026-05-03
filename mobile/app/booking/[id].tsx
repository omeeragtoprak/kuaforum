import { View, Text, StyleSheet, ScrollView, Alert, Pressable } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { EmptyState } from '@/components/EmptyState';
import { Colors, FontSize, Spacing } from '@/lib/theme';
import { BookingApi } from '@/api/booking';
import { extractApiError } from '@/api/client';
import { formatDateTime, formatDuration, formatTry } from '@/lib/format';

const STATUS_LABEL: Record<number, { text: string; color: string }> = {
  0: { text: 'Beklemede (ödeme)', color: Colors.warning },
  1: { text: 'Onaylandı', color: Colors.success },
  2: { text: 'Devam ediyor', color: Colors.info },
  3: { text: 'Tamamlandı', color: Colors.success },
  4: { text: 'İptal edildi', color: Colors.textMuted },
  5: { text: 'Gelmediniz', color: Colors.error },
};

export default function BookingDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();
  const qc = useQueryClient();

  const query = useQuery({
    queryKey: ['booking', id],
    queryFn: () => BookingApi.getById(id!),
    enabled: !!id,
  });

  const cancelMutation = useMutation({
    mutationFn: (reason: number) => BookingApi.cancel(id!, reason),
    onSuccess: (data) => {
      Alert.alert('İptal edildi', `İade oranı: %${data.refundPercentage}`);
      qc.invalidateQueries({ queryKey: ['booking', id] });
      qc.invalidateQueries({ queryKey: ['my-bookings'] });
    },
    onError: (err) => Alert.alert('Hata', extractApiError(err)),
  });

  function handleCancel() {
    Alert.alert(
      'Randevuyu iptal et',
      'Kademeli iade: 24 saat öncesine kadar %100, 12-24 saat %50, 0-12 saat %0.',
      [
        { text: 'Vazgeç', style: 'cancel' },
        { text: 'İptal Et', style: 'destructive', onPress: () => cancelMutation.mutate(0) },
      ],
    );
  }

  if (query.isLoading)
    return <SafeAreaView style={styles.container}><EmptyState title="Yükleniyor..." /></SafeAreaView>;
  if (query.isError || !query.data)
    return <SafeAreaView style={styles.container}><EmptyState title="Bulunamadı" /></SafeAreaView>;

  const b = query.data;
  const status = STATUS_LABEL[b.status] ?? { text: '?', color: Colors.textMuted };
  const canCancel = b.status === 0 || b.status === 1; // Pending or Confirmed
  const canReview = b.status === 3; // Completed

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScrollView contentContainerStyle={styles.scroll}>
        <Pressable onPress={() => router.back()}>
          <Text style={styles.back}>← Geri</Text>
        </Pressable>

        <View style={styles.statusRow}>
          <View style={[styles.badge, { backgroundColor: status.color }]}>
            <Text style={styles.badgeText}>{status.text}</Text>
          </View>
        </View>

        <Card style={styles.card}>
          <Text style={styles.heading}>Randevu Detayı</Text>
          <Row label="Tarih" value={formatDateTime(b.scheduledAt)} />
          <Row label="Bitiş" value={formatDateTime(b.scheduledEndsAt)} />
          <Row label="Toplam tutar" value={formatTry(b.totalAmountMinor)} />
          {b.refundPercentage !== undefined && b.refundPercentage !== null ? (
            <Row label="İade oranı" value={`%${b.refundPercentage}`} />
          ) : null}
        </Card>

        <Card style={styles.card}>
          <Text style={styles.heading}>Hizmetler</Text>
          {b.items.map((it) => (
            <View key={it.id} style={styles.itemRow}>
              <Text style={styles.itemName}>{it.serviceName}</Text>
              <Text style={styles.itemMeta}>{formatDuration(it.durationMinutes)} · {formatTry(it.priceMinor)}</Text>
            </View>
          ))}
        </Card>

        {canCancel ? (
          <Button label="Randevuyu iptal et" variant="destructive" onPress={handleCancel} loading={cancelMutation.isPending} />
        ) : null}

        {canReview ? (
          <Button
            label="Yorum yap"
            onPress={() => router.push({ pathname: '/review/submit', params: { bookingId: b.id } })}
          />
        ) : null}
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

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  scroll: { padding: Spacing.lg, gap: Spacing.lg },
  back: { color: Colors.primary, fontSize: FontSize.sm },
  statusRow: { flexDirection: 'row' },
  badge: { paddingHorizontal: Spacing.md, paddingVertical: Spacing.xs, borderRadius: 999 },
  badgeText: { color: '#fff', fontSize: FontSize.sm, fontWeight: '600' },
  card: { gap: Spacing.sm },
  heading: { fontSize: FontSize.lg, fontWeight: '700', color: Colors.text, marginBottom: Spacing.xs },
  row: { flexDirection: 'row', justifyContent: 'space-between', paddingVertical: Spacing.xs },
  rowLabel: { fontSize: FontSize.sm, color: Colors.textSecondary },
  rowValue: { fontSize: FontSize.sm, color: Colors.text, fontWeight: '500' },
  itemRow: { paddingVertical: Spacing.xs, borderBottomWidth: 1, borderBottomColor: Colors.border },
  itemName: { fontSize: FontSize.md, color: Colors.text, fontWeight: '500' },
  itemMeta: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 2 },
});
