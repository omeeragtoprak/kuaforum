import { useEffect, useMemo, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, Alert, Platform, KeyboardAvoidingView, Pressable, ViewStyle } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { AxiosError } from 'axios';
import { Button } from '@/components/Button';
import { EmptyState } from '@/components/EmptyState';
import { Skeleton } from '@/components/Skeleton';
import { DepositSection } from '@/components/booking-policy/DepositSection';
import { BookingRulesSection } from '@/components/booking-policy/BookingRulesSection';
import { Colors, FontSize, FontWeight, Radius, Spacing, Layout } from '@/lib/theme';
import { BarberApi } from '@/api/barber';
import { extractApiError } from '@/api/client';
import { Haptics } from '@/lib/haptics';
import {
  BookingPolicyDraft,
  DepositPercentBounds,
  draftFromDto,
  draftToPayload,
  formatPercent,
  hasPolicyChanges,
  parseDecimalOrNull,
  sanitizeDecimalText,
  validateBookingPolicy,
} from '@/lib/booking-policy';

type TouchedField = 'depositPercent' | 'maxPendingPerCustomer' | 'minDaysBetweenBookings' | 'maxBookingsPerMonth';
const ALL_FIELDS: TouchedField[] = ['depositPercent', 'maxPendingPerCustomer', 'minDaysBetweenBookings', 'maxBookingsPerMonth'];
const QUERY_KEY = ['my-booking-policy'];

