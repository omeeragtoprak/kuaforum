import { View, Text, StyleSheet, FlatList, RefreshControl, Alert, Linking, Pressable } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card } from '@/components/Card';
import { Badge } from '@/components/Badge';
import { Button } from '@/components/Button';
import { EmptyState } from '@/components/EmptyState';
import { SkeletonBookingRow, SkeletonList } from '@/components/Skeleton';
import { Colors, FontSize, FontWeight, Spacing, Layout, Radius } from '@/lib/theme';
import { BookingApi, Booking } from '@/api/booking';
import { extractApiError } from '@/api/client';
import {
  formatTime, formatTry, formatPhoneTr,
  bookingStatusInfo, isBookingConfirmed, isBookingInProgress,
  isBookingRevenueCounted, isBookingUpcoming,
} from '@/lib/format';

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

  const list = query.data ?? [];
  const totalRevenue = list
    .filter((b) => isBookingRevenueCounted(b.status))
    .reduce((sum, b) => sum + (b.totalAmountMinor || 0), 0);
  const upcomingCount = list.filter((b) => isBookingUpcoming(b.status)).length;

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <View style={styles.header}>
        <Text style={styles.dayLabel}>
          {today.toLocaleDateString('tr-TR', { weekday: 'long', day: '2-digit', month: 'long' })}
        </Text>
        <Text style={styles.title}>Bugün</Text>

        <View style={styles.statsRow}>
          <View style={styles.statBox}>
            <Text style={styles.statValue}>{list.length}</Text>
            <Text style={styles.statLabel}>Toplam</Text>
          </View>
          <View style={styles.statBox}>
            <Text style={styles.statValue}>{upcomingCount}</Text>
            <Text style={styles.statLabel}>Bekleyen</Text>
          </View>
          <View style={styles.statBox}>
            <Text style={styles.statValue}>{formatTry(totalRevenue)}</Text>
            <Text style={styles.statLabel}>Ciro</Text>
          </View>
        </View>
      </View>

      <FlatList
        data={list}
        keyExtractor={(b) => b.id}
        contentContainerStyle={styles.list}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl
            refreshing={query.isFetching && !query.isLoading}
            onRefresh={() => query.refetch()}
            tintColor="#0EA5E9"
          />
        }
        ItemSeparatorComponent={() => <View style={{ height: Spacing.md }} />}
        ListEmptyComponent={
          query.isLoading ? (
            <SkeletonList count={3} render={() => <SkeletonBookingRow />} />
          ) : query.isError ? (
            <EmptyState
              emoji="⚠️"
              title="Bağlantı hatası"
              description="Bugünkü randevular yüklenemedi. Sunucu erişilebilir mi?"
              actionLabel="Tekrar dene"
              onAction={() => query.refetch()}
            />
          ) : (
            <EmptyState
              emoji="☕"
              title="Bugün boş"
              description="Randevun yok — kahve molası vereceksin demek."
            />
          )
        }
        renderItem={({ item }) => (
          <BookingRow
            booking={item}
            onStart={() => confirmStart(item, () => startMut.mutate(item.id))}
            onComplete={() => confirmComplete(item, () => completeMut.mutate(item.id))}
            onNoShow={() =>
              Alert.alert('Müşteri gelmedi', 'No-show olarak işaretlemek istiyor musun?', [
                { text: 'Vazgeç', style: 'cancel' },
                { text: 'Evet', style: 'destructive', onPress: () => noShowMut.mutate(item.id) },
              ])
            }
            busy={startMut.isPending || completeMut.isPending || noShowMut.isPending}
          />
        )}
      />
    </SafeAreaView>
  );
}

function confirmStart(booking: Booking, onConfirm: () => void) {
  const minutesUntil = Math.round((new Date(booking.scheduledAt).getTime() - Date.now()) / 60000);
  if (minutesUntil > 15) {
    Alert.alert(
      'Erken başlatma',
      `Randevu saatine ${minutesUntil} dakika var. Yine de başlatmak istiyor musun?`,
      [
        { text: 'Vazgeç', style: 'cancel' },
        { text: 'Yine de başlat', onPress: onConfirm },
      ],
    );
    return;
  }
  onConfirm();
}

