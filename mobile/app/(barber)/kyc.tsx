import { useState } from 'react';
import { View, Text, StyleSheet, ScrollView, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { EmptyState } from '@/components/EmptyState';
import { Colors, FontSize, FontWeight, Radius, Spacing, Layout } from '@/lib/theme';
import { BarberApi, KycDocumentRequest } from '@/api/barber';
import { extractApiError } from '@/api/client';
import { Haptics } from '@/lib/haptics';

/**
 * KYC submit form. Backend SubmitKycCommand requires ≥2 documents. We collect document
 * metadata + storage keys; **actual file upload is a placeholder** — real impl needs
 * Cloudflare R2 presigned PUT URL flow (M4-photo-upload, GAP §9).
 *
 * Until R2 is wired, this screen lets the founder/dev manually input pre-uploaded keys
 * for testing. Production path: file picker → presigned URL → R2 upload → submit keys.
 */
export default function BarberKycScreen() {
  const router = useRouter();
  const qc = useQueryClient();
  const shopQuery = useQuery({ queryKey: ['my-shop'], queryFn: () => BarberApi.getMyShop() });
  const kycStatusQuery = useQuery({
    queryKey: ['my-kyc-status'],
    queryFn: () => BarberApi.getMyKycStatus(),
    enabled: !!shopQuery.data?.id,
  });

  const [taxLicenseKey, setTaxLicenseKey] = useState('');
  const [sanitaryPermitKey, setSanitaryPermitKey] = useState('');

  const submitMut = useMutation({
    mutationFn: () => {
      if (!shopQuery.data?.id) throw new Error('Shop bilgisi yüklenemedi.');
      const docs: KycDocumentRequest[] = [
        {
          documentType: 'TaxLicense',
          fileName: 'tax-license.pdf',
          storageKey: taxLicenseKey.trim(),
          sizeBytes: 100_000,
          contentType: 'application/pdf',
        },
        {
          documentType: 'SanitaryPermit',
          fileName: 'sanitary-permit.pdf',
          storageKey: sanitaryPermitKey.trim(),
          sizeBytes: 100_000,
          contentType: 'application/pdf',
        },
      ];
      return BarberApi.submitKyc(shopQuery.data.id, docs);
    },
    onSuccess: () => {
      Haptics.success();
      qc.invalidateQueries({ queryKey: ['my-kyc-status'] });
      qc.invalidateQueries({ queryKey: ['my-shop'] });
      Alert.alert(
        'KYC gönderildi',
        'Belgeleriniz admin onayına düştü. 1-2 iş günü içinde dönüş yapılacak.',
        [{ text: 'Tamam', onPress: () => router.back() }],
      );
    },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });

  const canSubmit = taxLicenseKey.trim().length > 0 && sanitaryPermitKey.trim().length > 0;
  const kyc = kycStatusQuery.data;
  // Status comes as 0/1/2/3 number or "Pending"/"UnderReview"/"Approved"/"Rejected" string.
  const statusVal = kyc?.status;
  const isApproved = statusVal === 2 || statusVal === 'Approved';
  const isPending = statusVal === 0 || statusVal === 'Pending';
  const isUnderReview = statusVal === 1 || statusVal === 'UnderReview';
  const isRejected = statusVal === 3 || statusVal === 'Rejected';
  const formDisabled = isApproved || isPending || isUnderReview;

  if (!shopQuery.data && !shopQuery.isLoading) {
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <EmptyState
          emoji="🏪"
          title="Salon yok"
          description="Önce profil sekmesinden salon bilgilerini gir."
          actionLabel="Geri"
          onAction={() => router.back()}
        />
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScrollView contentContainerStyle={styles.scroll} showsVerticalScrollIndicator={false}>
        <Text style={styles.title}>KYC Doğrulama</Text>
        <Text style={styles.subtitle}>
          Yasal kuaför hizmeti sunabilmek için vergi levhası ve işyeri açma ruhsatı gerekli.
          Belgelerin admin tarafından onaylanması ortalama 1-2 iş günü sürer.
        </Text>

        {isApproved ? (
          <View style={styles.statusBoxOk}>
            <Text style={styles.statusTitleOk}>✅ KYC onaylandı</Text>
            <Text style={styles.statusBody}>
              İşletmen aktif. Müşteriler seni keşfette görebiliyor ve randevu alabiliyor.
              {kyc?.reviewedAt ? `\nOnaylanma: ${formatLocal(kyc.reviewedAt)}` : ''}
            </Text>
          </View>
        ) : null}

        {isPending || isUnderReview ? (
          <View style={styles.statusBoxInfo}>
            <Text style={styles.statusTitleInfo}>
              {isPending ? '⏳ Başvurun beklemede' : '👁 İncelemede'}
            </Text>
            <Text style={styles.statusBody}>
              {kyc?.documentCount} belge admin onayına gönderildi.
              {kyc?.submittedAt ? `\nGönderilme: ${formatLocal(kyc.submittedAt)}` : ''}
              {'\n\nGenelde 1-2 iş günü içinde sonuç alırsın. Onaylandığında bildirim gelir.'}
            </Text>
          </View>
        ) : null}

        {isRejected ? (
          <View style={styles.statusBoxError}>
            <Text style={styles.statusTitleError}>❌ Başvurun reddedildi</Text>
            {kyc?.rejectionReason ? (
              <View style={styles.reasonBox}>
                <Text style={styles.reasonLabel}>Admin notu:</Text>
                <Text style={styles.reasonText}>{kyc.rejectionReason}</Text>
              </View>
            ) : null}
            <Text style={styles.statusBody}>
              {kyc?.reviewedAt ? `Reddedilme: ${formatLocal(kyc.reviewedAt)}\n\n` : ''}
              Belgeleri tekrar yükleyip aşağıdaki formdan yeni bir başvuru gönderebilirsin.
            </Text>
          </View>
        ) : null}

        {formDisabled ? null : (
          <>
            <Card>
              <Text style={styles.label}>Vergi levhası (storage key)</Text>
              <Input
                value={taxLicenseKey}
                onChangeText={setTaxLicenseKey}
                placeholder="örn: kyc/2026/tax-abc123.pdf"
                autoCapitalize="none"
              />
              <Text style={styles.hint}>Belgenizi yüklediğinizde sistem size bir storage key verir.</Text>
            </Card>

            <Card>
              <Text style={styles.label}>İşyeri açma ruhsatı (storage key)</Text>
              <Input
                value={sanitaryPermitKey}
                onChangeText={setSanitaryPermitKey}
                placeholder="örn: kyc/2026/permit-xyz456.pdf"
                autoCapitalize="none"
              />
              <Text style={styles.hint}>Belediyeden alınmış sıhhî müsaade veya işyeri açma ruhsatı.</Text>
            </Card>

            <Card style={styles.warningCard}>
              <Text style={styles.warningText}>
                ⚠️ Dosya yükleme şu anda manuel girilen storage key ile çalışıyor (Cloudflare R2 entegrasyonu
                sonraki sürümde). Test için herhangi bir string girebilirsin.
              </Text>
            </Card>

            <Button
              label={isRejected ? '📤 Yeniden gönder' : '📤 KYC\'yi gönder'}
              onPress={() => submitMut.mutate()}
              disabled={!canSubmit}
              loading={submitMut.isPending}
            />
          </>
        )}
      </ScrollView>
    </SafeAreaView>
  );
}

function formatLocal(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleDateString('tr-TR', { day: '2-digit', month: 'short', year: 'numeric' })
    + ' · ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' });
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
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, lineHeight: 20, marginBottom: Spacing.sm },
  label: { fontSize: FontSize.sm, fontWeight: FontWeight.semibold, color: Colors.text, marginBottom: 6 },
  hint: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 4 },

  statusBoxOk: {
    backgroundColor: Colors.successSoft,
    borderRadius: Radius.md,
    padding: Spacing.lg,
    borderWidth: 1,
    borderColor: Colors.success,
  },
  statusTitleOk: { fontSize: FontSize.lg, fontWeight: FontWeight.bold, color: Colors.success },
  statusBoxInfo: {
    backgroundColor: Colors.infoSoft,
    borderRadius: Radius.md,
    padding: Spacing.lg,
    borderWidth: 1,
    borderColor: Colors.info,
  },
  statusTitleInfo: { fontSize: FontSize.lg, fontWeight: FontWeight.bold, color: Colors.info },
  statusBoxError: {
    backgroundColor: Colors.errorSoft,
    borderRadius: Radius.md,
    padding: Spacing.lg,
    borderWidth: 1,
    borderColor: Colors.error,
  },
  statusTitleError: { fontSize: FontSize.lg, fontWeight: FontWeight.bold, color: Colors.error },
  statusBody: {
    fontSize: FontSize.sm,
    color: Colors.text,
    marginTop: Spacing.sm,
    lineHeight: 20,
  },
  reasonBox: {
    backgroundColor: Colors.surface,
    borderRadius: Radius.sm,
    padding: Spacing.md,
    marginTop: Spacing.sm,
    borderLeftWidth: 3,
    borderLeftColor: Colors.error,
  },
  reasonLabel: {
    fontSize: FontSize.xs,
    color: Colors.error,
    fontWeight: FontWeight.bold,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
  },
  reasonText: { fontSize: FontSize.sm, color: Colors.text, marginTop: 4, lineHeight: 20 },
  warningCard: { backgroundColor: '#FEF3C7', borderColor: '#FCD34D' },
  warningText: { fontSize: FontSize.xs, color: '#92400E', lineHeight: 18 },
});
