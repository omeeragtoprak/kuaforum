import { View, Text, StyleSheet, FlatList, RefreshControl, Alert, Linking, Pressable } from 'react-native';
import { useRouter } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card } from '@/components/Card';
import { Badge } from '@/components/Badge';
import { Button } from '@/components/Button';
import { EmptyState } from '@/components/EmptyState';
import { Icon } from '@/components/Icon';
import { SkeletonBookingRow, SkeletonList } from '@/components/Skeleton';
import { Colors, FontSize, FontWeight, LetterSpacing, Spacing, Layout, Radius } from '@/lib/theme';
import { BookingApi, Booking } from '@/api/booking';
import { BarberApi } from '@/api/barber';
import { resolveOnboardingStep } from '@/lib/onboarding';
import { extractApiError } from '@/api/client';
import { Haptics } from '@/lib/haptics';
import {
  formatTime, formatTry, formatPhoneTr,
  bookingStatusInfo, isBookingConfirmed, isBookingInProgress, isBookingPending,
  isBookingRevenueCounted, isBookingUpcoming, isBookingCancelled,
} from '@/lib/format';

export default function BarberTodayScreen() {
  const qc = useQueryClient();
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const tomorrow = new Date(today);
  tomorrow.setDate(today.getDate() + 1);

  const query = useQuery({
    queryKey: ['owner-bookings-today'],
    queryFn: () => BookingApi.getOwnerBookings(today.toISOString(), tomorrow.toISOString()),
  });

  const router = useRouter();

  // Salonun kurulum durumu. NEDEN BURADA: Draft (henüz sihirbaz bitmemiş) veya PendingKyc
  // (onay bekliyor) bir salonun sahibi bu ekrana düştüğünde 0 randevu / ₺0 ciro görüyordu ve
  // EKRANDA DURUMU AÇIKLAYAN HİÇBİR ŞEY YOKTU — emülatörde "Yeni kuaför (Draft)" hızlı
  // girişiyle yakalandı. Sihirbaza dönmenin bir yolu da yoktu: `resolveOnboardingStep`
  // yalnız onboarding ekranının KENDİ içinde çağrılıyordu, yani oraya zaten gitmiş olmak
  // gerekiyordu. Sonuç: yeni kaydolan her kuaför boş bir panele çarpıp tıkanıyordu.
  const shopQuery = useQuery({ queryKey: ['my-shop'], queryFn: () => BarberApi.getMyShop() });

  const statsQuery = useQuery({
    queryKey: ['owner-dashboard-stats'],
    queryFn: () => BookingApi.getOwnerStats(),
    staleTime: 5 * 60_000,
  });

  const startMut = useMutation({
    mutationFn: (id: string) => BookingApi.start(id),
    onSuccess: () => {
      Haptics.success();
      qc.invalidateQueries({ queryKey: ['owner-bookings-today'] });
    },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });
  const completeMut = useMutation({
    mutationFn: (id: string) => BookingApi.complete(id),
    onSuccess: () => {
      Haptics.success();
      qc.invalidateQueries({ queryKey: ['owner-bookings-today'] });
    },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });
  const noShowMut = useMutation({
    mutationFn: (id: string) => BookingApi.markNoShow(id),
    onSuccess: () => {
      Haptics.warning();
      qc.invalidateQueries({ queryKey: ['owner-bookings-today'] });
    },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });

  const list = query.data ?? [];
  const totalRevenue = list
    .filter((b) => isBookingRevenueCounted(b.status))
    .reduce((sum, b) => sum + (b.totalAmountMinor || 0), 0);
  const upcomingCount = list.filter((b) => isBookingUpcoming(b.status)).length;

  /**
   * Salon kurulumu bitmemiş/onay bekliyorsa gösterilecek durum kartı.
   *
   * Ürün ilkesi (docs): sistem KARAR VERMEZ, KÖRLÜĞÜ GİDERİR. Bu yüzden kullanıcıyı
   * zorla sihirbaza YÖNLENDİRMİYORUZ (paneli gezebilir, hizmetlerine bakabilir);
   * yalnız durumu söyleyip yolu gösteriyoruz. Kart, istatistiklerin ÜSTÜNDE durur —
   * "0 randevu" rakamını görmeden önce NEDEN 0 olduğunu okusun.
   *
   * `resolveOnboardingStep` Draft dışındaki her statüde 'done' döner; PendingKyc'i
   * ondan ayırmak için status'e ayrıca bakılıyor (backend int veya string gönderebilir,
   * onboarding.ts'teki DRAFT_VALUES ile aynı savunma).
   */
  const shop = shopQuery.data ?? null;
  const setupNotice = (() => {
    if (shopQuery.isLoading || shopQuery.isError) return null;

    if (!shop) {
      return {
        icon: 'store' as const,
        tone: Colors.accent,
        title: 'Salonunu oluştur',
        body: 'Randevu alabilmen için önce salon bilgilerini girmen gerekiyor.',
        cta: true,
        onPress: () => router.push('/onboarding/shop'),
      };
    }

    const step = resolveOnboardingStep({ status: shop.status, addressLine1: shop.addressLine1 });
    if (step !== 'done') {
      const what = step === 1 ? 'salon bilgilerin' : step === 2 ? 'adresin' : 'belgelerin';
      return {
        icon: 'store' as const,
        tone: Colors.accent,
        title: 'Kurulumun yarım kaldı',
        body: `Sırada ${what} var. Tamamlayınca başvurun incelemeye alınır.`,
        cta: true,
        onPress: () => router.push('/onboarding/shop'),
      };
    }

    // String()'e çevrilerek karşılaştırılıyor: tip number diyor ama backend
    // JsonStringEnumConverter ile 'PendingKyc' de gönderebiliyor (onboarding.ts'teki
    // DRAFT_VALUES ile aynı savunma). Tek dalda ikisini birden kapsar.
    const statusKey = String(shop.status);
    const isPendingKyc = statusKey === '1' || statusKey === 'PendingKyc';
    if (isPendingKyc) {
      return {
        icon: 'clock' as const,
        tone: Colors.warning,
        title: 'Başvurun inceleniyor',
        body: 'Onaylanınca salonun listelenmeye ve randevu almaya başlar.',
        cta: true,
        onPress: () => router.push('/(barber)/kyc'),
      };
    }

    const isSuspended = statusKey === '3' || statusKey === 'Suspended';
    if (isSuspended) {
      return {
        icon: 'alert' as const,
        tone: Colors.error,
        title: 'Salonun askıya alındı',
        body: 'Yeni randevu alamazsın. Ayrıntı için destekle iletişime geç.',
        cta: false,
        onPress: () => {},
      };
    }

    return null;
  })();

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <View style={styles.header}>
        <Text style={styles.dayLabel}>
          {today.toLocaleDateString('tr-TR', { weekday: 'long', day: '2-digit', month: 'long' })}
        </Text>
        <Text style={styles.title}>Bugün</Text>

        {setupNotice ? (
          <Pressable
            style={styles.setupCard}
            onPress={setupNotice.onPress}
            accessibilityRole="button"
            accessibilityLabel={`${setupNotice.title}. ${setupNotice.body}`}
          >
            <View style={styles.setupIcon}>
              <Icon name={setupNotice.icon} size={20} color={setupNotice.tone} />
            </View>
            <View style={{ flex: 1 }}>
              <Text style={styles.setupTitle}>{setupNotice.title}</Text>
              <Text style={styles.setupBody}>{setupNotice.body}</Text>
            </View>
            {setupNotice.cta ? <Icon name="chevronRight" size={18} color={Colors.textMuted} /> : null}
          </Pressable>
        ) : null}

        <View style={styles.statsRow}>
          <View style={styles.statBox}>
            <Text style={styles.statValue}>{list.length}</Text>
            <Text style={styles.statLabel}>Toplam</Text>
          </View>
          <View style={styles.statBox}>
            <Text style={styles.statValue}>{upcomingCount}</Text>
            <Text style={styles.statLabel}>Bekleyen</Text>
          </View>
          <View style={styles.statBox}>
            <Text style={styles.statValue} numberOfLines={1} adjustsFontSizeToFit>{formatTry(totalRevenue)}</Text>
            <Text style={styles.statLabel}>Ciro</Text>
          </View>
        </View>

        {statsQuery.data ? (
          <View style={styles.trendCard} accessible accessibilityLabel={`Bu hafta ${formatTry(statsQuery.data.thisWeekRevenueMinor)} ciro, geçen haftaya göre yüzde ${statsQuery.data.revenueDeltaPercent} fark`}>
            <View style={{ flex: 1 }}>
              <Text style={styles.trendLabel}>BU HAFTA</Text>
              <Text style={styles.trendValue}>{formatTry(statsQuery.data.thisWeekRevenueMinor)}</Text>
              <Text style={styles.trendSub}>{statsQuery.data.thisWeekCompletedCount} tamamlanan</Text>
            </View>
            <View style={[
              styles.deltaPill,
              statsQuery.data.revenueDeltaPercent >= 0 ? styles.deltaPillPositive : styles.deltaPillNegative,
            ]}>
              <Text style={[
                styles.deltaText,
                statsQuery.data.revenueDeltaPercent >= 0 ? styles.deltaTextPositive : styles.deltaTextNegative,
              ]}>
                {statsQuery.data.revenueDeltaPercent >= 0 ? '↑ ' : '↓ '}
                %{Math.abs(statsQuery.data.revenueDeltaPercent)}
              </Text>
              <Text style={styles.deltaCompare}>geçen haftaya göre</Text>
            </View>
            {statsQuery.data.topServiceName ? (
              <View style={styles.topService}>
                <Text style={styles.topServiceLabel}>EN POPÜLER (30g)</Text>
                <Text style={styles.topServiceName} numberOfLines={1}>{statsQuery.data.topServiceName}</Text>
                <Text style={styles.topServiceCount}>{statsQuery.data.topServiceBookingCount} randevu</Text>
              </View>
            ) : null}
          </View>
        ) : null}
      </View>

      <FlatList
        data={list}
        keyExtractor={(b) => b.id}
        contentContainerStyle={styles.list}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl
            refreshing={query.isFetching && !query.isLoading}
            onRefresh={() => query.refetch()}
            tintColor={Colors.ink}
          />
        }
        ItemSeparatorComponent={() => <View style={{ height: Spacing.md }} />}
        ListEmptyComponent={
          query.isLoading ? (
            <SkeletonList count={3} render={() => <SkeletonBookingRow />} />
          ) : query.isError ? (
            <EmptyState
              icon="alert"
              title="Bağlantı hatası"
              description="Bugünkü randevular yüklenemedi. Sunucu erişilebilir mi?"
              actionLabel="Tekrar dene"
              onAction={() => query.refetch()}
            />
          ) : (
            <EmptyState
              icon="calendar"
              title="Bugün boş"
              description="Randevun yok — kahve molası vereceksin demek."
            />
          )
        }
        renderItem={({ item }) => (
          <BookingRow
            booking={item}
            onStart={() => confirmStart(item, () => startMut.mutate(item.id))}
            onComplete={() => confirmComplete(item, () => completeMut.mutate(item.id))}
            onNoShow={() =>
              Alert.alert('Müşteri gelmedi', 'No-show olarak işaretlemek istiyor musun?', [
                { text: 'Vazgeç', style: 'cancel' },
                { text: 'Evet', style: 'destructive', onPress: () => noShowMut.mutate(item.id) },
              ])
            }
            busy={startMut.isPending || completeMut.isPending || noShowMut.isPending}
          />
        )}
      />
    </SafeAreaView>
  );
}

