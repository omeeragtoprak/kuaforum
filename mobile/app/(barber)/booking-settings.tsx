import { useEffect, useMemo, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, Alert, Switch, Platform, KeyboardAvoidingView } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { AxiosError } from 'axios';
import { Card } from '@/components/Card';
import { Chip } from '@/components/Chip';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { EmptyState } from '@/components/EmptyState';
import { Skeleton } from '@/components/Skeleton';
import { Colors, FontSize, FontWeight, Radius, Spacing, Layout } from '@/lib/theme';
import { BarberApi } from '@/api/barber';
import { extractApiError } from '@/api/client';
import { Haptics } from '@/lib/haptics';
import {
  BookingPolicyDraft,
  DepositPercentBounds,
  MAX_BOOKINGS_PER_MONTH_CEILING,
  MAX_PENDING_PER_CUSTOMER_CEILING,
  MIN_DAYS_BETWEEN_BOOKINGS_CEILING,
  draftFromDto,
  draftToPayload,
  formatPercent,
  hasPolicyChanges,
  validateBookingPolicy,
} from '@/lib/booking-policy';

type TouchedField = 'depositPercent' | 'maxPendingPerCustomer' | 'minDaysBetweenBookings' | 'maxBookingsPerMonth';

const PENDING_OPTIONS = Array.from({ length: MAX_PENDING_PER_CUSTOMER_CEILING }, (_, i) => i + 1);

