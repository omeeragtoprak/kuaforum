import { useMemo, useState } from 'react';
import { View, Text, StyleSheet, FlatList, Pressable, RefreshControl, TextInput } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { Card } from '@/components/Card';
import { Badge } from '@/components/Badge';
import { EmptyState } from '@/components/EmptyState';
import { Colors, FontSize, FontWeight, Radius, Spacing, Layout } from '@/lib/theme';
import { GeographyApi, City } from '@/api/geography';
import { useAuthStore } from '@/store/auth-store';
import { normalizeTr } from '@/lib/format';

export default function DiscoverScreen() {
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const [query, setQuery] = useState('');

  const citiesQuery = useQuery({
    queryKey: ['cities'],
    queryFn: () => GeographyApi.getCities(),
  });

  const filtered = useMemo(() => {
    const list = citiesQuery.data ?? [];
    if (!query.trim()) return list;
    const q = normalizeTr(query.trim());
    return list.filter((c) => normalizeTr(c.name).includes(q));
  }, [citiesQuery.data, query]);

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <FlatList
        data={filtered}
        keyExtractor={(c) => c.id}
        contentContainerStyle={styles.list}
        keyboardShouldPersistTaps="handled"
        showsVerticalScrollIndicator={false}
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

            <View style={styles.searchBox}>
              <Text style={styles.searchIcon}>🔍</Text>
              <TextInput
                value={query}
                onChangeText={setQuery}
                placeholder="Şehir ara…"
                placeholderTextColor={Colors.textMuted}
                style={styles.searchInput}
              />
              {query ? (
                <Pressable onPress={() => setQuery('')}>
                  <Text style={styles.clearIcon}>✕</Text>
                </Pressable>
              ) : null}
            </View>

            <Text style={styles.sectionLabel}>
              {citiesQuery.isLoading ? 'Yükleniyor…' : `${filtered.length} şehir`}
            </Text>
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
        renderItem={({ item }) => (
          <Pressable
            onPress={() =>
              router.push({ pathname: '/(tabs)/search', params: { cityId: item.id, cityName: item.name } })
            }
            style={({ pressed }) => [styles.cityRow, pressed && styles.pressed]}
          >
            <View style={styles.cityIconWrap}>
              <Text style={styles.cityIcon}>🏙️</Text>
            </View>
            <View style={styles.cityBody}>
              <Text style={styles.cityName}>{item.name}</Text>
              <Text style={styles.cityHint}>Plaka {item.plateCode.toString().padStart(2, '0')}</Text>
            </View>
            <Text style={styles.chevron}>›</Text>
          </Pressable>
        )}
        ItemSeparatorComponent={() => <View style={styles.separator} />}
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
  hero: { paddingTop: Spacing.lg, paddingBottom: Spacing.lg },
  greeting: { fontSize: FontSize.sm, color: Colors.textSecondary, marginBottom: 6 },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 4 },
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
    color: Colors.textMuted,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    marginBottom: Spacing.sm,
    fontWeight: FontWeight.semibold,
  },
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
  pressed: { opacity: 0.85, transform: [{ scale: 0.99 }] },
  cityIconWrap: {
    width: 44,
    height: 44,
    borderRadius: 22,
    backgroundColor: Colors.primarySoft,
    alignItems: 'center',
    justifyContent: 'center',
  },
  cityIcon: { fontSize: 22 },
  cityBody: { flex: 1 },
  cityName: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, color: Colors.text },
  cityHint: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 2 },
  chevron: { fontSize: 24, color: Colors.textMuted },
  separator: { height: Spacing.sm },
});
