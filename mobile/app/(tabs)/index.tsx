import { useEffect, useMemo, useState } from 'react';
import {
  View, Text, StyleSheet, FlatList, Pressable, RefreshControl, TextInput, Modal,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { Card } from '@/components/Card';
import { Badge } from '@/components/Badge';
import { Chip } from '@/components/Chip';
import { EmptyState } from '@/components/EmptyState';
import { SkeletonShopCard, SkeletonList } from '@/components/Skeleton';
import { Colors, FontSize, FontWeight, Radius, Spacing, Layout, Shadow } from '@/lib/theme';
import { GeographyApi, City } from '@/api/geography';
import { BarberApi, BarberSearchResult } from '@/api/barber';
import { useAuthStore } from '@/store/auth-store';
import { useDiscoveryStore, DEFAULT_CITY_PLATE, DEFAULT_CITY_NAME } from '@/store/discovery-store';
import { normalizeTr, formatTry, salonGenderLabel, salonGenderTone } from '@/lib/format';

type GenderFilter = 'all' | 'unisex' | 'female' | 'male' | 'walkin';

/**
 * Keşfet — varsayılan olarak kullanıcının (demo: Erzurum) şehrindeki kuaförleri
 * direkt gösterir. "📍 Şehir · Değiştir" banner'ından farklı şehir seçilebilir.
 * Üst arama input'u şehir değiştirme modalı yerine, kullanıcı şehir kartlarını
 * görmek istemiyorsa hızlı kuaför adı arar (V2 feature, şu an chip filter).
 */
export default function DiscoverScreen() {
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const { cityId, cityName, setCity } = useDiscoveryStore();
  const [genderFilter, setGenderFilter] = useState<GenderFilter>('all');
  const [cityModalOpen, setCityModalOpen] = useState(false);

  const citiesQuery = useQuery({
    queryKey: ['cities'],
    queryFn: () => GeographyApi.getCities(),
  });

  // Default city: Erzurum. Cities yüklenince auto-select if hiç seçili değilse.
  useEffect(() => {
    if (cityId || !citiesQuery.data) return;
    const erzurum = citiesQuery.data.find((c) => c.plateCode === DEFAULT_CITY_PLATE);
    if (erzurum) setCity(erzurum.id, erzurum.name);
  }, [citiesQuery.data, cityId, setCity]);

  const shopsQuery = useQuery({
    queryKey: ['discovery-shops', cityId, genderFilter],
    queryFn: () =>
      BarberApi.search({
        cityId: cityId ?? undefined,
        genderType:
          genderFilter === 'unisex' ? 'Unisex'
            : genderFilter === 'female' ? 'FemaleOnly'
              : genderFilter === 'male' ? 'MaleOnly' : undefined,
        walkInOnly: genderFilter === 'walkin' ? true : undefined,
        page: 1,
        pageSize: 30,
      }),
    enabled: !!cityId,
  });

  const shops = shopsQuery.data?.items ?? [];
  const totalShops = shopsQuery.data?.totalCount ?? 0;

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <FlatList
        data={shops}
        keyExtractor={(s) => s.id}
        contentContainerStyle={styles.list}
        keyboardShouldPersistTaps="handled"
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl
            refreshing={shopsQuery.isFetching && !shopsQuery.isLoading}
            onRefresh={() => shopsQuery.refetch()}
            tintColor={Colors.primary}
          />
        }
        ListHeaderComponent={
          <View>
            <View style={styles.hero}>
              <Text style={styles.greeting}>
                Merhaba{user?.displayName ? `, ${user.displayName.split(' ')[0]}` : ''} 👋
              </Text>
              <Text style={styles.title}>Salonunu bul</Text>
            </View>

            {/* Aktif şehir banner — büyük, tıklanır, "değiştir" CTA */}
            <Pressable
              onPress={() => setCityModalOpen(true)}
              style={styles.cityBanner}
              accessibilityRole="button"
              accessibilityLabel={`Aktif şehir: ${cityName ?? DEFAULT_CITY_NAME}. Değiştirmek için bas.`}
            >
              <View style={styles.cityBannerLeft}>
                <Text style={styles.cityPin}>📍</Text>
                <View>
                  <Text style={styles.cityBannerLabel}>BÖLGEN</Text>
                  <Text style={styles.cityBannerName}>{cityName ?? DEFAULT_CITY_NAME}</Text>
                </View>
              </View>
              <View style={styles.cityBannerRight}>
                <Text style={styles.cityBannerCount}>
                  {shopsQuery.isLoading ? '…' : `${totalShops} kuaför`}
                </Text>
                <Text style={styles.cityBannerChange}>değiştir ›</Text>
              </View>
            </Pressable>

            {/* Filter chips */}
            {totalShops > 0 || genderFilter !== 'all' ? (
              <View style={styles.filtersRow}>
                <Chip label="Tümü" selected={genderFilter === 'all'} onPress={() => setGenderFilter('all')} />
                <Chip label="Şu an boş" selected={genderFilter === 'walkin'} onPress={() => setGenderFilter('walkin')} />
                <Chip label="Unisex" selected={genderFilter === 'unisex'} onPress={() => setGenderFilter('unisex')} />
                <Chip label="Kadın" selected={genderFilter === 'female'} onPress={() => setGenderFilter('female')} />
                <Chip label="Erkek" selected={genderFilter === 'male'} onPress={() => setGenderFilter('male')} />
              </View>
            ) : null}
          </View>
        }
        ListEmptyComponent={
          citiesQuery.isError ? (
            <EmptyState
              emoji="📡"
              title="Şehirler yüklenemedi"
              description="Sunucuya ulaşılamadı. İnternet bağlantını kontrol edip tekrar dene."
              actionLabel="Tekrar dene"
              onAction={() => citiesQuery.refetch()}
            />
          ) : citiesQuery.isLoading || (!cityId && !citiesQuery.isError) || shopsQuery.isLoading ? (
            <SkeletonList count={4} render={() => <SkeletonShopCard />} />
          ) : shopsQuery.isError ? (
            <EmptyState
              emoji="⚠️"
              title="Bağlantı hatası"
              description="Sunucuya ulaşılamıyor."
              actionLabel="Tekrar dene"
              onAction={() => shopsQuery.refetch()}
            />
          ) : (
            <EmptyState
              emoji="✂️"
              title={`${cityName ?? '—'}'da henüz kuaför yok`}
              description="Yakında açılan ilk kuaföre sen olabilirsin. Başka bir şehir dene."
              actionLabel="Şehir değiştir"
              onAction={() => setCityModalOpen(true)}
            />
          )
        }
        ItemSeparatorComponent={() => <View style={{ height: Spacing.md }} />}
        renderItem={({ item }) => (
          <ShopCard barber={item} onPress={() => router.push(`/barber/${item.id}` as never)} />
        )}
      />

      <CityPickerModal
        visible={cityModalOpen}
        cities={citiesQuery.data ?? []}
        currentCityId={cityId}
        onClose={() => setCityModalOpen(false)}
        onPick={(c) => { setCity(c.id, c.name); setCityModalOpen(false); }}
      />
    </SafeAreaView>
  );
}