export default function BookingSettingsScreen() {
  const qc = useQueryClient();

  const policyQuery = useQuery({
    queryKey: ['my-booking-policy'],
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
      qc.setQueryData(['my-booking-policy'], data);
      syncFromDto(data);
      Alert.alert('Kaydedildi', 'Randevu ayarların güncellendi.');
    },
    onError: (err) => {
      Haptics.error();
      Alert.alert('Kaydedilemedi', extractApiError(err));
    },
  });

  function handleSave() {
    if (!draft) return;
    const currentErrors = validateBookingPolicy(draft, bounds);
    if (Object.keys(currentErrors).length > 0) {
      setTouched(new Set(['depositPercent', 'maxPendingPerCustomer', 'minDaysBetweenBookings', 'maxBookingsPerMonth']));
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

  const notFound = isNotFoundError(policyQuery.error);

  if (policyQuery.isLoading) {
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <View style={styles.header}>
          <Text style={styles.title}>Randevu Ayarları</Text>
        </View>
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
          <View style={styles.header}>
            <Text style={styles.title}>Randevu Ayarları</Text>
            <Text style={styles.subtitle}>Kapora ve spam koruma kurallarını buradan yönet.</Text>
          </View>

          <Text style={styles.sectionTitle}>Kapora</Text>
          <Card style={{ gap: Spacing.md }}>
            <View style={styles.switchRow}>
              <View style={{ flex: 1, paddingRight: Spacing.md }}>
                <Text style={styles.rowLabel}>Kapora ödemesi</Text>
                <Text style={styles.rowHint}>
                  Müşteri randevu alırken belirlediğin yüzdeyi online öder, kalanını salonda ödersiniz.
                </Text>
              </View>
              <Switch
                value={draft.depositEnabled}
                onValueChange={(value) => updateDraft({ depositEnabled: value })}
                trackColor={{ true: Colors.success, false: '#E5E7EB' }}
                accessibilityRole="switch"
                accessibilityLabel="Kapora ödemesi"
                accessibilityState={{ checked: draft.depositEnabled }}
              />
            </View>

            {draft.depositEnabled ? (
              <Input
                label="Kapora oranı"
                required
                value={percentText}
                onChangeText={(t) => {
                  const cleaned = t.replace(/[^\d.]/g, '');
                  setPercentText(cleaned);
                  const n = parseFloat(cleaned);
                  updateDraft({ depositPercent: cleaned.length > 0 && !Number.isNaN(n) ? n / 100 : null });
                }}
                onBlur={() => touch('depositPercent')}
                keyboardType="decimal-pad"
                rightSlot={<Text style={styles.unitText}>%</Text>}
                hint={`İzin verilen aralık: %${formatPercent(bounds.depositPercentMin)} – %${formatPercent(bounds.depositPercentMax)}`}
                error={touched.has('depositPercent') ? errors.depositPercent : undefined}
              />
            ) : null}

            {showMasterSwitchNotice ? (
              <View style={styles.noticeBox} accessibilityRole="text">
                <Text style={styles.noticeText}>
                  ℹ️ Online ödeme platform genelinde henüz kapalı — ayarın kaydedildi, ödeme açıldığında otomatik devreye girecek.
                </Text>
              </View>
            ) : null}
          </Card>

          <Text style={styles.sectionTitle}>Randevu kuralları</Text>
          <Card style={{ gap: Spacing.lg }}>
            <View>
              <Text style={styles.rowLabel}>Bekleyen talep limiti</Text>
              <Text style={styles.rowHint}>
                Bir müşterinin aynı anda kaç tane onay bekleyen randevusu olabilir? Düşük tutmak spam rezervasyonu önler.
              </Text>
              <View style={styles.chipsRow}>
                {PENDING_OPTIONS.map((n) => (
                  <Chip
                    key={n}
                    label={String(n)}
                    selected={draft.maxPendingPerCustomer === n}
                    onPress={() => {
                      updateDraft({ maxPendingPerCustomer: n });
                      touch('maxPendingPerCustomer');
                    }}
                  />
                ))}
              </View>
              {touched.has('maxPendingPerCustomer') && errors.maxPendingPerCustomer ? (
                <Text style={styles.errorText}>{errors.maxPendingPerCustomer}</Text>
              ) : null}
            </View>

            <View style={styles.divider} />

            <View>
              <View style={styles.switchRow}>
                <View style={{ flex: 1, paddingRight: Spacing.md }}>
                  <Text style={styles.rowLabel}>Randevular arası minimum gün</Text>
                  <Text style={styles.rowHint}>
                    Aynı müşteri, önceki randevusundan en az bu kadar gün geçmeden yeni randevu alamaz.
                  </Text>
                </View>
                <Switch
                  value={draft.minDaysBetweenBookingsEnabled}
                  onValueChange={(value) => {
                    updateDraft({ minDaysBetweenBookingsEnabled: value });
                    if (!value) untouch('minDaysBetweenBookings');
                  }}
                  trackColor={{ true: Colors.success, false: '#E5E7EB' }}
                  accessibilityRole="switch"
                  accessibilityLabel="Randevular arası minimum gün sınırı"
                  accessibilityState={{ checked: draft.minDaysBetweenBookingsEnabled }}
                />
              </View>
              {draft.minDaysBetweenBookingsEnabled ? (
                <Input
                  label="Minimum gün"
                  value={minDaysText}
                  onChangeText={(t) => {
                    const cleaned = t.replace(/\D/g, '').slice(0, 3);
                    setMinDaysText(cleaned);
                    const n = parseInt(cleaned, 10);
                    updateDraft({ minDaysBetweenBookings: cleaned.length > 0 && !Number.isNaN(n) ? n : null });
                  }}
                  onBlur={() => touch('minDaysBetweenBookings')}
                  keyboardType="number-pad"
                  rightSlot={<Text style={styles.unitText}>gün</Text>}
                  hint={`1 ile ${MIN_DAYS_BETWEEN_BOOKINGS_CEILING} arasında bir değer gir`}
                  error={touched.has('minDaysBetweenBookings') ? errors.minDaysBetweenBookings : undefined}
                />
              ) : null}
            </View>

            <View style={styles.divider} />

            <View>
              <View style={styles.switchRow}>
                <View style={{ flex: 1, paddingRight: Spacing.md }}>
                  <Text style={styles.rowLabel}>Müşteri başına aylık limit</Text>
                  <Text style={styles.rowHint}>
                    Bir müşteri bu salondan bir ayda en fazla bu kadar randevu alabilir.
                  </Text>
                </View>
                <Switch
                  value={draft.maxBookingsPerMonthEnabled}
                  onValueChange={(value) => {
                    updateDraft({ maxBookingsPerMonthEnabled: value });
                    if (!value) untouch('maxBookingsPerMonth');
                  }}
                  trackColor={{ true: Colors.success, false: '#E5E7EB' }}
                  accessibilityRole="switch"
                  accessibilityLabel="Müşteri başına aylık randevu limiti"
                  accessibilityState={{ checked: draft.maxBookingsPerMonthEnabled }}
                />
              </View>
              {draft.maxBookingsPerMonthEnabled ? (
                <Input
                  label="Aylık limit"
                  value={maxMonthlyText}
                  onChangeText={(t) => {
                    const cleaned = t.replace(/\D/g, '').slice(0, 3);
                    setMaxMonthlyText(cleaned);
                    const n = parseInt(cleaned, 10);
                    updateDraft({ maxBookingsPerMonth: cleaned.length > 0 && !Number.isNaN(n) ? n : null });
                  }}
                  onBlur={() => touch('maxBookingsPerMonth')}
                  keyboardType="number-pad"
                  rightSlot={<Text style={styles.unitText}>randevu</Text>}
                  hint={`1 ile ${MAX_BOOKINGS_PER_MONTH_CEILING} arasında bir değer gir`}
                  error={touched.has('maxBookingsPerMonth') ? errors.maxBookingsPerMonth : undefined}
                />
              ) : null}
            </View>
          </Card>

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
  header: { paddingTop: Spacing.lg, paddingBottom: Spacing.md },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 4 },
  sectionTitle: {
    fontSize: FontSize.xs,
    color: Colors.textMuted,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    marginTop: Spacing.lg,
    marginBottom: Spacing.sm,
    paddingHorizontal: Spacing.xs,
    fontWeight: FontWeight.semibold,
  },
  switchRow: { flexDirection: 'row', alignItems: 'center' },
  rowLabel: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, color: Colors.text },
  rowHint: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 4, lineHeight: 16 },
  chipsRow: { flexDirection: 'row', flexWrap: 'wrap', gap: 8, marginTop: Spacing.sm },
  errorText: { color: Colors.error, fontSize: FontSize.xs, marginTop: 6, fontWeight: FontWeight.medium },
  unitText: { fontSize: FontSize.md, color: Colors.textMuted, fontWeight: FontWeight.semibold, paddingHorizontal: 4 },
  divider: { height: 1, backgroundColor: Colors.divider },
  noticeBox: {
    backgroundColor: Colors.warningSoft,
    borderRadius: Radius.md,
    padding: Spacing.md,
    borderWidth: 1,
    borderColor: Colors.warning,
  },
  noticeText: { fontSize: FontSize.xs, color: Colors.text, lineHeight: 18 },
  saveRow: { marginTop: Spacing.xl },
});
