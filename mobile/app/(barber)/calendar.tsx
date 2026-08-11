import { View, Text, StyleSheet, ScrollView, RefreshControl, Pressable } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { Card } from '@/components/Card';
import { Badge } from '@/components/Badge';
import { EmptyState } from '@/components/EmptyState';
import { Icon } from '@/components/Icon';
import { SkeletonBookingRow, SkeletonList } from '@/components/Skeleton';
import { Colors, FontSize, FontWeight, LetterSpacing, Spacing, Radius, Layout } from '@/lib/theme';
import { BookingApi, Booking } from '@/api/booking';
import { formatTime, formatTry, bookingStatusInfo } from '@/lib/format';

/** Durum rozetinin tonuna karşılık gelen kenar rengi — durum yalnız renkle değil
 *  biçimle (satırın sol kenar çizgisiyle) de kodlanır (kuaför paneli "araç" ilkesi). */
function toneBorderColor(tone: string): string {
  switch (tone) {
    case 'success': return Colors.success;
    case 'warning': return Colors.warning;
    case 'error': return Colors.error;
    case 'info': return Colors.info;
    case 'brand': return Colors.accent;
    default: return Colors.borderStrong;
  }
}

export default function BarberCalendarScreen() {
  const router = useRouter();
  const start = new Date();
  start.setHours(0, 0, 0, 0);
  const end = new Date(start);
  end.setDate(end.getDate() + 14);

  const query = useQuery({
    queryKey: ['owner-bookings-2weeks'],
    queryFn: () => BookingApi.getOwnerBookings(start.toISOString(), end.toISOString()),
  });

  const byDay = new Map<string, Booking[]>();
  (query.data ?? []).forEach((b) => {
    const key = new Date(b.scheduledAt).toISOString().slice(0, 10);
    const arr = byDay.get(key) ?? [];
    arr.push(b);
    byDay.set(key, arr);
  });
  const days = Array.from(byDay.entries()).sort();

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <View style={styles.header}>
        <Text style={styles.title}>Takvim</Text>
        <Text style={styles.subtitle}>Önümüzdeki 14 gün</Text>
      </View>

      <ScrollView
        contentContainerStyle={styles.scroll}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl
            refreshing={query.isFetching && !query.isLoading}
            onRefresh={() => query.refetch()}
            tintColor={Colors.ink}
          />
        }
      >
        {query.isLoading ? (
          <SkeletonList count={4} render={() => <SkeletonBookingRow />} />
        ) : query.isError ? (
          <EmptyState
            icon="alert"
            title="Bağlantı hatası"
            description="Sunucuya ulaşılamıyor."
            actionLabel="Tekrar dene"
            onAction={() => query.refetch()}
          />
        ) : days.length === 0 ? (
          <EmptyState icon="calendar" title="Önümüzdeki 14 gün boş" description="Bu süre içinde randevun yok." />
        ) : (
          days.map(([day, bookings]) => {
            const date = new Date(day);
            const dayShort = date.toLocaleDateString('tr-TR', { weekday: 'short' });
            const dayNum = date.getDate();
            const monthShort = date.toLocaleDateString('tr-TR', { month: 'short' });
            const isToday = date.toDateString() === new Date().toDateString();
            return (
              <View key={day} style={styles.daySection}>
                <View style={styles.dayHeader}>
                  <View style={[styles.dateBadge, isToday && styles.dateBadgeToday]}>
                    <Text style={[styles.dateNum, isToday && styles.dateNumToday]}>{dayNum}</Text>
                    <Text style={[styles.dateMonth, isToday && styles.dateMonthToday]}>{monthShort}</Text>
                  </View>
                  <View style={{ flex: 1 }}>
                    <Text style={styles.dayLabel}>{dayShort}</Text>
                    <Text style={styles.dayCount}>{bookings.length} randevu</Text>
                  </View>
                </View>
                <View style={styles.bookingsList}>
                  {bookings.map((b) => {
                    const status = bookingStatusInfo(b.status);
                    return (
                      <Pressable
                        key={b.id}
                        onPress={() => router.push(`/booking/${b.id}`)}
                        accessibilityRole="button"
                        accessibilityLabel={`${formatTime(b.scheduledAt)} ${b.items[0]?.serviceName ?? ''} ${status.label}`}
                        style={({ pressed }) => [{ opacity: pressed ? 0.7 : 1 }]}
                      >
                        <Card style={[styles.row, { borderLeftWidth: 3, borderLeftColor: toneBorderColor(status.tone) }]}>
                          <View style={styles.rowTime}>
                            <Text style={styles.time}>{formatTime(b.scheduledAt)}</Text>
                            <Text style={styles.duration}>
                              {b.items.reduce((s, i) => s + (i.durationMinutes ?? 0), 0)} dk
                            </Text>
                          </View>
                          <View style={styles.rowMid}>
                            <Text style={styles.serviceLine} numberOfLines={1}>
                              {b.items[0]?.serviceName ?? '—'}
                              {b.items.length > 1 ? ` +${b.items.length - 1}` : ''}
                            </Text>
                            {b.customerName ? (
                              <View style={styles.customerRow}>
                                <Icon name="user" size={11} color={Colors.textSecondary} />
                                <Text style={styles.customerLine} numberOfLines={1}>{b.customerName}</Text>
                              </View>
                            ) : null}
                            <Badge label={status.label} tone={status.tone} />
                          </View>
                          <Text style={styles.amount}>{formatTry(b.totalAmountMinor)}</Text>
                        </Card>
                      </Pressable>
                    );
                  })}
                </View>
              </View>
            );
          })
        )}
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: { paddingHorizontal: Layout.screenPaddingX, paddingTop: Spacing.lg, paddingBottom: Spacing.md },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text, letterSpacing: LetterSpacing.title },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 2 },
  scroll: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingBottom: Spacing.xl,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  daySection: { marginBottom: Spacing.lg },
  dayHeader: { flexDirection: 'row', alignItems: 'center', gap: Spacing.md, marginBottom: Spacing.sm },
  dateBadge: {
    width: 56,
    height: 56,
    borderRadius: Radius.md,
    backgroundColor: Colors.surface,
    borderWidth: 1,
    borderColor: Colors.border,
    alignItems: 'center',
    justifyContent: 'center',
  },
  dateBadgeToday: { backgroundColor: Colors.ink, borderColor: Colors.ink },
  dateNum: { fontSize: FontSize.xl, fontWeight: FontWeight.bold, color: Colors.text, lineHeight: 24, fontVariant: ['tabular-nums'] },
  dateNumToday: { color: Colors.textInverse },
  dateMonth: {
    fontSize: 10,
    color: Colors.textMuted,
    textTransform: 'uppercase',
    letterSpacing: LetterSpacing.label,
    fontWeight: FontWeight.semibold,
  },
  dateMonthToday: { color: Colors.textInverse, opacity: 0.85 },
  dayLabel: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, color: Colors.text, textTransform: 'capitalize' },
  dayCount: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 2 },
  bookingsList: { gap: Spacing.sm },
  row: { flexDirection: 'row', alignItems: 'center', gap: Spacing.md },
  rowTime: { width: 64 },
  time: { fontSize: FontSize.md, fontWeight: FontWeight.bold, color: Colors.ink, fontVariant: ['tabular-nums'] },
  duration: { fontSize: 11, color: Colors.textMuted, marginTop: 2, fontVariant: ['tabular-nums'] },
  rowMid: { flex: 1, gap: 4 },
  serviceLine: { fontSize: FontSize.sm, color: Colors.text, fontWeight: FontWeight.medium },
  customerRow: { flexDirection: 'row', alignItems: 'center', gap: 4, marginTop: 2 },
  customerLine: { fontSize: FontSize.xs, color: Colors.textSecondary },
  amount: { fontSize: FontSize.sm, color: Colors.ink, fontWeight: FontWeight.bold, fontVariant: ['tabular-nums'] },
});