function ShopCard({ barber, onPress }: { barber: BarberSearchResult; onPress: () => void }) {
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

function CityPickerModal({
  visible, cities, currentCityId, onClose, onPick,
}: {
  visible: boolean;
  cities: City[];
  currentCityId: string | null;
  onClose: () => void;
  onPick: (city: City) => void;
}) {
  const [query, setQuery] = useState('');
  const filtered = useMemo(() => {
    const q = query.trim() ? normalizeTr(query.trim()) : null;
    const list = q ? cities.filter((c) => normalizeTr(c.name).includes(q)) : cities;
    // Hizmet veren önce, sonra alfabetik
    return [...list].sort((a, b) => {
      if ((a.activeShopCount > 0) !== (b.activeShopCount > 0)) {
        return a.activeShopCount > 0 ? -1 : 1;
      }
      return a.name.localeCompare(b.name, 'tr');
    });
  }, [cities, query]);

  return (
    <Modal visible={visible} animationType="slide" presentationStyle="pageSheet" onRequestClose={onClose}>
      <SafeAreaView style={styles.modalContainer} edges={['top']}>
        <View style={styles.modalHeader}>
          <Pressable onPress={onClose} hitSlop={8}>
            <Text style={styles.modalClose}>Kapat</Text>
          </Pressable>
          <Text style={styles.modalTitle}>Bölgeni seç</Text>
          <View style={{ width: 50 }} />
        </View>
        <View style={styles.modalSearchBox}>
          <Text style={styles.searchIcon}>🔍</Text>
          <TextInput
            value={query}
            onChangeText={setQuery}
            placeholder="Şehir ara…"
            placeholderTextColor={Colors.textMuted}
            style={styles.modalSearchInput}
            autoFocus
            returnKeyType="search"
          />
          {query ? (
            <Pressable onPress={() => setQuery('')} hitSlop={8}>
              <Text style={styles.clearIcon}>✕</Text>
            </Pressable>
          ) : null}
        </View>
        <FlatList
          data={filtered}
          keyExtractor={(c) => c.id}
          keyboardShouldPersistTaps="handled"
          contentContainerStyle={styles.modalList}
          ItemSeparatorComponent={() => <View style={{ height: 4 }} />}
          renderItem={({ item }) => {
            const active = item.id === currentCityId;
            const hasShops = item.activeShopCount > 0;
            return (
              <Pressable
                onPress={() => onPick(item)}
                style={({ pressed }) => [
                  styles.modalCityRow,
                  active && styles.modalCityRowActive,
                  pressed && styles.modalCityRowPressed,
                ]}
                accessibilityRole="button"
                accessibilityState={{ selected: active }}
              >
                <Text style={[styles.modalCityName, active && styles.modalCityNameActive]}>
                  {item.name}
                </Text>
                {hasShops ? (
                  <Text style={[styles.modalCityCount, active && styles.modalCityCountActive]}>
                    {item.activeShopCount} kuaför
                  </Text>
                ) : (
                  <Text style={styles.modalCitySoon}>yakında</Text>
                )}
              </Pressable>
            );
          }}
        />
      </SafeAreaView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  list: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingBottom: Spacing.xl,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  hero: { paddingTop: Spacing.lg, paddingBottom: Spacing.sm },
  greeting: { fontSize: FontSize.sm, color: Colors.textSecondary, marginBottom: 6 },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text },

  cityBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    backgroundColor: Colors.primary,
    borderRadius: Radius.lg,
    padding: Spacing.lg,
    marginVertical: Spacing.md,
    ...Shadow.md,
  },
  cityBannerLeft: { flexDirection: 'row', alignItems: 'center', gap: Spacing.md, flex: 1 },
  cityPin: { fontSize: 28 },
  cityBannerLabel: {
    fontSize: 10,
    color: Colors.textInverse,
    opacity: 0.7,
    fontWeight: FontWeight.bold,
    letterSpacing: 1,
  },
  cityBannerName: { fontSize: FontSize.xl, color: Colors.textInverse, fontWeight: FontWeight.bold },
  cityBannerRight: { alignItems: 'flex-end' },
  cityBannerCount: { fontSize: FontSize.sm, color: Colors.textInverse, fontWeight: FontWeight.semibold },
  cityBannerChange: { fontSize: FontSize.xs, color: Colors.textInverse, opacity: 0.85, marginTop: 2 },

  filtersRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 8,
    marginBottom: Spacing.md,
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

  // City picker modal
  modalContainer: { flex: 1, backgroundColor: Colors.background },
  modalHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: Layout.screenPaddingX,
    paddingVertical: Spacing.md,
    borderBottomWidth: 1,
    borderBottomColor: Colors.border,
  },
  modalClose: { fontSize: FontSize.md, color: Colors.primary, fontWeight: FontWeight.semibold },
  modalTitle: { fontSize: FontSize.lg, fontWeight: FontWeight.bold, color: Colors.text },
  modalSearchBox: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: Colors.surface,
    borderRadius: Radius.full,
    paddingHorizontal: Spacing.lg,
    minHeight: 48,
    borderWidth: 1,
    borderColor: Colors.border,
    marginHorizontal: Layout.screenPaddingX,
    marginVertical: Spacing.md,
    gap: 8,
  },
  searchIcon: { fontSize: 16 },
  modalSearchInput: { flex: 1, fontSize: FontSize.md, color: Colors.text, paddingVertical: 10 },
  clearIcon: { color: Colors.textMuted, fontSize: 16, paddingHorizontal: 4 },
  modalList: { paddingHorizontal: Layout.screenPaddingX, paddingBottom: Spacing.xl },
  modalCityRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    backgroundColor: Colors.surface,
    borderRadius: Radius.md,
    padding: Spacing.md,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  modalCityRowActive: { borderColor: Colors.primary, backgroundColor: Colors.primarySoft },
  modalCityRowPressed: { opacity: 0.85 },
  modalCityName: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, color: Colors.text },
  modalCityNameActive: { color: Colors.primary },
  modalCityCount: { fontSize: FontSize.xs, color: Colors.textMuted, fontWeight: FontWeight.semibold },
  modalCityCountActive: { color: Colors.primary },
  modalCitySoon: { fontSize: FontSize.xs, color: Colors.textMuted, fontStyle: 'italic' },
});
