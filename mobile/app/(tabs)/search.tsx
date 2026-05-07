import { useState } from 'react';
import { View, Text, StyleSheet, FlatList, Pressable, RefreshControl, ScrollView, Modal } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useQuery } from '@tanstack/react-query';
import { Card } from '@/components/Card';
import { Badge } from '@/components/Badge';
import { Chip } from '@/components/Chip';
import { Button } from '@/components/Button';
import { EmptyState } from '@/components/EmptyState';
import { SkeletonShopCard, SkeletonList } from '@/components/Skeleton';
import { Colors, FontSize, FontWeight, Radius, Spacing, Layout } from '@/lib/theme';
import { BarberApi, BarberSearchResult } from '@/api/barber';
import { formatTry, salonGenderLabel, salonGenderTone } from '@/lib/format';

type GenderFilter = 'all' | 'unisex' | 'female' | 'male' | 'walkin';
type RatingFilter = null | 3 | 4 | 4.5;
type PriceFilter = null | 200 | 500 | 1000 | 2000;

export default function SearchScreen() {
  const params = useLocalSearchParams<{ cityId?: string; cityName?: string }>();
  const router = useRouter();
  const [filter, setFilter] = useState<GenderFilter>('all');
  const [minRating, setMinRating] = useState<RatingFilter>(null);
  const [maxPrice, setMaxPrice] = useState<PriceFilter>(null);
  const [filterModalOpen, setFilterModalOpen] = useState(false);

  const advancedActiveCount = (minRating !== null ? 1 : 0) + (maxPrice !== null ? 1 : 0);

  const query = useQuery({
    queryKey: ['barbers-search', params.cityId, filter, minRating, maxPrice],
    queryFn: () =>
      BarberApi.search({
        cityId: params.cityId,
        genderType: filter === 'unisex' ? 'Unisex' : filter === 'female' ? 'FemaleOnly' : filter === 'male' ? 'MaleOnly' : undefined,
        walkInOnly: filter === 'walkin' ? true : undefined,
        minRating: minRating ?? undefined,
        maxPriceMinor: maxPrice ? maxPrice * 100 : undefined,
        page: 1,
        pageSize: 20,
      }),
  });

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <View style={styles.header}>
        <Pressable onPress={() => router.back()} style={styles.backBtn}>
          <Text style={styles.back}>← Geri</Text>
        </Pressable>
        <Text style={styles.title}>{params.cityName ?? 'Tüm kuaförler'}</Text>
        <Text style={styles.subtitle}>
          {query.isLoading ? 'Aranıyor…' : `${query.data?.totalCount ?? 0} kuaför`}
        </Text>
      </View>

      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={styles.filtersRow}
      >
        <Pressable
          onPress={() => setFilterModalOpen(true)}
          style={[styles.filterTrigger, advancedActiveCount > 0 && styles.filterTriggerActive]}
          accessibilityRole="button"
          accessibilityLabel={`Gelişmiş filtre, ${advancedActiveCount} aktif`}
        >
          <Text style={[styles.filterTriggerText, advancedActiveCount > 0 && styles.filterTriggerTextActive]}>
            ⚙ Filtre{advancedActiveCount > 0 ? ` · ${advancedActiveCount}` : ''}
          </Text>
        </Pressable>
        <Chip label="Tümü" selected={filter === 'all'} onPress={() => setFilter('all')} />
        <Chip label="Şu an boş" selected={filter === 'walkin'} onPress={() => setFilter('walkin')} />
        <Chip label="Unisex" selected={filter === 'unisex'} onPress={() => setFilter('unisex')} />
        <Chip label="Kadın" selected={filter === 'female'} onPress={() => setFilter('female')} />
        <Chip label="Erkek" selected={filter === 'male'} onPress={() => setFilter('male')} />
      </ScrollView>

      <FilterModal
        visible={filterModalOpen}
        onClose={() => setFilterModalOpen(false)}
        minRating={minRating}
        setMinRating={setMinRating}
        maxPrice={maxPrice}
        setMaxPrice={setMaxPrice}
      />


      <FlatList
        data={query.data?.items ?? []}
        keyExtractor={(item) => item.id}
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
            <SkeletonList count={4} render={() => <SkeletonShopCard />} />
          ) : query.isError ? (
            <EmptyState
              emoji="⚠️"
              title="Bağlantı hatası"
              description="Sunucuya ulaşılamadı."
              actionLabel="Tekrar dene"
              onAction={() => query.refetch()}
            />
          ) : (
            <EmptyState
              emoji="✂️"
              title="Bu kriterde kuaför yok"
              description="Filtreyi değiştir veya başka bir şehir seç."
            />
          )
        }
        renderItem={({ item }) => (
          <BarberCard barber={item} onPress={() => router.push(`/barber/${item.id}` as never)} />
        )}
      />
    </SafeAreaView>
  );
}

