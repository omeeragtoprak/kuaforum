import { View, Text, StyleSheet, FlatList, RefreshControl } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { Card } from '@/components/Card';
import { Badge } from '@/components/Badge';
import { EmptyState } from '@/components/EmptyState';
import { Button } from '@/components/Button';
import { SkeletonBookingRow, SkeletonList } from '@/components/Skeleton';
import { Colors, FontSize, FontWeight, Radius, Spacing, Layout } from '@/lib/theme';
import { BookingApi, Booking } from '@/api/booking';
import { formatDateTime, formatTry, bookingStatusInfo, isBookingUpcoming } from '@/lib/format';

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
        <Text style={styles.subtitle}>
          {query.isLoading ? 'Yükleniyor…' : `${query.data?.length ?? 0} randevu`}
        </Text>
      </View>

      <FlatList
        data={query.data ?? []}
        keyExtractor={(b) => b.id}
        contentContainerStyle={styles.list}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl
            refreshing={query.isFetching && !query.isLoading}
            onRefresh={() => query.refetch()}
            tintColor={Colors.primary}
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
              description="Sunucuya ulaşılamıyor."
              actionLabel="Tekrar dene"
              onAction={() => query.refetch()}
            />
          ) : (
            <EmptyState
              emoji="📅"
              title="Henüz randevun yok"
              description="Keşfet sekmesinden bir kuaför bul ve ilk randevunu oluştur."
              actionLabel="Kuaför bul"
              onAction={() => router.push('/(tabs)/' as never)}
            />
          )
        }
        renderItem={({ item }) => (
          <BookingRow booking={item} onPress={() => router.push(`/booking/${item.id}` as never)} />
        )}
      />
    </SafeAreaView>
  );
}

function BookingRow({ booking, onPress }: { booking: Booking; onPress: () => void }) {
  const status = bookingStatusInfo(booking.status);
  const isUpcoming = isBookingUpcoming(booking.status);
  return (
    <Card onPress={onPress} style={{ gap: Spacing.sm }}>
      <View style={styles.row}>
        <View style={{ flex: 1 }}>
          <Text style={styles.dateText}>{formatDateTime(booking.scheduledAt)}</Text>
          {booking.shopName ? (
            <Text style={styles.shopText} numberOfLines={1}>📍 {booking.shopName}</Text>
          ) : null}
          {booking.stylistName ? (
            <Text style={styles.shopText} numberOfLines={1}>✂️ {booking.stylistName}</Text>
          ) : null}
        </View>
        <Badge label={status.label} tone={status.tone} />
      </View>

      <View style={styles.servicesBlock}>
        {booking.items.slice(0, 3).map((it) => (
          <View key={it.id} style={styles.serviceRow}>
            <Text style={styles.serviceDot}>•</Text>
            <Text style={styles.serviceText} numberOfLines={1}>
              {it.serviceName}
            </Text>
            <Text style={styles.serviceMeta}>{it.durationMinutes} dk</Text>
          </View>
        ))}
        {booking.items.length > 3 ? (
          <Text style={styles.moreText}>+{booking.items.length - 3} daha</Text>
        ) : null}
      </View>

      <View style={styles.footerRow}>
        <Text style={styles.totalLabel}>Toplam</Text>
        <Text style={styles.amount}>{formatTry(booking.totalAmountMinor)}</Text>
      </View>

      {isUpcoming ? (
        <View style={styles.upcomingHint}>
          <View style={styles.dot} />
          <Text style={styles.upcomingText}>Yaklaşan randevu</Text>
        </View>
      ) : null}

      {(booking.status === 'Completed' || booking.status === 4) && booking.hasReview === false ? (
        <View style={styles.reviewPrompt}>
          <Text style={styles.reviewPromptText}>⭐ Bu randevu için yorum yazmadın</Text>
          <Text style={styles.reviewPromptHint}>Karta dokun → "Yorum yap"</Text>
        </View>
      ) : null}
    </Card>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: { paddingHorizontal: Layout.screenPaddingX, paddingTop: Spacing.lg, paddingBottom: Spacing.md },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 2 },
  list: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingBottom: Spacing.xl,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  row: { flexDirection: 'row', alignItems: 'flex-start', gap: Spacing.sm },
  dateText: { fontSize: FontSize.md, color: Colors.text, fontWeight: FontWeight.semibold },
  shopText: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 2 },
  servicesBlock: { gap: 4, marginTop: 2 },
  serviceRow: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  serviceDot: { color: Colors.primary, fontWeight: FontWeight.bold },
  serviceText: { flex: 1, fontSize: FontSize.sm, color: Colors.text },
  serviceMeta: { fontSize: FontSize.xs, color: Colors.textMuted },
  moreText: { fontSize: FontSize.xs, color: Colors.textMuted, fontStyle: 'italic' },
  footerRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingTop: Spacing.sm,
    borderTopWidth: 1,
    borderTopColor: Colors.divider,
  },
  totalLabel: { fontSize: FontSize.xs, color: Colors.textSecondary, textTransform: 'uppercase', letterSpacing: 0.5 },
  amount: { fontSize: FontSize.lg, fontWeight: FontWeight.bold, color: Colors.primary },
  upcomingHint: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    backgroundColor: Colors.infoSoft,
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: Radius.sm,
    alignSelf: 'flex-start',
  },
  dot: { width: 8, height: 8, borderRadius: 4, backgroundColor: Colors.info },
  upcomingText: { fontSize: FontSize.xs, color: Colors.info, fontWeight: FontWeight.semibold },
  reviewPrompt: {
    backgroundColor: Colors.warningSoft,
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: Radius.sm,
  },
  reviewPromptText: { fontSize: FontSize.xs, color: Colors.warning, fontWeight: FontWeight.bold },
  reviewPromptHint: { fontSize: 10, color: Colors.warning, opacity: 0.8, marginTop: 1 },
});