export default function BookingSettingsScreen() {
  const router = useRouter();
  const qc = useQueryClient();

  const policyQuery = useQuery({
    queryKey: QUERY_KEY,
    queryFn: () => BarberApi.getBookingPolicy(),
    retry: false,
  });

  const [draft, setDraft] = useState<BookingPolicyDraft | null>(null);
  const [percentText, setPercentText] = useState('');
  const [minDaysText, setMinDaysText] = useState('');
  const [maxMonthlyText, setMaxMonthlyText] = useState('');
  const [touched, setTouched] = useState<Set<TouchedField>>(new Set());

  // Sunucudan ilk yükleme tamamlanınca draft'ı doldur. Kaydet sonrası (onSuccess) draft
  // manuel senkronize edildiği için burada tekrar tetiklenmez (draft !== null guard).
  useEffect(() => {
    if (policyQuery.data && draft === null) {
      syncFromDto(policyQuery.data);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [policyQuery.data]);

  function syncFromDto(dto: NonNullable<typeof policyQuery.data>) {
    const d = draftFromDto(dto);
    setDraft(d);
    setPercentText(d.depositPercent !== null ? formatPercent(d.depositPercent) : '');
    setMinDaysText(d.minDaysBetweenBookings !== null ? String(d.minDaysBetweenBookings) : '');
    setMaxMonthlyText(d.maxBookingsPerMonth !== null ? String(d.maxBookingsPerMonth) : '');
    setTouched(new Set());
  }

  const bounds: DepositPercentBounds = useMemo(
    () => ({
      depositPercentMin: policyQuery.data?.depositPercentMin ?? 0,
      depositPercentMax: policyQuery.data?.depositPercentMax ?? 1,
    }),
    [policyQuery.data?.depositPercentMin, policyQuery.data?.depositPercentMax],
  );

  const errors = useMemo(() => (draft ? validateBookingPolicy(draft, bounds) : {}), [draft, bounds]);
  const dirty = draft && policyQuery.data ? hasPolicyChanges(policyQuery.data, draft) : false;

  const saveMut = useMutation({
    mutationFn: () => BarberApi.updateBookingPolicy(draftToPayload(draft!)),
    onSuccess: (data) => {
      Haptics.success();
      qc.setQueryData(QUERY_KEY, data);
      syncFromDto(data);
      Alert.alert('Kaydedildi', 'Randevu ayarların güncellendi.');
    },
    onError: (err) => {
      Haptics.error();
      // Admin aralığı (depositPercentMin/Max) bu arada değişmiş olabilir — ekrandaki
      // "izin verilen aralık" ipucu bayat kalmasın diye sunucudan taze veriyi çek (M11).
      qc.invalidateQueries({ queryKey: QUERY_KEY });
      Alert.alert('Kaydedilemedi', extractApiError(err));
    },
  });

  function handleSave() {
    if (!draft) return;
    const currentErrors = validateBookingPolicy(draft, bounds);
    if (Object.keys(currentErrors).length > 0) {
      setTouched(new Set(ALL_FIELDS));
      Haptics.error();
      return;
    }
    saveMut.mutate();
  }

  function touch(field: TouchedField) {
    setTouched((prev) => (prev.has(field) ? prev : new Set(prev).add(field)));
  }

  function untouch(field: TouchedField) {
    setTouched((prev) => {
      if (!prev.has(field)) return prev;
      const next = new Set(prev);
      next.delete(field);
      return next;
    });
  }

  function updateDraft(patch: Partial<BookingPolicyDraft>) {
    setDraft((prev) => (prev ? { ...prev, ...patch } : prev));
  }

  function handlePercentChange(t: string) {
    const cleaned = sanitizeDecimalText(t, 2);
    setPercentText(cleaned);
    const n = parseDecimalOrNull(cleaned);
    updateDraft({ depositPercent: n !== null ? n / 100 : null });
  }

  function handleMinDaysChange(t: string) {
    const cleaned = t.replace(/\D/g, '').slice(0, 3);
    setMinDaysText(cleaned);
    const n = parseInt(cleaned, 10);
    updateDraft({ minDaysBetweenBookings: cleaned.length > 0 && !Number.isNaN(n) ? n : null });
  }

  function handleMaxMonthlyChange(t: string) {
    const cleaned = t.replace(/\D/g, '').slice(0, 3);
    setMaxMonthlyText(cleaned);
    const n = parseInt(cleaned, 10);
    updateDraft({ maxBookingsPerMonth: cleaned.length > 0 && !Number.isNaN(n) ? n : null });
  }

  const notFound = isNotFoundError(policyQuery.error);

  if (policyQuery.isLoading) {
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <ScreenHeader onBack={() => router.back()} style={styles.headerPadX} />
        <View style={styles.scroll}>
          <Skeleton height={120} radius={Radius.lg} style={{ marginBottom: Spacing.md }} />
          <Skeleton height={220} radius={Radius.lg} />
        </View>
      </SafeAreaView>
    );
  }

  if (notFound) {
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <ScreenHeader onBack={() => router.back()} style={styles.headerPadX} />
        <EmptyState
          emoji="🏪"
          title="Salon yok"
          description="Önce Profil sekmesinden salon bilgilerini girmelisin."
        />
      </SafeAreaView>
    );
  }

  if (policyQuery.isError || !draft || !policyQuery.data) {
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <ScreenHeader onBack={() => router.back()} style={styles.headerPadX} />
        <EmptyState
          emoji="⚠️"
          title="Bağlantı hatası"
          description="Randevu ayarların yüklenemedi."
          actionLabel="Tekrar dene"
          onAction={() => policyQuery.refetch()}
        />
      </SafeAreaView>
    );
  }

  const showMasterSwitchNotice = policyQuery.data.depositEnabled && !policyQuery.data.effectiveDepositActive;

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <KeyboardAvoidingView style={{ flex: 1 }} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView
          contentContainerStyle={styles.scroll}
          showsVerticalScrollIndicator={false}
          keyboardShouldPersistTaps="handled"
        >
          <ScreenHeader onBack={() => router.back()} subtitle="Kapora ve spam koruma kurallarını buradan yönet." />

          <DepositSection
            depositEnabled={draft.depositEnabled}
            onToggleDeposit={(value) => updateDraft({ depositEnabled: value })}
            percentText={percentText}
            onChangePercentText={handlePercentChange}
            onBlurPercent={() => touch('depositPercent')}
            percentError={touched.has('depositPercent') ? errors.depositPercent : undefined}
            bounds={bounds}
            showMasterSwitchNotice={showMasterSwitchNotice}
          />

          <BookingRulesSection
            maxPendingPerCustomer={draft.maxPendingPerCustomer}
            onSelectPending={(n) => {
              updateDraft({ maxPendingPerCustomer: n });
              touch('maxPendingPerCustomer');
            }}
            pendingError={touched.has('maxPendingPerCustomer') ? errors.maxPendingPerCustomer : undefined}
            minDaysEnabled={draft.minDaysBetweenBookingsEnabled}
            onToggleMinDays={(value) => {
              updateDraft({ minDaysBetweenBookingsEnabled: value });
              if (!value) untouch('minDaysBetweenBookings');
            }}
            minDaysText={minDaysText}
            onChangeMinDaysText={handleMinDaysChange}
            onBlurMinDays={() => touch('minDaysBetweenBookings')}
            minDaysError={touched.has('minDaysBetweenBookings') ? errors.minDaysBetweenBookings : undefined}
            maxMonthlyEnabled={draft.maxBookingsPerMonthEnabled}
            onToggleMaxMonthly={(value) => {
              updateDraft({ maxBookingsPerMonthEnabled: value });
              if (!value) untouch('maxBookingsPerMonth');
            }}
            maxMonthlyText={maxMonthlyText}
            onChangeMaxMonthlyText={handleMaxMonthlyChange}
            onBlurMaxMonthly={() => touch('maxBookingsPerMonth')}
            maxMonthlyError={touched.has('maxBookingsPerMonth') ? errors.maxBookingsPerMonth : undefined}
          />

          <View style={styles.saveRow}>
            <Button
              label="Kaydet"
              onPress={handleSave}
              loading={saveMut.isPending}
              disabled={!dirty || saveMut.isPending}
            />
          </View>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