function confirmStart(booking: Booking, onConfirm: () => void) {
  const minutesUntil = Math.round((new Date(booking.scheduledAt).getTime() - Date.now()) / 60000);
  if (minutesUntil > 15) {
    Alert.alert(
      'Erken başlatma',
      `Randevu saatine ${minutesUntil} dakika var. Yine de başlatmak istiyor musun?`,
      [
        { text: 'Vazgeç', style: 'cancel' },
        { text: 'Yine de başlat', onPress: onConfirm },
      ],
    );
    return;
  }
  onConfirm();
}

function confirmComplete(booking: Booking, onConfirm: () => void) {
  Alert.alert(
    'Hizmeti tamamla',
    `${formatTime(booking.scheduledAt)} randevusunu tamamlamak istediğine emin misin? Müşteriye "Tamamlandı" bildirimi gidecek.`,
    [
      { text: 'Vazgeç', style: 'cancel' },
      { text: 'Tamamla', onPress: onConfirm },
    ],
  );
}

function BookingRow({
  booking, onStart, onComplete, onNoShow, busy,
}: {
  booking: Booking;
  onStart: () => void;
  onComplete: () => void;
  onNoShow: () => void;
  busy: boolean;
}) {
  const status = bookingStatusInfo(booking.status);
  const canStart = isBookingConfirmed(booking.status);
  const canComplete = isBookingInProgress(booking.status);
  // Ödenmiş ama hâlâ owner onayı bekleyen tek talep türü — kuaförün ekranda aksiyon
  // alması gereken tek şey bu, o yüzden yalnız renkle değil kenar çizgisiyle de
  // (biçimle) ayırt ediliyor.
  const needsAttention = isBookingPending(booking.status) && booking.paymentKind != null;

  return (
    <Card style={[{ gap: Spacing.sm }, needsAttention && styles.attentionCard]}>
      <View style={styles.head}>
        <View style={styles.timeBlock}>
          <Text style={styles.time}>{formatTime(booking.scheduledAt)}</Text>
          <Text style={styles.duration}>
            {booking.items.reduce((s, i) => s + (i.durationMinutes ?? 0), 0)} dk
          </Text>
        </View>
        <Badge label={status.label} tone={status.tone} />
      </View>

      {/* RFC-013 C4/C5c: ödeme booking'i otomatik onaylamıyor — kapora/tam online ödemesi
          alınmış ama hâlâ Pending (owner onayı bekleyen) talepler burada öne çıkarılıyor ki
          kuaför bunlara öncelik versin (para zaten yatmış, yanıt bekliyor). */}
      {needsAttention ? (
        <View
          accessible
          accessibilityLabel="Ödendi, onayınız bekleniyor. Öncelik verin."
        >
          <Badge label="Ödendi · onay bekliyor" tone="warning" />
        </View>
      ) : null}

      <View style={styles.customerBlock}>
        <View style={styles.customerLine}>
          <Icon name="user" size={14} color={Colors.textMuted} />
          <Text style={styles.customerName}>
            {booking.customerName ?? `Müşteri #${booking.customerId.slice(0, 8)}`}
          </Text>
        </View>
        {booking.customerPhone ? (
          <Pressable
            onPress={() => Linking.openURL(`tel:${booking.customerPhone}`)}
            hitSlop={6}
            style={styles.customerLine}
            accessibilityRole="link"
            accessibilityLabel={`Müşteriyi ara ${booking.customerPhone}`}
          >
            <Icon name="phone" size={14} color={Colors.accent} />
            <Text style={styles.customerPhone}>{formatPhoneTr(booking.customerPhone)}</Text>
          </Pressable>
        ) : null}
        {booking.customerNote ? (
          <View style={styles.noteBox}>
            <Text style={styles.noteLabel}>Not</Text>
            <Text style={styles.noteText}>{booking.customerNote}</Text>
          </View>
        ) : null}
      </View>

      <View style={styles.servicesBlock}>
        {booking.items.map((it) => (
          <View key={it.id} style={styles.serviceRow}>
            <Text style={styles.serviceDot}>·</Text>
            <Text style={styles.serviceName}>{it.serviceName}</Text>
            <Text style={styles.serviceMeta}>{it.durationMinutes} dk</Text>
          </View>
        ))}
      </View>

      <View style={styles.amountRow}>
        <Text style={styles.amountLabel}>Toplam</Text>
        <Text style={styles.amount}>{formatTry(booking.totalAmountMinor)}</Text>
      </View>

      {booking.remainingAtShopMinor && !isBookingCancelled(booking.status) ? (
        <View
          accessible
          accessibilityLabel={`Salonda ödenecek tutar: ${formatTry(booking.remainingAtShopMinor)}`}
        >
          <Badge label={`Salonda: ${formatTry(booking.remainingAtShopMinor)}`} tone="warning" />
        </View>
      ) : null}

      {(canStart || canComplete) ? (
        <View style={styles.actions}>
          {canStart ? (
            <>
              <Button label="Başlat" onPress={onStart} disabled={busy} size="sm" />
              <Button label="Gelmedi" variant="ghost" onPress={onNoShow} disabled={busy} size="sm" fullWidth={false} />
            </>
          ) : null}
          {canComplete ? <Button label="Tamamla" onPress={onComplete} disabled={busy} size="sm" /> : null}
        </View>
      ) : null}
    </Card>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: { paddingHorizontal: Layout.screenPaddingX, paddingTop: Spacing.lg, paddingBottom: Spacing.lg },
  dayLabel: {
    fontSize: FontSize.xs,
    color: Colors.textSecondary,
    textTransform: 'uppercase',
    letterSpacing: LetterSpacing.label,
    fontWeight: FontWeight.bold,
  },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text, marginTop: 4 },
  setupCard: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.md,
    backgroundColor: Colors.surfaceAlt,
    borderRadius: Radius.lg,
    padding: Spacing.lg,
    marginBottom: Spacing.md,
  },
  setupIcon: {
    width: 40,
    height: 40,
    borderRadius: Radius.full,
    backgroundColor: Colors.background,
    alignItems: 'center',
    justifyContent: 'center',
  },
  setupTitle: {
    fontSize: FontSize.md,
    fontWeight: FontWeight.semibold,
    color: Colors.ink,
    letterSpacing: LetterSpacing.title,
  },
  setupBody: {
    fontSize: FontSize.sm,
    color: Colors.textSecondary,
    marginTop: 2,
    lineHeight: 18,
  },
  statsRow: { flexDirection: 'row', gap: Spacing.sm, marginTop: Spacing.md },
  statBox: {
    flex: 1,
    backgroundColor: Colors.surface,
    padding: Spacing.md,
    borderRadius: Radius.md,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  statValue: { fontSize: FontSize.lg, fontWeight: FontWeight.bold, color: Colors.text, fontVariant: ['tabular-nums'] },
  statLabel: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 2 },
  trendCard: {
    flexDirection: 'row',
    alignItems: 'center',
    flexWrap: 'wrap',
    backgroundColor: Colors.surfaceAlt,
    borderRadius: Radius.md,
    padding: Spacing.md,
    marginTop: Spacing.md,
    gap: Spacing.md,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  trendLabel: {
    fontSize: 10,
    color: Colors.textMuted,
    fontWeight: FontWeight.bold,
    letterSpacing: 0.5,
  },
  trendValue: { fontSize: FontSize.xl, fontWeight: FontWeight.bold, color: Colors.text, marginTop: 2, fontVariant: ['tabular-nums'] },
  trendSub: { fontSize: FontSize.xs, color: Colors.textSecondary, marginTop: 2 },
  deltaPill: {
    paddingHorizontal: Spacing.sm,
    paddingVertical: 6,
    borderRadius: Radius.md,
    alignItems: 'center',
  },
  deltaPillPositive: { backgroundColor: Colors.successSoft },
  deltaPillNegative: { backgroundColor: Colors.errorSoft },
  deltaText: { fontSize: FontSize.md, fontWeight: FontWeight.bold, fontVariant: ['tabular-nums'] },
  deltaTextPositive: { color: Colors.success },
  deltaTextNegative: { color: Colors.error },
  deltaCompare: { fontSize: 9, color: Colors.textMuted, marginTop: 2 },
  topService: {
    flexBasis: '100%',
    paddingTop: Spacing.sm,
    borderTopWidth: 1,
    borderTopColor: Colors.border,
  },
  topServiceLabel: { fontSize: 10, color: Colors.textMuted, fontWeight: FontWeight.bold, letterSpacing: 0.5 },
  topServiceName: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, color: Colors.text, marginTop: 2 },
  topServiceCount: { fontSize: FontSize.xs, color: Colors.textSecondary },
  list: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingBottom: Spacing.xl,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  head: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-start' },
  timeBlock: { flexDirection: 'column' },
  time: { fontSize: FontSize.xl, fontWeight: FontWeight.bold, color: Colors.text, fontVariant: ['tabular-nums'] },
  duration: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 2 },
  // Onay bekleyen ödenmiş randevu: sol kenar bakır çizgi — renk körlüğünde/güneş
  // altında da "buna bak" sinyali versin diye yalnız Badge'e güvenilmiyor.
  attentionCard: {
    borderLeftWidth: 3,
    borderLeftColor: Colors.accent,
  },
  customerBlock: { gap: 6 },
  customerLine: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  customerName: { fontSize: FontSize.md, color: Colors.text, fontWeight: FontWeight.semibold },
  customerPhone: { fontSize: FontSize.sm, color: Colors.accent, fontWeight: FontWeight.medium },
  noteBox: {
    marginTop: 4,
    backgroundColor: Colors.surfaceAlt,
    borderRadius: Radius.sm,
    padding: Spacing.sm,
  },
  noteLabel: {
    fontSize: FontSize.xs,
    color: Colors.textSecondary,
    fontWeight: FontWeight.bold,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    marginBottom: 2,
  },
  noteText: { fontSize: FontSize.sm, color: Colors.text, lineHeight: 18 },
  servicesBlock: { gap: 4 },
  serviceRow: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  serviceDot: { color: Colors.textMuted, fontSize: 16, fontWeight: FontWeight.bold },
  serviceName: { flex: 1, fontSize: FontSize.sm, color: Colors.text },
  serviceMeta: { fontSize: FontSize.xs, color: Colors.textMuted },
  amountRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingTop: Spacing.sm,
    borderTopWidth: 1,
    borderTopColor: Colors.divider,
  },
  amountLabel: { fontSize: FontSize.xs, color: Colors.textSecondary, textTransform: 'uppercase', letterSpacing: 0.5 },
  amount: { fontSize: FontSize.lg, fontWeight: FontWeight.bold, color: Colors.text, fontVariant: ['tabular-nums'] },
  actions: { flexDirection: 'row', gap: Spacing.sm, marginTop: Spacing.xs },
});
