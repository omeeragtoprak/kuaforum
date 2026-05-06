import { useMemo, useState } from 'react';
import { View, Text, StyleSheet, SectionList, Pressable, RefreshControl, TextInput } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { EmptyState } from '@/components/EmptyState';
import { Colors, FontSize, FontWeight, Radius, Spacing, Layout, Shadow } from '@/lib/theme';
import { GeographyApi, City } from '@/api/geography';
import { useAuthStore } from '@/store/auth-store';
import { normalizeTr } from '@/lib/format';

type CitySection = { title: string; key: 'with' | 'without'; data: City[] };

export default function DiscoverScreen() {
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const [query, setQuery] = useState('');

  const citiesQuery = useQuery({
    queryKey: ['cities'],
    queryFn: () => GeographyApi.getCities(),
  });

  const { sections, totalShops, totalActiveCities } = useMemo(() => {
    const list = citiesQuery.data ?? [];
    const q = query.trim() ? normalizeTr(query.trim()) : null;
    const filtered = q ? list.filter((c) => normalizeTr(c.name).includes(q)) : list;

    const withShops = filtered.filter((c) => c.activeShopCount > 0)
      .sort((a, b) => b.activeShopCount - a.activeShopCount || a.name.localeCompare(b.name, 'tr'));
    const withoutShops = filtered.filter((c) => c.activeShopCount === 0)
      .sort((a, b) => a.name.localeCompare(b.name, 'tr'));

    const out: CitySection[] = [];
    if (withShops.length > 0) out.push({ title: 'Hizmet veren şehirler', key: 'with', data: withShops });
    if (withoutShops.length > 0) out.push({ title: 'Yakında açılacak', key: 'without', data: withoutShops });

    return {
      sections: out,
      totalShops: list.reduce((s, c) => s + c.activeShopCount, 0),
      totalActiveCities: list.filter((c) => c.activeShopCount > 0).length,
    };
  }, [citiesQuery.data, query]);

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <SectionList
        sections={sections}
        keyExtractor={(c) => c.id}
        contentContainerStyle={styles.list}
        keyboardShouldPersistTaps="handled"
        showsVerticalScrollIndicator={false}
        stickySectionHeadersEnabled={false}
        refreshControl={
          <RefreshControl
            refreshing={citiesQuery.isFetching && !citiesQuery.isLoading}
            onRefresh={() => citiesQuery.refetch()}
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
              <Text style={styles.subtitle}>Yakındaki kuaförlerden hızlıca randevu al.</Text>
            </View>

            {!citiesQuery.isLoading && totalShops > 0 ? (
              <View style={styles.statsBanner} accessible accessibilityLabel={`${totalShops} aktif kuaför, ${totalActiveCities} şehirde`}>
                <View style={styles.statBlock}>
                  <Text style={styles.statValue}>{totalShops}</Text>
                  <Text style={styles.statLabel}>aktif kuaför</Text>
                </View>
                <View style={styles.statDivider} />
                <View style={styles.statBlock}>
                  <Text style={styles.statValue}>{totalActiveCities}</Text>
                  <Text style={styles.statLabel}>şehirde hizmet</Text>
                </View>
              </View>
            ) : null}

            <View style={styles.searchBox}>
              <Text style={styles.searchIcon}>🔍</Text>
              <TextInput
                value={query}
                onChangeText={setQuery}
                placeholder="Şehir ara…"
                placeholderTextColor={Colors.textMuted}
                style={styles.searchInput}
                accessibilityLabel="Şehir ara"
                returnKeyType="search"
              />
              {query ? (
                <Pressable onPress={() => setQuery('')} hitSlop={8} accessibilityLabel="Aramayı temizle">
                  <Text style={styles.clearIcon}>✕</Text>
                </Pressable>
              ) : null}
            </View>
          </View>
        }
        ListEmptyComponent={
          citiesQuery.isLoading ? null : citiesQuery.isError ? (
            <EmptyState
              emoji="⚠️"
              title="Bağlantı hatası"
              description="Sunucuya ulaşılamıyor. Bağlantını kontrol edip tekrar dene."
              actionLabel="Tekrar dene"
              onAction={() => citiesQuery.refetch()}
            />
          ) : (
            <EmptyState
              emoji="🔍"
              title="Şehir bulunamadı"
              description={query ? `"${query}" araması için sonuç yok.` : 'Henüz şehir verisi yok.'}
            />
          )
        }
        renderSectionHeader={({ section }) => (
          <Text style={[styles.sectionLabel, section.key === 'without' && styles.sectionLabelMuted]}>
            {section.title} · {section.data.length}
          </Text>
        )}
        renderItem={({ item, section }) => {
          const hasShops = item.activeShopCount > 0;
          return (
            <Pressable
              onPress={() => {
                if (hasShops) {
                  router.push({ pathname: '/(tabs)/search', params: { cityId: item.id, cityName: item.name } });
                }
              }}
              disabled={!hasShops}
              accessibilityRole="button"
              accessibilityLabel={
                hasShops
                  ? `${item.name}, ${item.activeShopCount} aktif kuaför`
                  : `${item.name}, henüz kuaför yok`
              }
              accessibilityState={{ disabled: !hasShops }}
              style={({ pressed }) => [
                styles.cityRow,
                !hasShops && styles.cityRowMuted,
                pressed && hasShops && styles.pressed,
              ]}
            >
              <View style={[styles.cityIconWrap, !hasShops && styles.cityIconWrapMuted]}>
                <Text style={styles.cityIcon}>🏙️</Text>
              </View>
              <View style={styles.cityBody}>
                <Text style={[styles.cityName, !hasShops && styles.cityNameMuted]}>{item.name}</Text>
                <Text style={styles.cityHint}>Plaka {item.plateCode.toString().padStart(2, '0')}</Text>
              </View>
              {hasShops ? (
                <View style={styles.countBadge}>
                  <Text style={styles.countBadgeText}>
                    {item.activeShopCount} kuaför
                  </Text>
                </View>
              ) : (
                <Text style={styles.soonText}>yakında</Text>
              )}
              {hasShops ? <Text style={styles.chevron}>›</Text> : null}
            </Pressable>
          );
        }}
        ItemSeparatorComponent={() => <View style={styles.separator} />}
        SectionSeparatorComponent={() => <View style={{ height: Spacing.lg }} />}
      />
    </SafeAreaView>
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
  hero: { paddingTop: Spacing.lg, paddingBottom: Spacing.md },
  greeting: { fontSize: FontSize.sm, color: Colors.textSecondary, marginBottom: 6 },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 4 },

  statsBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: Colors.primarySoft,
    borderRadius: Radius.lg,
    paddingVertical: Spacing.md,
    paddingHorizontal: Spacing.lg,
    marginBottom: Spacing.md,
    ...Shadow.sm,
  },
  statBlock: { flex: 1, alignItems: 'center' },
  statDivider: { width: 1, alignSelf: 'stretch', backgroundColor: Colors.border, marginHorizontal: Spacing.sm },
  statValue: { fontSize: FontSize.xl, fontWeight: FontWeight.bold, color: Colors.primary },
  statLabel: { fontSize: FontSize.xs, color: Colors.primary, opacity: 0.85, marginTop: 2 },

  searchBox: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: Colors.surface,
    borderRadius: Radius.full,
    paddingHorizontal: Spacing.lg,
    minHeight: 48,
    borderWidth: 1,
    borderColor: Colors.border,
    marginBottom: Spacing.md,
    gap: 8,
  },
  searchIcon: { fontSize: 16 },
  searchInput: { flex: 1, fontSize: FontSize.md, color: Colors.text, paddingVertical: 10 },
  clearIcon: { color: Colors.textMuted, fontSize: 16, paddingHorizontal: 4 },

  sectionLabel: {
    fontSize: FontSize.xs,
    color: Colors.textSecondary,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    marginBottom: Spacing.sm,
    fontWeight: FontWeight.bold,
  },
  sectionLabelMuted: { color: Colors.textMuted },

  cityRow: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: Colors.surface,
    borderRadius: Radius.lg,
    padding: Spacing.md,
    gap: Spacing.md,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  cityRowMuted: { opacity: 0.6, backgroundColor: Colors.background },
  pressed: { opacity: 0.85, transform: [{ scale: 0.99 }] },

  cityIconWrap: {
    width: 44,
    height: 44,
    borderRadius: 22,
    backgroundColor: Colors.primarySoft,
    alignItems: 'center',
    justifyContent: 'center',
  },
  cityIconWrapMuted: { backgroundColor: Colors.divider },
  cityIcon: { fontSize: 22 },

  cityBody: { flex: 1 },
  cityName: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, color: Colors.text },
  cityNameMuted: { color: Colors.textSecondary },
  cityHint: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 2 },

  countBadge: {
    backgroundColor: Colors.primary,
    borderRadius: Radius.full,
    paddingHorizontal: Spacing.sm + 2,
    paddingVertical: 4,
  },
  countBadgeText: {
    fontSize: FontSize.xs,
    fontWeight: FontWeight.bold,
    color: Colors.textInverse,
  },
  soonText: { fontSize: FontSize.xs, color: Colors.textMuted, fontStyle: 'italic' },

  chevron: { fontSize: 24, color: Colors.textMuted, marginLeft: 4 },
  separator: { height: Spacing.sm },
});
