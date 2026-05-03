import { View, Text, StyleSheet, ScrollView, RefreshControl } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery } from '@tanstack/react-query';
import { Card } from '@/components/Card';
import { EmptyState } from '@/components/EmptyState';
import { Colors, FontSize, Spacing } from '@/lib/theme';
import { BookingApi } from '@/api/booking';
import { formatDateTime, formatTry } from '@/lib/format';

export default function BarberCalendarScreen() {
  const start = new Date();
  start.setHours(0, 0, 0, 0);
  const end = new Date(start);
  end.setDate(end.getDate() + 14);

  const query = useQuery({
    queryKey: ['owner-bookings-2weeks'],
    queryFn: () => BookingApi.getOwnerBookings(start.toISOString(), end.toISOString()),
  });

  // Group by day (yyyy-mm-dd)
  const byDay = new Map<string, typeof query.data>();
  (query.data ?? []).forEach((b) => {
    const key = new Date(b.scheduledAt).toISOString().slice(0, 10);
    const arr = byDay.get(key) ?? [];
    arr.push(b);
    byDay.set(key, arr as never);
  });
  const days = Array.from(byDay.entries()).sort();

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <View style={styles.header}>
        <Text style={styles.title}>Takvim</Text>
        <Text style={styles.subtitle}>Önümüzdeki 14 gün</Text>
      </View>

      {query.isLoading ? (
        <EmptyState title="Yükleniyor..." />
      ) : days.length === 0 ? (
        <EmptyState title="Bu 14 günde randevu yok" />
      ) : (
        <ScrollView
          contentContainerStyle={styles.scroll}
          refreshControl={<RefreshControl refreshing={query.isFetching} onRefresh={() => query.refetch()} />}
        >
          {days.map(([day, bookings]) => (
            <View key={day} style={styles.section}>
              <Text style={styles.dayHeader}>
                {new Date(day).toLocaleDateString('tr-TR', { weekday: 'short', day: '2-digit', month: 'short' })}
                {' · '}
                {bookings?.length} randevu
              </Text>
              {bookings?.map((b) => (
                <Card key={b.id} style={styles.row}>
                  <Text style={styles.time}>{formatDateTime(b.scheduledAt)}</Text>
                  <Text style={styles.amount}>{formatTry(b.totalAmountMinor)}</Text>
                </Card>
              ))}
            </View>
          ))}
        </ScrollView>
      )}
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: { padding: Spacing.lg },
  title: { fontSize: FontSize.xxl, fontWeight: '700', color: Colors.text },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: Spacing.xs },
  scroll: { padding: Spacing.lg, paddingTop: 0 },
  section: { marginBottom: Spacing.lg, gap: Spacing.sm },
  dayHeader: { fontSize: FontSize.md, fontWeight: '600', color: Colors.text, marginBottom: Spacing.xs },
  row: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  time: { fontSize: FontSize.sm, color: Colors.text },
  amount: { fontSize: FontSize.sm, color: Colors.primary, fontWeight: '600' },
});
