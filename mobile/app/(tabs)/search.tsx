import { View, Text, StyleSheet, FlatList, Pressable, RefreshControl } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useQuery } from '@tanstack/react-query';
import { Card } from '@/components/Card';
import { EmptyState } from '@/components/EmptyState';
import { Colors, FontSize, Radius, Spacing } from '@/lib/theme';
import { BarberApi, BarberSearchResult } from '@/api/barber';
import { formatTry } from '@/lib/format';

const GENDER_BADGE: Record<number, { text: string; color: string }> = {
  0: { text: 'Unisex', color: Colors.info },
  1: { text: 'Sadece Kadın', color: '#EC4899' },
  2: { text: 'Sadece Erkek', color: Colors.primaryDark },
};

export default function SearchScreen() {
  const params = useLocalSearchParams<{ cityId?: string; cityName?: string }>();
  const router = useRouter();

  const query = useQuery({
    queryKey: ['barbers-search', params.cityId],
    queryFn: () =>
      BarberApi.search({
        cityId: params.cityId,
        page: 1,
        pageSize: 20,
      }),
  });

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <View style={styles.header}>
        <Pressable onPress={() => router.back()}>
          <Text style={styles.back}>← Geri</Text>
        </Pressable>
        <Text style={styles.title}>{params.cityName ?? 'Tüm kuaförler'}</Text>
        <Text style={styles.subtitle}>{query.data?.totalCount ?? 0} kuaför bulundu</Text>
      </View>

      {query.isLoading ? (
        <EmptyState title="Yükleniyor..." />
      ) : query.isError ? (
        <EmptyState title="Bağlantı hatası" description="Backend'e ulaşılamıyor." />
      ) : (query.data?.items.length ?? 0) === 0 ? (
        <EmptyState
          title="Bu şehirde kuaför yok"
          description="Demo veride bu şehir kapsanmadı. İstanbul, Ankara, İzmir'de demo kuaför var (Seed:Demo=true)."
        />
      ) : (
        <FlatList
          data={query.data!.items}
          keyExtractor={(item) => item.id}
          contentContainerStyle={styles.list}
          refreshControl={<RefreshControl refreshing={query.isFetching} onRefresh={() => query.refetch()} />}
          renderItem={({ item }) => (
            <BarberCard barber={item} onPress={() => router.push(`/barber/${item.id}` as never)} />
          )}
        />
      )}
    </SafeAreaView>
  );
}

function BarberCard({ barber, onPress }: { barber: BarberSearchResult; onPress: () => void }) {
  const badge = GENDER_BADGE[barber.genderType] ?? GENDER_BADGE[0];
  return (
    <Pressable onPress={onPress}>
      <Card style={styles.card}>
        <View style={styles.cardHeader}>
          <Text style={styles.shopName}>{barber.name}</Text>
          <View style={[styles.badge, { backgroundColor: badge.color }]}>
            <Text style={styles.badgeText}>{badge.text}</Text>
          </View>
        </View>
        {barber.description ? (
          <Text style={styles.description} numberOfLines={2}>{barber.description}</Text>
        ) : null}
        {barber.addressLine1 ? <Text style={styles.address}>📍 {barber.addressLine1}</Text> : null}
        <View style={styles.footer}>
          <Text style={styles.meta}>
            {barber.stylistCount} stilist · {barber.serviceCount} hizmet
          </Text>
          {barber.minPriceMinor !== undefined && barber.minPriceMinor !== null ? (
            <Text style={styles.price}>{formatTry(barber.minPriceMinor)}'den</Text>
          ) : null}
        </View>
        {barber.hasWalkInAvailable ? (
          <View style={styles.walkInBadge}>
            <Text style={styles.walkInText}>🟢 Şu an boş</Text>
          </View>
        ) : null}
      </Card>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: { padding: Spacing.lg },
  back: { color: Colors.primary, fontSize: FontSize.sm, marginBottom: Spacing.sm },
  title: { fontSize: FontSize.xxl, fontWeight: '700', color: Colors.text },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: Spacing.xs },
  list: { padding: Spacing.lg, paddingTop: 0, gap: Spacing.md },
  card: { gap: Spacing.sm },
  cardHeader: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  shopName: { fontSize: FontSize.lg, fontWeight: '600', color: Colors.text, flex: 1 },
  badge: { paddingHorizontal: Spacing.sm, paddingVertical: 4, borderRadius: 999, marginLeft: Spacing.sm },
  badgeText: { color: '#fff', fontSize: FontSize.xs, fontWeight: '600' },
  description: { fontSize: FontSize.sm, color: Colors.textSecondary },
  address: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 2 },
  footer: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginTop: Spacing.sm,
    paddingTop: Spacing.sm,
    borderTopWidth: 1,
    borderTopColor: Colors.border,
  },
  meta: { fontSize: FontSize.xs, color: Colors.textMuted },
  price: { fontSize: FontSize.sm, color: Colors.primary, fontWeight: '600' },
  walkInBadge: {
    alignSelf: 'flex-start',
    paddingHorizontal: Spacing.sm,
    paddingVertical: 2,
    borderRadius: Radius.sm,
    backgroundColor: 'rgba(34, 197, 94, 0.1)',
    marginTop: Spacing.xs,
  },
  walkInText: { fontSize: FontSize.xs, color: Colors.success, fontWeight: '600' },
});
