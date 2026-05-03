import { View, Text, StyleSheet, FlatList, RefreshControl, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { EmptyState } from '@/components/EmptyState';
import { Colors, FontSize, Spacing } from '@/lib/theme';
import { BookingApi, Booking } from '@/api/booking';
import { extractApiError } from '@/api/client';
import { formatTime, formatTry } from '@/lib/format';

const STATUS: Record<number, { text: string; color: string }> = {
  0: { text: 'Beklemede', color: Colors.warning },
  1: { text: 'Onaylandı', color: Colors.success },
  2: { text: 'Devam ediyor', color: Colors.info },
  3: { text: 'Tamamlandı', color: Colors.textMuted },
  4: { text: 'İptal', color: Colors.textMuted },
  5: { text: 'No-show', color: Colors.error },
};

export default function BarberTodayScreen() {
  const qc = useQueryClient();
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const tomorrow = new Date(today);
  tomorrow.setDate(today.getDate() + 1);

  const query = useQuery({
    queryKey: ['owner-bookings-today'],
    queryFn: () => BookingApi.getOwnerBookings(today.toISOString(), tomorrow.toISOString()),
  });

  const startMut = useMutation({
    mutationFn: (id: string) => BookingApi.start(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['owner-bookings-today'] }),
    onError: (err) => Alert.alert('Hata', extractApiError(err)),
  });

  const completeMut = useMutation({
    mutationFn: (id: string) => BookingApi.complete(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['owner-bookings-today'] }),
    onError: (err) => Alert.alert('Hata', extractApiError(err)),
  });

  const noShowMut = useMutation({
    mutationFn: (id: string) => BookingApi.markNoShow(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['owner-bookings-today'] }),
    onError: (err) => Alert.alert('Hata', extractApiError(err)),
  });

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <View style={styles.header}>
        <Text style={styles.title}>Bugün</Text>
        <Text style={styles.subtitle}>
          {today.toLocaleDateString('tr-TR', { weekday: 'long', day: '2-digit', month: 'long' })}
        </Text>
      </View>

      {query.isLoading ? (
        <EmptyState title="Yükleniyor..." />
      ) : query.isError ? (
        <EmptyState
          title="İşletme bulunamadı"
          description="Henüz BarberShop oluşturulmamış. Web'den /v1/owner/barber-shop POST ile oluştur veya admin panelinden oluştur."
        />
      ) : (query.data?.length ?? 0) === 0 ? (
        <EmptyState title="Bugün randevu yok" description="Boş gün, kahve molası 🙂" />
      ) : (
        <FlatList
          data={query.data}
          keyExtractor={(b) => b.id}
          contentContainerStyle={styles.list}
          refreshControl={<RefreshControl refreshing={query.isFetching} onRefresh={() => query.refetch()} />}
          renderItem={({ item }) => (
            <BookingRow
              booking={item}
              onStart={() => startMut.mutate(item.id)}
              onComplete={() => completeMut.mutate(item.id)}
              onNoShow={() => Alert.alert('No-show', 'Müşteri gelmedi olarak işaretle?', [
                { text: 'Vazgeç', style: 'cancel' },
                { text: 'Evet', onPress: () => noShowMut.mutate(item.id) },
              ])}
              busy={startMut.isPending || completeMut.isPending || noShowMut.isPending}
            />
          )}
        />
      )}
    </SafeAreaView>
  );
}

function BookingRow({
  booking, onStart, onComplete, onNoShow, busy,
}: {
  booking: Booking;
  onStart: () => void;
  onComplete: () => void;
  onNoShow: () => void;
  busy: boolean;
}) {
  const status = STATUS[booking.status] ?? { text: '?', color: Colors.textMuted };
  const canStart = booking.status === 1; // Confirmed
  const canComplete = booking.status === 2; // Started

  return (
    <Card style={styles.card}>
      <View style={styles.head}>
        <Text style={styles.time}>{formatTime(booking.scheduledAt)}</Text>
        <View style={[styles.badge, { backgroundColor: status.color }]}>
          <Text style={styles.badgeText}>{status.text}</Text>
        </View>
      </View>
      <Text style={styles.customerLine}>Müşteri: {booking.customerId.slice(0, 8)}…</Text>
      <View style={styles.servicesRow}>
        {booking.items.map((it) => (
          <Text key={it.id} style={styles.serviceLine}>
            • {it.serviceName} ({it.durationMinutes} dk)
          </Text>
        ))}
      </View>
      <Text style={styles.amount}>{formatTry(booking.totalAmountMinor)}</Text>

      {canStart || canComplete ? (
        <View style={styles.actions}>
          {canStart ? <Button label="Başla" onPress={onStart} disabled={busy} /> : null}
          {canStart ? <Button label="Gelmedi" variant="destructive" onPress={onNoShow} disabled={busy} /> : null}
          {canComplete ? <Button label="Tamamla" onPress={onComplete} disabled={busy} /> : null}
        </View>
      ) : null}
    </Card>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: { padding: Spacing.lg },
  title: { fontSize: FontSize.xxl, fontWeight: '700', color: Colors.text },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: Spacing.xs, textTransform: 'capitalize' },
  list: { padding: Spacing.lg, paddingTop: 0, gap: Spacing.md },
  card: { gap: Spacing.sm },
  head: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  time: { fontSize: FontSize.xl, fontWeight: '700', color: Colors.primary },
  badge: { paddingHorizontal: Spacing.sm, paddingVertical: 4, borderRadius: 999 },
  badgeText: { color: '#fff', fontSize: FontSize.xs, fontWeight: '600' },
  customerLine: { fontSize: FontSize.sm, color: Colors.textSecondary },
  servicesRow: { gap: 2 },
  serviceLine: { fontSize: FontSize.sm, color: Colors.text },
  amount: { fontSize: FontSize.lg, fontWeight: '700', color: Colors.primary },
  actions: { flexDirection: 'row', gap: Spacing.sm, marginTop: Spacing.sm },
});
