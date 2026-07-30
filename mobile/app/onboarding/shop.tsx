import { useEffect, useRef, useState } from 'react';
import { View, Text, StyleSheet, Pressable, Alert, KeyboardAvoidingView, Platform, ScrollView, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { EmptyState } from '@/components/EmptyState';
import { OnboardingHeader } from '@/components/onboarding/OnboardingHeader';
import { Colors, FontSize, FontWeight, Spacing, Layout, Radius } from '@/lib/theme';
import { BarberApi, BarberShop } from '@/api/barber';
import { extractApiError, refreshTokens } from '@/api/client';
import { Haptics } from '@/lib/haptics';
import { formatPhoneTr, phoneToE164 } from '@/lib/format';
import { isValidShopName, resolveOnboardingStep } from '@/lib/onboarding';
import { useOnboardingStore, OnboardingGenderType } from '@/store/onboarding-store';
import { useAuthStore } from '@/store/auth-store';

const GENDER_OPTIONS: { value: OnboardingGenderType; title: string; detail: string }[] = [
  { value: 'Unisex', title: 'Unisex', detail: 'Kadın ve erkek müşteriye hizmet' },
  { value: 'FemaleOnly', title: 'Sadece Kadın', detail: 'Yalnız kadın müşteriye hizmet' },
  { value: 'MaleOnly', title: 'Sadece Erkek', detail: 'Yalnız erkek müşteriye hizmet' },
];

/**
 * Adım 1 — salon adı, hizmet segmenti, opsiyonel işletme telefonu.
 * "Devam" → createShop (Customer→BarberOwner atomik terfi) → TOKEN REFRESH
 * (eski token'ın role claim'i bayat, refresh olmadan adım 2 kesin 403 alır) → hydrate.
 */
export default function OnboardingShopScreen() {
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const isCustomer = useAuthStore((s) => s.isCustomer());

  const shopName = useOnboardingStore((s) => s.shopName);
  const genderType = useOnboardingStore((s) => s.genderType);
  const phone = useOnboardingStore((s) => s.phone);
  const shopId = useOnboardingStore((s) => s.shopId);
  const setField = useOnboardingStore((s) => s.setField);
  const setShopId = useOnboardingStore((s) => s.setShopId);

  const [submitting, setSubmitting] = useState(false);
  // Customer için gerek yok (her zaman adım 1) — yalnız zaten BarberOwner/Stylist
  // olup bu ekrana geri dönenler için "kaldığı yerden devam" kontrolü.
  const [resuming, setResuming] = useState(!isCustomer);
  const [blocked, setBlocked] = useState(false);
  const mountedRef = useRef(true);
  useEffect(() => () => { mountedRef.current = false; }, []);

  useEffect(() => {
    if (isCustomer) return;
    if (user?.role === 'Stylist') {
      setBlocked(true);
      setResuming(false);
      return;
    }
    (async () => {
      const shop = await BarberApi.getMyShop();
      if (!mountedRef.current) return;
      routeForExistingShop(shop);
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function routeForExistingShop(shop: BarberShop | null) {
    const step = resolveOnboardingStep(shop);
    if (step === 1) {
      setResuming(false);
      return;
    }
    if (shop) setShopId(shop.id);
    if (step === 2) router.replace('/onboarding/address' as never);
    else if (step === 3) router.replace('/onboarding/documents' as never);
    else router.replace('/(barber)/kyc');
  }

  const nameValid = isValidShopName(shopName);
  const formValid = nameValid;

  async function refreshRoleThenGoToAddress() {
    setSubmitting(true);
    const ok = await refreshTokens();
    if (!mountedRef.current) return;
    if (!ok) {
      setSubmitting(false);
      Alert.alert(
        'Oturum yenilenemedi',
        'Salonun oluşturuldu ama yeni yetki bilgisi alınamadı. Şimdi devam edersen izin hatası alırsın — tekrar dene.',
        [{ text: 'Tekrar dene', onPress: refreshRoleThenGoToAddress }],
      );
      return;
    }
    await useAuthStore.getState().hydrate();
    if (!mountedRef.current) return;
    setSubmitting(false);
    router.push('/onboarding/address' as never);
  }

  async function handleContinue() {
    if (!formValid || submitting) return;

    if (shopId) {
      // Zaten oluşturulmuş (ör. önceki denemede refresh başarısız oldu) — tekrar create çağırma.
      await refreshRoleThenGoToAddress();
      return;
    }

    setSubmitting(true);
    try {
      const res = await BarberApi.createShop({
        name: shopName.trim(),
        genderType,
        phone: phone.trim() ? phoneToE164(phone) : undefined,
      });
      if (!mountedRef.current) return;
      setShopId(res.id);
      Haptics.success();
      await refreshRoleThenGoToAddress();
    } catch (err) {
      if (!mountedRef.current) return;
      const status = (err as { response?: { status?: number } })?.response?.status;
      if (status === 409) {
        // Zaten bir işletmesi var — kaldığı adıma yönlendir (idempotent devam).
        const shop = await BarberApi.getMyShop();
        if (!mountedRef.current) return;
        setSubmitting(false);
        if (!shop) {
          // 409 ama işletme yok (ör. slug çakışması) — sessiz kalma, gerçek hatayı göster.
          Haptics.error();
          Alert.alert('Hata', extractApiError(err));
          return;
        }
        routeForExistingShop(shop);
        return;
      }
      setSubmitting(false);
      Haptics.error();
      Alert.alert('Hata', extractApiError(err));
    }
  }

  if (blocked) {
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <EmptyState
          emoji="🚫"
          title="Bu özellik salon sahipleri için"
          description="Stilist hesabınla yeni bir salon oluşturamazsın. Salon sahibinin seni ekleyeceği daveti bekle."
          actionLabel="Geri"
          onAction={() => router.back()}
        />
      </SafeAreaView>
    );
  }

  if (resuming) {
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <View style={styles.loadingCenter}>
          <ActivityIndicator size="large" color={Colors.primary} />
          <Text style={styles.loadingText}>Salon bilgin kontrol ediliyor…</Text>
        </View>
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} style={styles.flex}>
        <ScrollView contentContainerStyle={styles.scroll} showsVerticalScrollIndicator={false} keyboardShouldPersistTaps="handled">
          <OnboardingHeader
            step={1}
            onBack={() => router.back()}
            title="Salonunu tanıt"
            subtitle="Müşterilerin seni keşfette bu bilgilerle görecek."
          />

          <Input
            label="Salon adı"
            required
            value={shopName}
            onChangeText={(v) => setField('shopName', v)}
            placeholder="Örn: Ahmet Erkek Kuaförü"
            maxLength={120}
            hint={nameValid || shopName.length === 0 ? undefined : 'En az 2 karakter olmalı'}
            error={shopName.length > 0 && !nameValid ? 'En az 2, en fazla 120 karakter olmalı' : undefined}
            autoCapitalize="words"
          />

          <View style={styles.section}>
            <Text style={styles.sectionLabel}>Hizmet segmenti *</Text>
            <View accessibilityRole="radiogroup" style={styles.radioGroup}>
              {GENDER_OPTIONS.map((opt) => (
                <GenderOption
                  key={opt.value}
                  selected={genderType === opt.value}
                  onPress={() => setField('genderType', opt.value)}
                  title={opt.title}
                  detail={opt.detail}
                />
              ))}
            </View>
          </View>

          <Input
            label="İşletme telefonu (opsiyonel)"
            value={phone}
            onChangeText={(v) => setField('phone', formatPhoneTr(v))}
            placeholder="+90 555 123 45 67"
            keyboardType="phone-pad"
            autoComplete="tel"
            hint="Müşteriler bu numaradan sana ulaşabilir"
          />

          <Button
            label="Devam"
            onPress={handleContinue}
            loading={submitting}
            disabled={!formValid}
            size="lg"
          />
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

function GenderOption({ selected, onPress, title, detail }: { selected: boolean; onPress: () => void; title: string; detail: string }) {
  return (
    <Pressable
      onPress={onPress}
      accessible
      accessibilityRole="radio"
      accessibilityState={{ checked: selected, selected }}
      accessibilityLabel={`${title}. ${detail}`}
      style={({ pressed }) => [styles.genderOption, selected && styles.genderOptionSelected, pressed && styles.pressed]}
    >
      <Text style={[styles.radioMark, selected && styles.radioMarkSelected]}>{selected ? '●' : '○'}</Text>
      <View style={{ flex: 1 }}>
        <Text style={styles.genderTitle}>{title}</Text>
        <Text style={styles.genderDetail}>{detail}</Text>
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  flex: { flex: 1 },
  scroll: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingTop: Spacing.lg,
    paddingBottom: Spacing.xxl,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  loadingCenter: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: Spacing.md },
  loadingText: { fontSize: FontSize.sm, color: Colors.textSecondary },
  section: { marginBottom: Spacing.lg },
  sectionLabel: { fontSize: FontSize.sm, color: Colors.textSecondary, marginBottom: Spacing.sm, fontWeight: FontWeight.medium },
  radioGroup: { gap: Spacing.sm },
  genderOption: {
    flexDirection: 'row', alignItems: 'center', gap: Spacing.md,
    borderWidth: 1.5, borderColor: Colors.border, borderRadius: Radius.md,
    padding: Spacing.md, backgroundColor: Colors.surface, minHeight: Layout.tapTargetMin,
  },
  genderOptionSelected: { borderColor: Colors.primary, backgroundColor: Colors.primarySoft },
  pressed: { opacity: 0.9 },
  radioMark: { fontSize: 18, color: Colors.textMuted, width: 20, textAlign: 'center' },
  radioMarkSelected: { color: Colors.primary },
  genderTitle: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, color: Colors.text },
  genderDetail: { fontSize: FontSize.xs, color: Colors.textSecondary, marginTop: 2 },
});
