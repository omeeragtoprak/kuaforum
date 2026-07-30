import { useEffect, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, Pressable, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Card } from '@/components/Card';
import { Colors, FontSize, FontWeight, Spacing, Layout } from '@/lib/theme';
import { BookingApi } from '@/api/booking';
import { apiClient, extractApiError } from '@/api/client';
import type { PaymentKind } from '@/api/payment';
import { formatTry, isBookingConfirmed } from '@/lib/format';
import { Haptics } from '@/lib/haptics';
import { ErrorReporter } from '@/lib/error-reporter';
import { PaymentCountdown } from '@/components/payment/PaymentCountdown';
import { MockCheckoutCard } from '@/components/payment/MockCheckoutCard';
import { CheckoutWebView } from '@/components/payment/CheckoutWebView';
import { PaymentResultCard } from '@/components/payment/PaymentResultCard';

type Phase = 'checkout' | 'verifying' | 'success' | 'cancelled' | 'pendingWebhook' | 'error';

const VERIFY_MAX_ATTEMPTS = 8;
const VERIFY_INTERVAL_MS = 2000;

// MockPaymentProvider bugün sabit `http://localhost:5000` üretiyor (dev backend portu
// 5100, Android emülatörde localhost host makineye hiç ulaşmıyor) — checkoutUrl'in origin'i
// bu yüzden güvenilir değil. apiClient zaten platforma göre doğru baseURL'i taşıyor,
// origin'i atıp sadece path+query'yi ona veriyoruz.
function stripOrigin(url: string): string {
  return url.replace(/^https?:\/\/[^/]+/, '');
}

