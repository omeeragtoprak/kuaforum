import { useMemo, useState } from 'react';
import { View, Text, StyleSheet, FlatList, RefreshControl } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { Card } from '@/components/Card';
import { Badge } from '@/components/Badge';
import { Chip } from '@/components/Chip';
import { EmptyState } from '@/components/EmptyState';
import { ErrorState } from '@/components/ErrorState';
import { AuthPrompt } from '@/components/AuthPrompt';
import { Skeleton, SkeletonBookingRow, SkeletonList } from '@/components/Skeleton';
import { Colors, FontSize, FontWeight, Radius, Spacing, Layout } from '@/lib/theme';
import { BookingApi, Booking } from '@/api/booking';
import { useAuthStore } from '@/store/auth-store';
import {
  formatDateTime, formatTry, bookingStatusInfo,
  isBookingUpcoming, isBookingCompleted, isBookingCancelled,
} from '@/lib/format';

type FilterKey = 'all' | 'upcoming' | 'completed' | 'cancelled';

export default function BookingsScreen() {
  const router = useRouter();
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const [filter, setFilter] = useState<FilterKey>('all');
  const query = useQuery({
    queryKey: ['my-bookings'],
    queryFn: () => BookingApi.getMine(),
    enabled: isAuthenticated,
  });

  const all = query.data ?? [];
  const counts = useMemo(() => ({
    all: all.length,
    upcoming: all.filter((b) => isBookingUpcoming(b.status)).length,
    completed: all.filter((b) => isBookingCompleted(b.status)).length,
    cancelled: all.filter((b) => isBookingCancelled(b.status)).length,
  }), [all]);

  const filtered = useMemo(() => {
    switch (filter) {
      case 'upcoming': return all.filter((b) => isBookingUpcoming(b.status));
      case 'completed': return all.filter((b) => isBookingCompleted(b.status));
      case 'cancelled': return all.filter((b) => isBookingCancelled(b.status));
      default: return all;
    }
  }, [all, filter]);

  // RFC-010 — misafir "randevularım" sekmesine geldiğinde global redirect yerine
  // burada AuthPrompt gösterilir; girişten sonra aynı sekmeye döner.
  if (!isAuthenticated) {
    return (
      <AuthPrompt
        description="Randevularını görmek için giriş yapman gerekiyor."
        redirectTo="/(tabs)/bookings"
      />
    );
  }

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <View style={styles.header}>
        <Text style={styles.title}>Randevularım</Text>
        {query.isLoading ? (
          // G15 fix: gövde artık SkeletonBookingRow gösteriyor; başlık altındaki bu
          // tali metin de düz "Yükleniyor…" yerine aynı iskelet diline geçti.
          <Skeleton width={90} height={13} style={{ marginTop: 2 }} />
        ) : (
          <Text style={styles.subtitle}>{`${all.length} randevu`}</Text>
        )}
      </View>

      {all.length > 0 ? (
        <View style={styles.filtersRow}>
          <Chip label={`Tümü · ${counts.all}`} selected={filter === 'all'} onPress={() => setFilter('all')} />
          <Chip label={`Yaklaşan · ${counts.upcoming}`} selected={filter === 'upcoming'} onPress={() => setFilter('upcoming')} />
          <Chip label={`Tamamlanan · ${counts.completed}`} selected={filter === 'completed'} onPress={() => setFilter('completed')} />
          <Chip label={`İptal · ${counts.cancelled}`} selected={filter === 'cancelled'} onPress={() => setFilter('cancelled')} />
        </View>
      ) : null}

      <FlatList
        data={filtered}
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
            <ErrorState
              error={query.error}
              onRetry={() => query.refetch()}
              notFoundTitle="Randevu listesi bulunamadı"
              notFoundDescription="Bu kaynak kaldırılmış görünüyor."
              notFoundActionLabel="Tekrar dene"
              onNotFoundAction={() => query.refetch()}
              networkTitle="Bağlantı hatası"
              networkDescription="Sunucuya ulaşılamıyor."
            />
          ) : all.length === 0 ? (
            <EmptyState
              emoji="📅"
              title="Henüz randevun yok"
              description="Keşfet sekmesinden bir kuaför bul ve ilk randevunu oluştur."
              actionLabel="Kuaför bul"
              onAction={() => router.push('/(tabs)/' as never)}
            />
          ) : (
            <EmptyState
              emoji="🔎"
              title={emptyFilterTitle(filter)}
              description="Bu filtreye uyan randevu yok. Başka bir filtre dene."
              actionLabel="Tüm randevuları göster"
              onAction={() => setFilter('all')}
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

function emptyFilterTitle(f: FilterKey): string {
  switch (f) {
    case 'upcoming': return 'Yaklaşan randevu yok';
    case 'completed': return 'Tamamlanan randevu yok';
    case 'cancelled': return 'İptal edilmiş randevu yok';
    default: return 'Eşleşen randevu yok';
  }
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

      {isBookingCompleted(booking.status) && booking.hasReview === false ? (
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
  header: { paddingHorizontal: Layout.screenPaddingX, paddingTop: Spacing.lg, paddingBottom: Spacing.sm },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 2 },
  filtersRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 8,
    paddingHorizontal: Layout.screenPaddingX,
    paddingBottom: Spacing.sm,
  },
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
