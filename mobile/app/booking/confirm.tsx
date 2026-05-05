import { useState } from 'react';
import { View, Text, StyleSheet, ScrollView, Alert, Pressable } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Card } from '@/components/Card';
import { Badge } from '@/components/Badge';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { Colors, FontSize, FontWeight, Radius, Spacing, Layout } from '@/lib/theme';
import { BookingApi } from '@/api/booking';
import { extractApiError } from '@/api/client';
import { formatDateTime, formatDuration, formatTry } from '@/lib/format';

export default function ConfirmBookingScreen() {
  const params = useLocalSearchParams<{
    stylistId: string;
    stylistName?: string;
    scheduledAt: string;
    services: string;
    serviceLabels?: string;
    totalAmount?: string;
    totalDuration?: string;
    shopName?: string;
  }>();
  const router = useRouter();
  const [loading, setLoading] = useState(false);
  const [note, setNote] = useState('');

  const serviceIds = params.services?.split(',').filter(Boolean) ?? [];

  async function handleConfirm() {
    setLoading(true);
    try {
      await BookingApi.initiate(
        params.stylistId!,
        params.scheduledAt!,
        serviceIds,
        note || undefined,
        false,
      );
      Alert.alert(
        '✅ Randevu oluşturuldu',
        'Randevun "Beklemede" durumunda kaydedildi. Kuaför onayladığında bildirim alacaksın.',
        [{ text: 'Randevularıma git', onPress: () => router.replace('/(tabs)/bookings') }],
      );
    } catch (err) {
      Alert.alert('Hata', extractApiError(err));
    } finally {
      setLoading(false);
    }
  }

  return (
    <SafeAreaView style={styles.container} edges={['top', 'bottom']}>
      <ScrollView
        contentContainerStyle={styles.scroll}
        showsVerticalScrollIndicator={false}
      >
        <Pressable onPress={() => router.back()} style={styles.backBtn}>
          <Text style={styles.back}>← Geri</Text>
        </Pressable>

        <Text style={styles.heading}>Randevuyu onayla</Text>
        <Text style={styles.helper}>Bilgileri kontrol et ve oluştur.</Text>

        <Card style={{ gap: 0 }} padded={false}>
          {params.shopName ? (
            <>
              <DetailRow icon="🏪" label="Salon" value={params.shopName} />
              <Divider />
            </>
          ) : null}
          <DetailRow icon="📅" label="Tarih & Saat" value={formatDateTime(params.scheduledAt!)} />
          <Divider />
          <DetailRow
            icon="✂️"
            label={serviceIds.length > 1 ? `${serviceIds.length} hizmet` : 'Hizmet'}
            value={params.serviceLabels || `${serviceIds.length} hizmet`}
          />
          <Divider />
          <DetailRow
            icon="👤"
            label="Stilist"
            value={params.stylistName || `#${params.stylistId!.slice(0, 8)}`}
          />
          {params.totalAmount && params.totalDuration ? (
            <>
              <Divider />
              <DetailRow
                icon="💰"
                label="Toplam"
                value={`${formatTry(parseInt(params.totalAmount, 10))} · ${formatDuration(parseInt(params.totalDuration, 10))}`}
              />
            </>
          ) : null}
        </Card>

        <View style={styles.section}>
          <Input
            label="Notun (opsiyonel)"
            value={note}
            onChangeText={setNote}
            placeholder="Özel istek, alerjik durum vs."
            multiline
            numberOfLines={3}
            maxLength={500}
            hint={`${note.length}/500`}
          />
        </View>

        <Card style={{ gap: 8 }}>
          <Text style={styles.policyTitle}>📋 İptal politikası</Text>
          <View style={styles.policyRow}>
            <Badge label="24+ saat" tone="success" />
            <Text style={styles.policyText}>%100 iade</Text>
          </View>
          <View style={styles.policyRow}>
            <Badge label="12-24 saat" tone="warning" />
            <Text style={styles.policyText}>%50 iade</Text>
          </View>
          <View style={styles.policyRow}>
            <Badge label="0-12 saat" tone="error" />
            <Text style={styles.policyText}>İade yok</Text>
          </View>
        </Card>

        <Card>
          <Text style={styles.policyTitle}>💳 Ödeme</Text>
          <Text style={styles.policySub}>
            Ödeme adımı geliştirme aşamasında — şimdilik randevu "Beklemede" durumunda oluşur.
            Onay sonrası kuaförden ödeme bilgisi alacaksın.
          </Text>
        </Card>

        <Button
          label="Randevuyu oluştur"
          onPress={handleConfirm}
          loading={loading}
          size="lg"
        />

        <Text style={styles.kvkk}>
          Devam ederek randevuyla ilgili kişisel verilerinin işlenmesine ve KVKK aydınlatma metnine onay
          vermiş olursun.
        </Text>
      </ScrollView>
    </SafeAreaView>
  );
}

function DetailRow({ icon, label, value }: { icon: string; label: string; value: string }) {
  return (
    <View style={styles.detailRow}>
      <Text style={styles.detailIcon}>{icon}</Text>
      <View style={{ flex: 1 }}>
        <Text style={styles.detailLabel}>{label}</Text>
        <Text style={styles.detailValue}>{value}</Text>
      </View>
    </View>
  );
}

function Divider() { return <View style={styles.divider} />; }

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  scroll: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingBottom: Spacing.xxl,
    gap: Spacing.lg,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  backBtn: { paddingVertical: 6, alignSelf: 'flex-start' },
  back: { color: Colors.primary, fontSize: FontSize.sm, fontWeight: FontWeight.medium },
  heading: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text },
  helper: { fontSize: FontSize.sm, color: Colors.textSecondary },
  detailRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.md,
    paddingVertical: Spacing.md,
    paddingHorizontal: Spacing.lg,
  },
  detailIcon: { fontSize: 22, width: 28, textAlign: 'center' },
  detailLabel: { fontSize: FontSize.xs, color: Colors.textMuted, fontWeight: FontWeight.medium },
  detailValue: { fontSize: FontSize.md, color: Colors.text, fontWeight: FontWeight.semibold, marginTop: 2 },
  divider: { height: 1, backgroundColor: Colors.divider, marginLeft: 64 },
  section: {},
  policyTitle: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, color: Colors.text, marginBottom: 4 },
  policySub: { fontSize: FontSize.sm, color: Colors.textSecondary, lineHeight: 20 },
  policyRow: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  policyText: { fontSize: FontSize.sm, color: Colors.text },
  kvkk: { fontSize: FontSize.xs, color: Colors.textMuted, textAlign: 'center', lineHeight: 16, marginTop: Spacing.sm },
});