export default function PaymentScreen() {
  const params = useLocalSearchParams<{
    paymentId?: string;
    checkoutUrl?: string;
    amountMinor?: string;
    remainingAtShopMinor?: string;
    kind?: string;
    bookingId?: string;
    expiresAt?: string;
  }>();
  const router = useRouter();

  const checkoutUrl = params.checkoutUrl;
  const bookingId = params.bookingId;
  const expiresAt = params.expiresAt;
  const amountMinor = params.amountMinor ? parseInt(params.amountMinor, 10) : 0;
  const remaining = params.remainingAtShopMinor ? parseInt(params.remainingAtShopMinor, 10) : 0;
  const kind: PaymentKind = params.kind === 'Deposit' ? 'Deposit' : 'FullOnline';
  const paramsValid = !!checkoutUrl && !!bookingId;
  const isMock = !!checkoutUrl?.includes('/payments/mock/complete');

  const [phase, setPhase] = useState<Phase>(paramsValid ? 'checkout' : 'error');
  const [errorMessage, setErrorMessage] = useState<string | null>(
    paramsValid
      ? null
      : 'Ödeme bilgisi eksik geldi. Randevunu Randevularım sayfasından bulup ödemeyi tekrar başlatabilirsin.',
  );
  const [mockLoading, setMockLoading] = useState(false);

  // 'verifying' fazı: booking Confirmed olana kadar 2sn arayla poll et (max 8 deneme ≈ 14sn).
  // Webhook/mock complete asenkron çalıştığı için booking'in Confirmed'e geçmesi anlık olmayabilir.
  useEffect(() => {
    if (phase !== 'verifying' || !bookingId) return;
    let cancelled = false;
    let timeoutId: ReturnType<typeof setTimeout> | undefined;
    let attempt = 0;

    async function poll() {
      attempt += 1;
      try {
        const booking = await BookingApi.getById(bookingId!);
        if (cancelled) return;
        if (isBookingConfirmed(booking.status)) {
          Haptics.success();
          setPhase('success');
          return;
        }
      } catch (err) {
        ErrorReporter.capture(err, { tag: 'booking.payment.verify-poll', extra: { bookingId, attempt } });
        // Geçici ağ hatası doğrulamayı iptal ettirmesin — deneme hakkı bitene kadar devam et.
      }
      if (cancelled) return;
      if (attempt >= VERIFY_MAX_ATTEMPTS) {
        setPhase('pendingWebhook');
        return;
      }
      timeoutId = setTimeout(poll, VERIFY_INTERVAL_MS);
    }

    poll();
    return () => {
      cancelled = true;
      if (timeoutId) clearTimeout(timeoutId);
    };
  }, [phase, bookingId]);

  async function handleSimulate() {
    if (!checkoutUrl) return;
    setMockLoading(true);
    Haptics.light();
    try {
      await apiClient.get(stripOrigin(checkoutUrl));
      setPhase('verifying');
    } catch (err) {
      ErrorReporter.capture(err, { tag: 'booking.payment.mock-simulate', extra: { paymentId: params.paymentId } });
      Haptics.error();
      setErrorMessage(extractApiError(err));
      setPhase('error');
    } finally {
      setMockLoading(false);
    }
  }

  function handleWebViewSuccess() {
    Haptics.light();
    setPhase('verifying');
  }

  function handleWebViewCancel() {
    Haptics.warning();
    setPhase('cancelled');
  }

  function handleRetry() {
    setErrorMessage(null);
    setPhase('checkout');
  }

  function goToBookingDetail() {
    if (bookingId) router.replace(`/booking/${bookingId}`);
    else router.replace('/(tabs)/bookings');
  }

  function goToBookings() {
    router.replace('/(tabs)/bookings');
  }

  // paramsValid=false ise 'checkout' fazına dönmenin bir anlamı yok (checkoutUrl/bookingId
  // hiç yok) — o durumda "Tekrar dene" yerine tek çıkış "Randevularıma dön".
  const canRetryCheckout = paramsValid;
  const showCountdown = !!expiresAt && (phase === 'checkout' || phase === 'verifying');
  const showWebView = phase === 'checkout' && !isMock && !!checkoutUrl;

  return (
    <SafeAreaView style={styles.container} edges={['top', 'bottom']}>
      <View style={styles.header}>
        <Pressable
          onPress={() => router.back()}
          style={styles.backBtn}
          accessibilityRole="button"
          accessibilityLabel="Geri dön"
        >
          <Text style={styles.back}>← Geri</Text>
        </Pressable>
        <Text style={styles.heading}>Ödeme</Text>
      </View>

      {showCountdown ? (
        <View style={styles.countdownWrap}>
          <PaymentCountdown expiresAt={expiresAt!} />
        </View>
      ) : null}

      {showWebView ? (
        <CheckoutWebView
          checkoutUrl={checkoutUrl!}
          onSuccess={handleWebViewSuccess}
          onCancel={handleWebViewCancel}
        />
      ) : (
        <ScrollView contentContainerStyle={styles.scroll} showsVerticalScrollIndicator={false}>
          {phase === 'checkout' && isMock ? (
            <MockCheckoutCard
              amountMinor={amountMinor}
              remainingAtShopMinor={remaining}
              kind={kind}
              loading={mockLoading}
              onSimulate={handleSimulate}
            />
          ) : null}

          {phase === 'verifying' ? (
            <Card style={styles.verifyingCard}>
              <ActivityIndicator color={Colors.primary} size="large" />
              <Text style={styles.verifyingText}>Ödemen doğrulanıyor…</Text>
            </Card>
          ) : null}

          {phase === 'success' ? (
            <PaymentResultCard
              icon="✅"
              title="Ödeme alındı"
              message={
                kind === 'Deposit' && remaining > 0
                  ? `Randevun onaylandı.\n\nSalonda ödenecek: ${formatTry(remaining)}`
                  : 'Randevun onaylandı.'
              }
              primaryLabel="Randevuya git"
              onPrimary={goToBookingDetail}
            />
          ) : null}

          {phase === 'cancelled' ? (
            <PaymentResultCard
              icon="🚫"
              title="Ödeme iptal edildi"
              message="Ödemeyi tamamlamadan çıktın. Randevun ödeme bekliyor durumunda kalabilir."
              primaryLabel="Tekrar dene"
              onPrimary={handleRetry}
              secondaryLabel="Randevularıma dön"
              onSecondary={goToBookings}
            />
          ) : null}

          {phase === 'pendingWebhook' ? (
            <PaymentResultCard
              icon="⏳"
              title="Ödemen işleniyor"
              message="Onaylanınca bildirim alacaksın. Randevu detayından da durumu kontrol edebilirsin."
              primaryLabel="Randevuya git"
              onPrimary={goToBookingDetail}
            />
          ) : null}

          {phase === 'error' ? (
            <PaymentResultCard
              icon="⚠️"
              title="Bir şeyler ters gitti"
              message={errorMessage ?? 'Beklenmeyen bir hata oluştu.'}
              primaryLabel={canRetryCheckout ? 'Tekrar dene' : 'Randevularıma dön'}
              onPrimary={canRetryCheckout ? handleRetry : goToBookings}
              secondaryLabel={canRetryCheckout ? 'Randevularıma dön' : undefined}
              onSecondary={canRetryCheckout ? goToBookings : undefined}
            />
          ) : null}
        </ScrollView>
      )}
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.md,
    paddingHorizontal: Layout.screenPaddingX,
    paddingTop: Spacing.sm,
    paddingBottom: Spacing.xs,
  },
  backBtn: { paddingVertical: 6 },
  back: { color: Colors.primary, fontSize: FontSize.sm, fontWeight: FontWeight.medium },
  heading: { fontSize: FontSize.lg, fontWeight: FontWeight.bold, color: Colors.text },
  countdownWrap: { paddingHorizontal: Layout.screenPaddingX, paddingBottom: Spacing.sm },
  scroll: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingBottom: Spacing.xxl,
    gap: Spacing.lg,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  verifyingCard: { alignItems: 'center', gap: Spacing.md },
  verifyingText: { fontSize: FontSize.md, color: Colors.textSecondary },
});
