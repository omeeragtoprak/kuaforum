import { View, Text, StyleSheet } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams } from 'expo-router';
import { Colors, FontSize, Spacing } from '@/lib/theme';
import { EmptyState } from '@/components/EmptyState';

/**
 * Search screen — gerçek barber search endpoint M9 Search modülünden gelecek.
 * Şu an placeholder; M9 Meilisearch integration sonrası bu ekran gerçek listeyi gösterecek.
 */
export default function SearchScreen() {
  const params = useLocalSearchParams<{ cityId?: string; cityName?: string }>();

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <View style={styles.header}>
        <Text style={styles.title}>{params.cityName ?? 'Ara'}</Text>
        <Text style={styles.subtitle}>Yakındaki kuaförler</Text>
      </View>
      <EmptyState
        title="Search modülü hazır değil"
        description="M9 Search (Meilisearch) implement edilince burada filtre + harita + liste görüntülenecek."
      />
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: { padding: Spacing.lg },
  title: { fontSize: FontSize.xxl, fontWeight: '700', color: Colors.text },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: Spacing.xs },
});
