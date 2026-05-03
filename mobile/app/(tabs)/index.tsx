import { useState } from 'react';
import { View, Text, StyleSheet, FlatList, Pressable, RefreshControl } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { Card } from '@/components/Card';
import { EmptyState } from '@/components/EmptyState';
import { Colors, FontSize, Spacing } from '@/lib/theme';
import { GeographyApi, City } from '@/api/geography';

export default function DiscoverScreen() {
  const router = useRouter();
  const [selectedCity, setSelectedCity] = useState<City | null>(null);

  const citiesQuery = useQuery({
    queryKey: ['cities'],
    queryFn: () => GeographyApi.getCities(),
  });

  // NOTE: Real barber list endpoint (M9 Search) henüz yazılmadı.
  // Şu an sadece şehir seçimi gösterilir; barber list M9 sonrası bağlanır.

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <View style={styles.header}>
        <Text style={styles.title}>Keşfet</Text>
        <Text style={styles.subtitle}>Şehrini seç ve yakındaki kuaförleri gör</Text>
      </View>

      {citiesQuery.isLoading ? (
        <EmptyState title="Yükleniyor..." />
      ) : citiesQuery.isError ? (
        <EmptyState
          title="Bağlantı hatası"
          description="Backend'e ulaşılamıyor. dotnet run ile API'yi başlattın mı?"
        />
      ) : (citiesQuery.data?.length ?? 0) === 0 ? (
        <EmptyState
          title="Şehir verisi yok"
          description="NVI veri bootstrap'ı henüz çalıştırılmadı. Veri import script'ini çalıştırman gerekir."
        />
      ) : (
        <FlatList
          data={citiesQuery.data}
          keyExtractor={(c) => c.id}
          contentContainerStyle={styles.list}
          refreshControl={
            <RefreshControl
              refreshing={citiesQuery.isFetching}
              onRefresh={() => citiesQuery.refetch()}
            />
          }
          ListHeaderComponent={
            selectedCity ? (
              <View style={styles.selectedBar}>
                <Text style={styles.selectedText}>{selectedCity.name} seçili</Text>
                <Pressable onPress={() => setSelectedCity(null)}>
                  <Text style={styles.clearLink}>Temizle</Text>
                </Pressable>
              </View>
            ) : null
          }
          renderItem={({ item }) => (
            <Pressable
              onPress={() => {
                setSelectedCity(item);
                router.push({ pathname: '/(tabs)/search', params: { cityId: item.id, cityName: item.name } });
              }}
            >
              <Card style={styles.cityCard}>
                <Text style={styles.cityName}>{item.name}</Text>
                <Text style={styles.cityCode}>{item.plateCode.toString().padStart(2, '0')}</Text>
              </Card>
            </Pressable>
          )}
        />
      )}
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: { padding: Spacing.lg, paddingBottom: Spacing.md },
  title: { fontSize: FontSize.xxl, fontWeight: '700', color: Colors.text },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: Spacing.xs },
  list: { padding: Spacing.lg, paddingTop: 0, gap: Spacing.sm },
  selectedBar: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingVertical: Spacing.sm,
    marginBottom: Spacing.sm,
  },
  selectedText: { fontSize: FontSize.sm, color: Colors.text, fontWeight: '600' },
  clearLink: { color: Colors.primary, fontSize: FontSize.sm },
  cityCard: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  cityName: { fontSize: FontSize.md, color: Colors.text, fontWeight: '500' },
  cityCode: { fontSize: FontSize.xs, color: Colors.textMuted },
});
