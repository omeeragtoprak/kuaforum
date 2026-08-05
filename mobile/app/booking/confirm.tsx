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
import { PaymentApi, PaymentKind, InitiatePaymentResponse } from '@/api/payment';
import { extractApiError } from '@/api/client';
import { formatDateTime, formatDuration, formatTry } from '@/lib/format';
import { estimateDepositMinor, remainingAtShopMinor } from '@/lib/deposit';
import { Haptics } from '@/lib/haptics';
import { ErrorReporter } from '@/lib/error-reporter';
import { useAuthStore } from '@/store/auth-store';
import { resolveBookingConfirmMode } from '@/lib/booking-confirm';

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
  const [payKind, setPayKind] = useState<PaymentKind>('Deposit');

  const onlinePaymentsEnabled = useAuthStore((s) => s.user?.onlinePaymentsEnabled);
  const confirmMode = resolveBookingConfirmMode(onlinePaymentsEnabled);

  const serviceIds = params.services?.split(',').filter(Boolean) ?? [];
  const total = params.totalAmount ? parseInt(params.totalAmount, 10) : undefined;

  async function handleConfirm() {
    setLoading(true);
    try {
      const booking = await BookingApi.initiate(
        params.stylistId!,
        params.scheduledAt!,
        serviceIds,
        note || undefined,
        false,
      );

      // RFC-009 ücretsiz büyüme modu: flag kapalıyken ödeme adımı hiç başlatılmaz — randevu
      // Pending kalır, kuaför OwnerConfirm ile onaylar. PaymentApi.initiate çağrılmadığı için
      // "ödeme başlatılamadı" hata dalı bu senaryoda hiç devreye girmez.
      if (confirmMode === 'salon-payment') {
        Haptics.success();
        Alert.alert(
          '✅ Randevu talebin alındı',
          'Kuaför onaylayınca bildirim alacaksın.',
          [{ text: 'Tamam', onPress: () => router.replace('/(tabs)/bookings') }],
        );
        return;
      }

      let pay: InitiatePaymentResponse;
      try {
        pay = await PaymentApi.initiate(booking.bookingId, payKind);
      } catch (payErr) {
        ErrorReporter.capture(payErr, { tag: 'booking.confirm.payment-initiate', extra: { bookingId: booking.bookingId, payKind } });
        Haptics.warning();
        Alert.alert(
          'Randevun oluştu',
          'Randevun oluştu ama ödeme başlatılamadı. Randevularım\'dan ödemeyi tamamlayabilirsin.',
          [{ text: 'Tamam', onPress: () => router.replace('/(tabs)/bookings') }],
        );
        return;
      }
      Haptics.success();
      router.replace({
        pathname: '/booking/payment',
        params: {
          paymentId: pay.paymentId,
          checkoutUrl: pay.checkoutUrl,
          amountMinor: String(pay.amountMinor),
          remainingAtShopMinor: String(pay.remainingAtShopMinor),
          kind: pay.kind,
          bookingId: booking.bookingId,
          expiresAt: pay.expiresAt,
        },
      });
    } catch (err) {
      Haptics.error();
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

        {confirmMode === 'online-payment' ? (
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
        ) : null}

        {confirmMode === 'online-payment' ? (
          <Card style={{ gap: Spacing.sm }}>
            <Text style={styles.policyTitle}>💳 Ödeme</Text>
            <View accessibilityRole="radiogroup" style={{ gap: Spacing.sm }}>
              <PayOption
                selected={payKind === 'Deposit'}
                onPress={() => setPayKind('Deposit')}
                title="Kapora ile ayırt (önerilen)"
                detail={
                  total
                    ? `Şimdi ${formatTry(estimateDepositMinor(total))} · Salonda ${formatTry(remainingAtShopMinor(total, estimateDepositMinor(total)))}`
                    : 'Hizmet bedelinin %15’i şimdi, kalanı salonda'
                }
              />
              <PayOption
                selected={payKind === 'FullOnline'}
                onPress={() => setPayKind('FullOnline')}
                title="Tamamını şimdi öde"
                detail={total ? `${formatTry(total)} — salonda ödeme kalmaz` : 'Tamamı online'}
              />
            </View>
            <Text style={styles.policySub}>
              Kesin tutar ödeme adımında gösterilir. 30 dk içinde ödenmeyen randevu otomatik iptal olur.
            </Text>
          </Card>
        ) : (
          <Card style={{ gap: 4 }}>
            <View accessible accessibilityLabel="Randevu kuaför onayına gönderilecek, ödeme salonda yapılır">
              <Text style={styles.policyTitle}>💈 Randevu kuaför onayına gönderilecek</Text>
              <Text style={styles.policySub}>
                Ödeme salonda yapılır. Kuaför onaylayınca bildirim alacaksın.
              </Text>
            </View>
          </Card>
        )}

        <Button
          label="Randevuyu oluştur"
          onPress={handleConfirm}
          loading={loading}
          size="lg"
        />

        <Text style={styles.kvkk}>
          Kişisel verilerinin işlenmesi hakkında:{' '}
          <Text
            style={styles.kvkkLink}
            onPress={() => router.push('/legal/aydinlatma' as never)}
            accessibilityRole="link"
            accessibilityLabel="Aydınlatma Metni"
          >
            Aydınlatma Metni
          </Text>
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

function PayOption({
  selected,
  onPress,
  title,
  detail,
}: {
  selected: boolean;
  onPress: () => void;
  title: string;
  detail: string;
}) {
  return (
    <Pressable
      onPress={onPress}
      accessible
      accessibilityRole="radio"
      accessibilityState={{ checked: selected, selected }}
      accessibilityLabel={`${title}. ${detail}`}
      style={({ pressed }) => [
        styles.payOption,
        selected && styles.payOptionSelected,
        pressed && styles.pressed,
      ]}
    >
      <Text style={[styles.payRadio, selected && styles.payRadioSelected]}>
        {selected ? '●' : '○'}
      </Text>
      <View style={{ flex: 1 }}>
        <Text style={styles.payTitle}>{title}</Text>
        <Text style={styles.payDetail}>{detail}</Text>
      </View>
    </Pressable>
  );
}

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
  kvkkLink: { color: Colors.primary, textDecorationLine: 'underline' },
  payOption: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.md,
    minHeight: Layout.tapTargetMin,
    paddingVertical: Spacing.sm,
    paddingHorizontal: Spacing.md,
    borderRadius: Radius.md,
    borderWidth: 1,
    borderColor: Colors.border,
    backgroundColor: Colors.surface,
  },
  payOptionSelected: {
    borderColor: Colors.primary,
    backgroundColor: Colors.primarySoft,
  },
  pressed: { opacity: 0.85 },
  payRadio: { fontSize: 18, color: Colors.borderStrong, width: 20, textAlign: 'center' },
  payRadioSelected: { color: Colors.primary },
  payTitle: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, color: Colors.text },
  payDetail: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 2 },
});