function confirmComplete(booking: Booking, onConfirm: () => void) {
  Alert.alert(
    'Hizmeti tamamla',
    `${formatTime(booking.scheduledAt)} randevusunu tamamlamak istediğine emin misin? Müşteriye "Tamamlandı" bildirimi gidecek.`,
    [
      { text: 'Vazgeç', style: 'cancel' },
      { text: 'Tamamla', onPress: onConfirm },
    ],
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
  const status = bookingStatusInfo(booking.status);
  const canStart = isBookingConfirmed(booking.status);
  const canComplete = isBookingInProgress(booking.status);

  return (
    <Card style={{ gap: Spacing.sm }}>
      <View style={styles.head}>
        <View style={styles.timeBlock}>
          <Text style={styles.time}>{formatTime(booking.scheduledAt)}</Text>
          <Text style={styles.duration}>
            {booking.items.reduce((s, i) => s + (i.durationMinutes ?? 0), 0)} dk
          </Text>
        </View>
        <Badge label={status.label} tone={status.tone} />
      </View>

      <View style={styles.customerBlock}>
        <Text style={styles.customerName}>
          👤 {booking.customerName ?? `Müşteri #${booking.customerId.slice(0, 8)}`}
        </Text>
        {booking.customerPhone ? (
          <Pressable
            onPress={() => Linking.openURL(`tel:${booking.customerPhone}`)}
            hitSlop={6}
            accessibilityRole="link"
            accessibilityLabel={`Müşteriyi ara ${booking.customerPhone}`}
          >
            <Text style={styles.customerPhone}>📞 {formatPhoneTr(booking.customerPhone)}</Text>
          </Pressable>
        ) : null}
        {booking.customerNote ? (
          <View style={styles.noteBox}>
            <Text style={styles.noteLabel}>Not</Text>
            <Text style={styles.noteText}>{booking.customerNote}</Text>
          </View>
        ) : null}
      </View>

      <View style={styles.servicesBlock}>
        {booking.items.map((it) => (
          <View key={it.id} style={styles.serviceRow}>
            <Text style={styles.serviceDot}>·</Text>
            <Text style={styles.serviceName}>{it.serviceName}</Text>
            <Text style={styles.serviceMeta}>{it.durationMinutes} dk</Text>
          </View>
        ))}
      </View>

      <View style={styles.amountRow}>
        <Text style={styles.amountLabel}>Toplam</Text>
        <Text style={styles.amount}>{formatTry(booking.totalAmountMinor)}</Text>
      </View>

      {(canStart || canComplete) ? (
        <View style={styles.actions}>
          {canStart ? (
            <>
              <Button label="Başlat" onPress={onStart} disabled={busy} size="sm" />
              <Button label="Gelmedi" variant="ghost" onPress={onNoShow} disabled={busy} size="sm" fullWidth={false} />
            </>
          ) : null}
          {canComplete ? <Button label="Tamamla" onPress={onComplete} disabled={busy} size="sm" /> : null}
        </View>
      ) : null}
    </Card>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: { paddingHorizontal: Layout.screenPaddingX, paddingTop: Spacing.lg, paddingBottom: Spacing.lg },
  dayLabel: {
    fontSize: FontSize.xs,
    color: '#0EA5E9',
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    fontWeight: FontWeight.bold,
  },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text, marginTop: 4 },
  statsRow: { flexDirection: 'row', gap: Spacing.sm, marginTop: Spacing.md },
  statBox: {
    flex: 1,
    backgroundColor: Colors.surface,
    padding: Spacing.md,
    borderRadius: Radius.md,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  statValue: { fontSize: FontSize.lg, fontWeight: FontWeight.bold, color: Colors.text },
  statLabel: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 2 },
  list: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingBottom: Spacing.xl,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  head: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-start' },
  timeBlock: { flexDirection: 'column' },
  time: { fontSize: FontSize.xl, fontWeight: FontWeight.bold, color: '#0EA5E9' },
  duration: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 2 },
  customerBlock: { gap: 4 },
  customerName: { fontSize: FontSize.md, color: Colors.text, fontWeight: FontWeight.semibold },
  customerPhone: { fontSize: FontSize.sm, color: '#0EA5E9', fontWeight: FontWeight.medium },
  noteBox: {
    marginTop: 4,
    backgroundColor: Colors.primarySoft,
    borderRadius: Radius.sm,
    padding: Spacing.sm,
  },
  noteLabel: {
    fontSize: FontSize.xs,
    color: Colors.primary,
    fontWeight: FontWeight.bold,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    marginBottom: 2,
  },
  noteText: { fontSize: FontSize.sm, color: Colors.text, lineHeight: 18 },
  servicesBlock: { gap: 4 },
  serviceRow: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  serviceDot: { color: Colors.primary, fontSize: 16, fontWeight: FontWeight.bold },
  serviceName: { flex: 1, fontSize: FontSize.sm, color: Colors.text },
  serviceMeta: { fontSize: FontSize.xs, color: Colors.textMuted },
  amountRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingTop: Spacing.sm,
    borderTopWidth: 1,
    borderTopColor: Colors.divider,
  },
  amountLabel: { fontSize: FontSize.xs, color: Colors.textSecondary, textTransform: 'uppercase', letterSpacing: 0.5 },
  amount: { fontSize: FontSize.lg, fontWeight: FontWeight.bold, color: Colors.primary },
  actions: { flexDirection: 'row', gap: Spacing.sm, marginTop: Spacing.xs },
});