function BarberCard({ barber, onPress }: { barber: BarberSearchResult; onPress: () => void }) {
  const genderTone = salonGenderTone(barber.genderType);
  const genderLabel = salonGenderLabel(barber.genderType);
  const hasRating = barber.ratingCount > 0 && barber.ratingAverage != null;
  return (
    <Card onPress={onPress} style={{ gap: Spacing.sm }}>
      <View style={styles.cardHeader}>
        <View style={{ flex: 1 }}>
          <Text style={styles.shopName} numberOfLines={1}>{barber.name}</Text>
          <View style={styles.ratingRow}>
            {hasRating ? (
              <>
                <Text style={styles.ratingStars}>⭐</Text>
                <Text style={styles.ratingValue}>{barber.ratingAverage!.toFixed(1)}</Text>
                <Text style={styles.ratingCount}>({barber.ratingCount})</Text>
              </>
            ) : (
              <Text style={styles.ratingNew}>✨ Yeni</Text>
            )}
            {barber.addressLine1 ? (
              <>
                <Text style={styles.ratingDot}>·</Text>
                <Text style={styles.address} numberOfLines={1}>📍 {barber.addressLine1}</Text>
              </>
            ) : null}
          </View>
        </View>
        <Badge label={genderLabel} tone={genderTone as any} />
      </View>

      {barber.description ? (
        <Text style={styles.description} numberOfLines={2}>{barber.description}</Text>
      ) : null}

      <View style={styles.footerRow}>
        <View style={styles.statsRow}>
          <Text style={styles.stat}>👥 {barber.stylistCount}</Text>
          <Text style={styles.statDivider}>·</Text>
          <Text style={styles.stat}>✂️ {barber.serviceCount}</Text>
        </View>
        {barber.minPriceMinor != null ? (
          <Text style={styles.price}>{formatTry(barber.minPriceMinor)}'den</Text>
        ) : null}
      </View>

      {barber.hasWalkInAvailable ? (
        <View style={styles.walkInRow}>
          <View style={styles.dot} />
          <Text style={styles.walkInText}>Şu an müsait — sıraya gir</Text>
        </View>
      ) : null}
    </Card>
  );
}

