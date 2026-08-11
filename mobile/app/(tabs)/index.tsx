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
import { Icon } from '@/components/Icon';
import { EmptyState } from '@/components/EmptyState';
import { ErrorState } from '@/components/ErrorState';
import { SkeletonShopCard, SkeletonList } from '@/components/Skeleton';
import { Colors, FontSize, FontWeight, LetterSpacing, Radius, Spacing, Layout, monogramFor } from '@/lib/theme';
import { GeographyApi, City } from '@/api/geography';
import { BarberApi, BarberSearchResult } from '@/api/barber';
import { useAuthStore } from '@/store/auth-store';
import { useDiscoveryStore, DEFAULT_CITY_PLATE, DEFAULT_CITY_NAME } from '@/store/discovery-store';
import { normalizeTr, formatTry, salonGenderLabel, salonGenderTone } from '@/lib/format';

type GenderFilter = 'all' | 'unisex' | 'female' | 'male' | 'walkin';

/**
 * Keşfet — varsayılan olarak kullanıcının (demo: Erzurum) şehrindeki kuaförleri
 * direkt gösterir. Bölge satırından farklı şehir seçilebilir.
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
            tintColor={Colors.ink}
          />
        }
        ListHeaderComponent={
          <View>
            <View style={styles.hero}>
              {user?.displayName ? (
                <Text style={styles.greeting}>Merhaba, {user.displayName.split(' ')[0]}</Text>
              ) : null}
              <Text style={styles.title}>Salonunu bul</Text>
            </View>

            {/* Bölge satırı — aktif şehir, tıklanır, "değiştir" tek vurgulu aksiyon */}
            <Pressable
              onPress={() => setCityModalOpen(true)}
              style={({ pressed }) => [styles.regionRow, pressed && styles.regionRowPressed]}
              accessibilityRole="button"
              accessibilityLabel={`Aktif şehir: ${cityName ?? DEFAULT_CITY_NAME}. Değiştirmek için bas.`}
            >
              <Icon name="mapPin" size={18} color={Colors.textSecondary} />
              <Text style={styles.regionName}>{cityName ?? DEFAULT_CITY_NAME}</Text>
              <Text style={styles.regionCount}>
                {shopsQuery.isLoading ? '…' : `${totalShops} kuaför`}
              </Text>
              <View style={{ flex: 1 }} />
              <Text style={styles.regionChange}>Değiştir</Text>
              <Icon name="chevronRight" size={16} color={Colors.accent} strokeWidth={2} />
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
            <ErrorState
              error={citiesQuery.error}
              onRetry={() => citiesQuery.refetch()}
              notFoundTitle="Şehir listesi bulunamadı"
              notFoundDescription="Bu kaynak kaldırılmış görünüyor."
              notFoundActionLabel="Tekrar dene"
              onNotFoundAction={() => citiesQuery.refetch()}
              networkTitle="Şehirler yüklenemedi"
              networkDescription="Sunucuya ulaşılamadı. İnternet bağlantını kontrol edip tekrar dene."
            />
          ) : citiesQuery.isLoading || (!cityId && !citiesQuery.isError) || shopsQuery.isLoading ? (
            <SkeletonList count={4} render={() => <SkeletonShopCard />} />
          ) : shopsQuery.isError ? (
            <ErrorState
              error={shopsQuery.error}
              onRetry={() => shopsQuery.refetch()}
              notFoundTitle="Bu bölge artık desteklenmiyor"
              notFoundDescription="Farklı bir şehir dene."
              notFoundActionLabel="Şehir değiştir"
              onNotFoundAction={() => setCityModalOpen(true)}
              networkTitle="Bağlantı hatası"
              networkDescription="Sunucuya ulaşılamıyor."
            />
          ) : (
            <EmptyState
              icon="scissors"
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
  const mono = monogramFor(barber.name);
  return (
    <Card onPress={onPress} style={{ gap: Spacing.sm }}>
      <View style={styles.cardHeader}>
        <View style={[styles.monogram, { backgroundColor: mono.bg }]}>
          <Text style={[styles.monogramText, { color: mono.fg }]}>{mono.initials}</Text>
        </View>
        <View style={{ flex: 1 }}>
          <Text style={styles.shopName} numberOfLines={1}>{barber.name}</Text>
          <View style={styles.ratingRow}>
            {hasRating ? (
              <>
                <Text style={styles.ratingStars}>★</Text>
                <Text style={styles.ratingValue}>{barber.ratingAverage!.toFixed(1)}</Text>
                <Text style={styles.ratingCount}>({barber.ratingCount})</Text>
              </>
            ) : (
              <Badge label="Yeni" tone="brand" />
            )}
            {barber.addressLine1 ? (
              <>
                <Text style={styles.ratingDot}>·</Text>
                <Icon name="mapPin" size={11} color={Colors.textMuted} />
                <Text style={styles.address} numberOfLines={1}>{barber.addressLine1}</Text>
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
          <Icon name="users" size={13} color={Colors.textSecondary} />
          <Text style={styles.stat}>{barber.stylistCount}</Text>
          <Text style={styles.statDivider}>·</Text>
          <Icon name="scissors" size={13} color={Colors.textSecondary} />
          <Text style={styles.stat}>{barber.serviceCount}</Text>
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
          <Icon name="search" size={17} color={Colors.textMuted} />
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
              <Icon name="x" size={16} color={Colors.textMuted} />
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
  greeting: { fontSize: FontSize.sm, color: Colors.textSecondary, marginBottom: 4 },
  title: {
    fontSize: FontSize.display,
    fontWeight: FontWeight.bold,
    color: Colors.text,
    letterSpacing: LetterSpacing.display,
  },

  regionRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    backgroundColor: Colors.surfaceAlt,
    borderRadius: Radius.lg,
    paddingHorizontal: Spacing.lg,
    paddingVertical: Spacing.md,
    marginTop: Spacing.lg,
    marginBottom: Spacing.md,
    minHeight: Layout.tapTargetMin,
  },
  regionRowPressed: { backgroundColor: Colors.border },
  regionName: { fontSize: FontSize.md, color: Colors.text, fontWeight: FontWeight.semibold },
  regionCount: { fontSize: FontSize.xs, color: Colors.textMuted },
  regionChange: { fontSize: FontSize.sm, color: Colors.accent, fontWeight: FontWeight.semibold },

  filtersRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 8,
    marginBottom: Spacing.md,
  },

  cardHeader: { flexDirection: 'row', alignItems: 'flex-start', gap: Spacing.sm },
  monogram: {
    width: 44, height: 44, borderRadius: Radius.full,
    alignItems: 'center', justifyContent: 'center',
  },
  monogramText: { fontSize: FontSize.sm, fontWeight: FontWeight.bold },
  shopName: { fontSize: FontSize.lg, fontWeight: FontWeight.semibold, color: Colors.text },
  ratingRow: { flexDirection: 'row', alignItems: 'center', gap: 4, marginTop: 4, flexWrap: 'wrap' },
  ratingStars: { fontSize: 13, color: Colors.star },
  ratingValue: { fontSize: FontSize.xs, color: Colors.text, fontWeight: FontWeight.bold },
  ratingCount: { fontSize: FontSize.xs, color: Colors.textMuted },
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
  price: { fontSize: FontSize.sm, color: Colors.ink, fontWeight: FontWeight.bold },
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
  modalClose: { fontSize: FontSize.md, color: Colors.ink, fontWeight: FontWeight.semibold },
  modalTitle: { fontSize: FontSize.lg, fontWeight: FontWeight.bold, color: Colors.text },
  modalSearchBox: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: Colors.surfaceAlt,
    borderRadius: Radius.full,
    paddingHorizontal: Spacing.lg,
    minHeight: 48,
    marginHorizontal: Layout.screenPaddingX,
    marginVertical: Spacing.md,
    gap: 8,
  },
  modalSearchInput: { flex: 1, fontSize: FontSize.md, color: Colors.text, paddingVertical: 10 },
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
  modalCityRowActive: { borderColor: Colors.accent, backgroundColor: Colors.accentSoft },
  modalCityRowPressed: { opacity: 0.85 },
  modalCityName: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, color: Colors.text },
  modalCityNameActive: { color: Colors.accentInk },
  modalCityCount: { fontSize: FontSize.xs, color: Colors.textMuted, fontWeight: FontWeight.semibold },
  modalCityCountActive: { color: Colors.accentInk },
  modalCitySoon: { fontSize: FontSize.xs, color: Colors.textMuted, fontStyle: 'italic' },
});
