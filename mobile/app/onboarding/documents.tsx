import { useRef, useState, useEffect } from 'react';
import { View, Text, StyleSheet, ScrollView, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/Button';
import { EmptyState } from '@/components/EmptyState';
import { Icon } from '@/components/Icon';
import { OnboardingHeader } from '@/components/onboarding/OnboardingHeader';
import { KycDocumentSection } from '@/components/kyc/KycDocumentSection';
import { UploadedKycDoc } from '@/components/kyc/KycDocumentPicker';
import { Colors, FontSize, FontWeight, LetterSpacing, Spacing, Layout, Radius } from '@/lib/theme';
import { BarberApi, KycDocumentRequest } from '@/api/barber';
import { extractApiError } from '@/api/client';
import { Haptics } from '@/lib/haptics';
import { canSubmitKyc } from '@/lib/kyc';
import { useOnboardingStore } from '@/store/onboarding-store';

/**
 * Adım 3 — KYC belgeleri (Vergi Levhası + Kimlik zorunlu). Başarılı submit sonrası
 * sihirbaz kapanır, store sıfırlanır ve owner doğrudan panele (Bugün sekmesi) düşer —
 * KYC sonucu bildirimle gelecek (bkz. (barber)/kyc.tsx durum ekranı, oradan da takip edilebilir).
 */
export default function OnboardingDocumentsScreen() {
  const router = useRouter();
  const qc = useQueryClient();
  const shopId = useOnboardingStore((s) => s.shopId);
  const reset = useOnboardingStore((s) => s.reset);

  const mountedRef = useRef(true);
  useEffect(() => () => { mountedRef.current = false; }, []);

  const [docs, setDocs] = useState<UploadedKycDoc[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [done, setDone] = useState(false);

  const canSubmit = canSubmitKyc(docs);

  async function handleSubmit() {
    if (!canSubmit || submitting || !shopId) return;
    setSubmitting(true);
    try {
      const documents: KycDocumentRequest[] = docs.map((d) => ({
        documentType: d.documentType,
        fileName: d.fileName,
        storageKey: d.storageKey,
        sizeBytes: d.sizeBytes,
        contentType: d.contentType,
      }));
      await BarberApi.submitKyc(shopId, documents);
      if (!mountedRef.current) return;
      Haptics.success();
      qc.invalidateQueries({ queryKey: ['my-shop'] });
      qc.invalidateQueries({ queryKey: ['my-kyc-status'] });
      setDone(true);
    } catch (err) {
      if (!mountedRef.current) return;
      Haptics.error();
      Alert.alert('Hata', extractApiError(err));
    } finally {
      if (mountedRef.current) setSubmitting(false);
    }
  }

  function handleGoToPanel() {
    reset();
    router.replace('/(barber)/today');
  }

  if (!shopId) {
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <EmptyState
          icon="store"
          title="Önce salon bilgisi gerekli"
          description="Belge yüklemeden önce salon adını ve adresini tamamlaman gerekiyor."
          actionLabel="Adım 1'e git"
          onAction={() => router.replace('/onboarding/shop' as never)}
        />
      </SafeAreaView>
    );
  }

  if (done) {
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <View style={styles.successCenter}>
          <View style={styles.successIconWrap}>
            <Icon name="check" size={30} color={Colors.success} strokeWidth={2} />
          </View>
          <Text style={styles.successTitle}>Başvurun incelemede</Text>
          <Text style={styles.successBody}>
            Belgelerin admin onayına gönderildi. Sonuç 1-2 iş günü içinde bildirimle gelecek.
            Onaylanana kadar panelden salon bilgilerini düzenleyebilirsin.
          </Text>
          <View style={styles.successBtn}>
            <Button label="Panele git" onPress={handleGoToPanel} size="lg" />
          </View>
        </View>
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScrollView contentContainerStyle={styles.scroll} showsVerticalScrollIndicator={false}>
        <OnboardingHeader
          step={3}
          onBack={() => router.back()}
          title="Belgelerini yükle"
          subtitle="Vergi levhası ve kimlik zorunlu. Onay ortalama 1-2 iş günü sürer."
        />

        <KycDocumentSection docs={docs} onChange={setDocs} />

        {!canSubmit ? <Text style={styles.hint}>Vergi Levhası ve Kimlik zorunludur.</Text> : null}

        <View style={styles.submitBtn}>
          <Button label="Başvuruyu gönder" onPress={handleSubmit} disabled={!canSubmit} loading={submitting} size="lg" />
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  scroll: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingTop: Spacing.lg,
    paddingBottom: Spacing.xxl,
    gap: Spacing.md,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  hint: { fontSize: FontSize.xs, color: Colors.textMuted },
  submitBtn: { marginTop: Spacing.sm },
  successCenter: { flex: 1, alignItems: 'center', justifyContent: 'center', paddingHorizontal: Layout.screenPaddingX, gap: Spacing.sm },
  successIconWrap: {
    width: 72,
    height: 72,
    borderRadius: Radius.full,
    backgroundColor: Colors.successSoft,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: Spacing.sm,
  },
  successTitle: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text, textAlign: 'center', letterSpacing: LetterSpacing.title },
  successBody: { fontSize: FontSize.sm, color: Colors.textSecondary, textAlign: 'center', lineHeight: 20, maxWidth: 340 },
  successBtn: { width: '100%', marginTop: Spacing.lg, borderRadius: Radius.md },
});