function FilterModal({
  visible,
  onClose,
  minRating,
  setMinRating,
  maxPrice,
  setMaxPrice,
}: {
  visible: boolean;
  onClose: () => void;
  minRating: RatingFilter;
  setMinRating: (v: RatingFilter) => void;
  maxPrice: PriceFilter;
  setMaxPrice: (v: PriceFilter) => void;
}) {
  function reset() {
    setMinRating(null);
    setMaxPrice(null);
  }
  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
      <Pressable style={styles.modalBackdrop} onPress={onClose}>
        <Pressable style={styles.modalCard} onPress={(e) => e.stopPropagation()}>
          <View style={styles.modalHandle} />
          <Text style={styles.modalTitle}>Gelişmiş filtre</Text>

          <Text style={styles.modalSection}>Minimum puan</Text>
          <View style={styles.modalChips}>
            <Chip label="Tümü" selected={minRating === null} onPress={() => setMinRating(null)} />
            <Chip label="3+" selected={minRating === 3} onPress={() => setMinRating(3)} />
            <Chip label="4+" selected={minRating === 4} onPress={() => setMinRating(4)} />
            <Chip label="4.5+" selected={minRating === 4.5} onPress={() => setMinRating(4.5)} />
          </View>

          <Text style={styles.modalSection}>Maksimum fiyat</Text>
          <View style={styles.modalChips}>
            <Chip label="Tümü" selected={maxPrice === null} onPress={() => setMaxPrice(null)} />
            <Chip label="200₺" selected={maxPrice === 200} onPress={() => setMaxPrice(200)} />
            <Chip label="500₺" selected={maxPrice === 500} onPress={() => setMaxPrice(500)} />
            <Chip label="1000₺" selected={maxPrice === 1000} onPress={() => setMaxPrice(1000)} />
            <Chip label="2000₺" selected={maxPrice === 2000} onPress={() => setMaxPrice(2000)} />
          </View>

          <View style={styles.modalActions}>
            <Button label="Sıfırla" variant="ghost" onPress={reset} />
            <Button label="Uygula" onPress={onClose} />
          </View>
        </Pressable>
      </Pressable>
    </Modal>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: { paddingHorizontal: Layout.screenPaddingX, paddingTop: Spacing.md, paddingBottom: Spacing.sm },
  backBtn: { alignSelf: 'flex-start', paddingVertical: 4 },
  back: { color: Colors.primary, fontSize: FontSize.sm, fontWeight: FontWeight.medium },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text, marginTop: 6 },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 2 },
  filtersRow: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingVertical: Spacing.md,
    gap: 8,
    flexDirection: 'row',
    alignItems: 'center',
  },
  filterTrigger: {
    paddingHorizontal: Spacing.md,
    paddingVertical: 8,
    borderRadius: Radius.full,
    backgroundColor: Colors.surface,
    borderWidth: 1,
    borderColor: Colors.borderStrong,
  },
  filterTriggerActive: { backgroundColor: Colors.primarySoft, borderColor: Colors.primary },
  filterTriggerText: { fontSize: FontSize.sm, color: Colors.text, fontWeight: FontWeight.semibold },
  filterTriggerTextActive: { color: Colors.primary },

  modalBackdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.4)', justifyContent: 'flex-end' },
  modalCard: {
    backgroundColor: Colors.surface,
    borderTopLeftRadius: Radius.lg,
    borderTopRightRadius: Radius.lg,
    padding: Spacing.lg,
    gap: Spacing.md,
  },
  modalHandle: {
    width: 40, height: 4, borderRadius: 2,
    backgroundColor: Colors.borderStrong,
    alignSelf: 'center', marginBottom: 4,
  },
  modalTitle: { fontSize: FontSize.lg, fontWeight: FontWeight.bold, color: Colors.text },
  modalSection: {
    fontSize: FontSize.xs, color: Colors.textSecondary,
    textTransform: 'uppercase', letterSpacing: 0.5,
    fontWeight: FontWeight.semibold, marginTop: Spacing.sm,
  },
  modalChips: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  modalActions: { flexDirection: 'row', gap: Spacing.sm, justifyContent: 'flex-end', marginTop: Spacing.md },
  list: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingBottom: Spacing.xl,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  cardHeader: { flexDirection: 'row', alignItems: 'flex-start', gap: Spacing.sm },
  shopName: { fontSize: FontSize.lg, fontWeight: FontWeight.semibold, color: Colors.text },
  ratingRow: { flexDirection: 'row', alignItems: 'center', gap: 4, marginTop: 4, flexWrap: 'wrap' },
  ratingStars: { fontSize: 12 },
  ratingValue: { fontSize: FontSize.xs, color: Colors.text, fontWeight: FontWeight.bold },
  ratingCount: { fontSize: FontSize.xs, color: Colors.textMuted },
  ratingNew: { fontSize: FontSize.xs, color: Colors.primary, fontWeight: FontWeight.semibold },
  ratingDot: { fontSize: FontSize.xs, color: Colors.textMuted, marginHorizontal: 2 },
  address: { fontSize: FontSize.xs, color: Colors.textMuted, flexShrink: 1 },
  description: { fontSize: FontSize.sm, color: Colors.textSecondary, lineHeight: 20 },
  footerRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingTop: Spacing.sm,
    borderTopWidth: 1,
    borderTopColor: Colors.divider,
  },
  statsRow: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  stat: { fontSize: FontSize.xs, color: Colors.textSecondary },
  statDivider: { color: Colors.textMuted },
  price: { fontSize: FontSize.sm, color: Colors.primary, fontWeight: FontWeight.bold },
  walkInRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    backgroundColor: Colors.successSoft,
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: Radius.sm,
    alignSelf: 'flex-start',
  },
  dot: { width: 8, height: 8, borderRadius: 4, backgroundColor: Colors.success },
  walkInText: { fontSize: FontSize.xs, color: Colors.success, fontWeight: FontWeight.semibold },
});