/** reviews.tsx'teki "← Geri" kalıbıyla aynı — push ile gelinen bu ekranda tab bar yok,
 *  headerShown:false olduğundan geri butonu olmazsa kullanıcı sıkışır (review I3). */
function ScreenHeader({ onBack, subtitle, style }: { onBack: () => void; subtitle?: string; style?: ViewStyle }) {
  return (
    <View style={[styles.header, style]}>
      <Pressable onPress={onBack} style={styles.backBtn} accessibilityRole="button" accessibilityLabel="Geri">
        <Text style={styles.back}>← Geri</Text>
      </Pressable>
      <Text style={styles.title}>Randevu Ayarları</Text>
      {subtitle ? <Text style={styles.subtitle}>{subtitle}</Text> : null}
    </View>
  );
}

function isNotFoundError(err: unknown): boolean {
  const axErr = err as AxiosError | null;
  return !!axErr && axErr.response?.status === 404;
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  scroll: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingBottom: Spacing.xxl,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  // NOT: bu View, hem ScrollView içeriğinde (contentContainerStyle zaten paddingHorizontal
  // veriyor) HEM DE erken dönüş (loading/404/hata) dallarında SafeAreaView'a doğrudan çocuk
  // olarak kullanılıyor. Kendi paddingHorizontal'ı olmadığı için erken dönüş dallarında
  // `headerPadX` ile ayrıca padlenmesi gerekiyor — aksi halde çift padding (scroll içinde)
  // ya da hiç padding (standalone'da) olurdu.
  header: { paddingTop: Spacing.lg, paddingBottom: Spacing.md },
  headerPadX: { paddingHorizontal: Layout.screenPaddingX },
  backBtn: { paddingVertical: 6, marginBottom: Spacing.sm, minHeight: Layout.tapTargetMin, justifyContent: 'center', alignSelf: 'flex-start' },
  back: { fontSize: FontSize.md, color: Colors.primary, fontWeight: FontWeight.semibold },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 4 },
  saveRow: { marginTop: Spacing.xl },
});
