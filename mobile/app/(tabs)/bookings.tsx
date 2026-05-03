import { View, Text, StyleSheet, FlatList, Pressable, RefreshControl } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { Card } from '@/components/Card';
import { EmptyState } from '@/components/EmptyState';
import { Colors, FontSize, Spacing } from '@/lib/theme';
import { BookingApi, Booking } from '@/api/booking';
import { formatDateTime, formatTry } from '@/lib/format';

const STATUS_LABELS: Record<number, { text: string; color: string }> = {
  0: { text: 'Beklemede', color: Colors.warning },
  1: { text: 'Onaylandı', color: Colors.success },
  2: { text: 'Devam ediyor', color: Colors.info },
  3: { text: 'Tamamlandı', color: Colors.success },
  4: { text: 'İptal edildi', color: Colors.textMuted },
  5: { text: 'Gelmediniz', color: Colors.error },
};

export default function BookingsScreen() {
  const router = useRouter();
  const query = useQuery({
    queryKey: ['my-bookings'],
    queryFn: () => BookingApi.getMine(),
  });

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <View style={styles.header}>
        <Text style={styles.title}>Randevularım</Text>
      </View>

      {query.isLoading ? (
        <EmptyState title="Yükleniyor..." />
      ) : query.isError ? (
        <EmptyState
          title="Bağlantı hatası"
          description="Backend'e ulaşılamıyor."
        />
      ) : (query.data?.length ?? 0) === 0 ? (
        <EmptyState
          title="Henüz randevu yok"
          description="Ana sayfadan bir kuaför bul ve randevu al."
        />
      ) : (
        <FlatList
          data={query.data}
          keyExtractor={(b) => b.id}
          contentContainerStyle={styles.list}
          refreshControl={
            <RefreshControl refreshing={query.isFetching} onRefresh={() => query.refetch()} />
          }
          renderItem={({ item }) => <BookingRow booking={item} onPress={() => router.push(`/booking/${item.id}` as never)} />}
        />
      )}
    </SafeAreaView>
  );
}

function BookingRow({ booking, onPress }: { booking: Booking; onPress: () => void }) {
  const status = STATUS_LABELS[booking.status] ?? { text: '?', color: Colors.textMuted };
  return (
    <Pressable onPress={onPress}>
      <Card style={styles.card}>
        <View style={styles.row}>
          <Text style={styles.dateText}>{formatDateTime(booking.scheduledAt)}</Text>
          <View style={[styles.badge, { backgroundColor: status.color }]}>
            <Text style={styles.badgeText}>{status.text}</Text>
          </View>
        </View>
        <View style={styles.servicesRow}>
          {booking.items.map((it) => (
            <Text key={it.id} style={styles.serviceText} numberOfLines={1}>
              • {it.serviceName} ({it.durationMinutes} dk)
            </Text>
          ))}
        </View>
        <Text style={styles.amount}>{formatTry(booking.totalAmountMinor)}</Text>
      </Card>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: { padding: Spacing.lg },
  title: { fontSize: FontSize.xxl, fontWeight: '700', color: Colors.text },
  list: { paddingHorizontal: Spacing.lg, paddingBottom: Spacing.lg, gap: Spacing.md },
  card: { gap: Spacing.sm },
  row: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  dateText: { fontSize: FontSize.md, color: Colors.text, fontWeight: '600' },
  badge: { paddingHorizontal: Spacing.sm, paddingVertical: 4, borderRadius: 999 },
  badgeText: { color: '#fff', fontSize: FontSize.xs, fontWeight: '600' },
  servicesRow: { gap: 2 },
  serviceText: { fontSize: FontSize.sm, color: Colors.textSecondary },
  amount: { fontSize: FontSize.lg, fontWeight: '700', color: Colors.primary, marginTop: Spacing.xs },
});
