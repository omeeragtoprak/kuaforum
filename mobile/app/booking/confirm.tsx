import { useState } from 'react';
import { View, Text, StyleSheet, ScrollView, Alert, Pressable } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { Colors, FontSize, Spacing } from '@/lib/theme';
import { BookingApi } from '@/api/booking';
import { extractApiError } from '@/api/client';
import { formatDateTime } from '@/lib/format';

export default function ConfirmBookingScreen() {
  const params = useLocalSearchParams<{
    stylistId: string;
    scheduledAt: string;
    services: string;
  }>();
  const router = useRouter();
  const [loading, setLoading] = useState(false);
  const [note, setNote] = useState('');

  const serviceIds = params.services?.split(',') ?? [];

  async function handleConfirm() {
    setLoading(true);
    try {
      const res = await BookingApi.initiate(
        params.stylistId!,
        params.scheduledAt!,
        serviceIds,
        note || undefined,
        false,
      );
      Alert.alert(
        'Randevu oluşturuldu',
        'Ödeme adımı M7 Payment ile entegre olunca yönlendirme yapılacak. Şu an Pending durumda kaydedildi.',
        [{ text: 'Tamam', onPress: () => router.replace('/(tabs)/bookings') }],
      );
    } catch (err) {
      Alert.alert('Hata', extractApiError(err));
    } finally {
      setLoading(false);
    }
  }

  return (
    <SafeAreaView style={styles.container} edges={['top', 'bottom']}>
      <ScrollView contentContainerStyle={styles.scroll}>
        <Pressable onPress={() => router.back()} style={styles.back}>
          <Text style={styles.backText}>← Geri</Text>
        </Pressable>

        <Text style={styles.heading}>Randevuyu onayla</Text>

        <Card style={styles.card}>
          <Row label="Tarih & Saat" value={formatDateTime(params.scheduledAt!)} />
          <Row label="Hizmet sayısı" value={serviceIds.length.toString()} />
          <Row label="Stilist" value={params.stylistId!.slice(0, 8) + '…'} />
        </Card>

        <Card style={styles.card}>
          <Text style={styles.policyTitle}>İptal politikası</Text>
          <Text style={styles.policyLine}>• 24 saat öncesine kadar: %100 iade</Text>
          <Text style={styles.policyLine}>• 12-24 saat öncesi: %50 iade</Text>
          <Text style={styles.policyLine}>• 0-12 saat öncesi: iade yok</Text>
        </Card>

        <Card style={styles.card}>
          <Text style={styles.policyTitle}>Ödeme</Text>
          <Text style={styles.policyLine}>
            🚧 M7 Payment modülü hazır olduğunda PayTR Pazaryeri 3DS ile ödeme alınacak. Şu an demo
            akışında randevu Pending durumunda oluşturuluyor.
          </Text>
        </Card>

        <Button label="Randevuyu oluştur" onPress={handleConfirm} loading={loading} />
      </ScrollView>
    </SafeAreaView>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.row}>
      <Text style={styles.rowLabel}>{label}</Text>
      <Text style={styles.rowValue}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  scroll: { padding: Spacing.lg, gap: Spacing.lg },
  back: { paddingVertical: Spacing.sm },
  backText: { color: Colors.primary, fontSize: FontSize.sm },
  heading: { fontSize: FontSize.xxl, fontWeight: '700', color: Colors.text },
  card: { gap: Spacing.sm },
  row: { flexDirection: 'row', justifyContent: 'space-between', paddingVertical: Spacing.sm },
  rowLabel: { fontSize: FontSize.sm, color: Colors.textSecondary },
  rowValue: { fontSize: FontSize.sm, color: Colors.text, fontWeight: '500' },
  policyTitle: { fontSize: FontSize.md, fontWeight: '600', color: Colors.text, marginBottom: Spacing.xs },
  policyLine: { fontSize: FontSize.sm, color: Colors.textSecondary, marginVertical: 2 },
});
