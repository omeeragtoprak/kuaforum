import { useState } from 'react';
import { View, Text, StyleSheet, ScrollView, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useQuery, useMutation } from '@tanstack/react-query';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { EmptyState } from '@/components/EmptyState';
import { Colors, FontSize, FontWeight, Spacing, Layout } from '@/lib/theme';
import { BarberApi, KycDocumentRequest } from '@/api/barber';
import { extractApiError } from '@/api/client';

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
  const shopQuery = useQuery({ queryKey: ['my-shop'], queryFn: () => BarberApi.getMyShop() });

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
      Alert.alert(
        'KYC gönderildi',
        'Belgeleriniz admin onayına düştü. 1-2 iş günü içinde dönüş yapılacak.',
        [{ text: 'Tamam', onPress: () => router.back() }],
      );
    },
    onError: (err) => Alert.alert('Hata', extractApiError(err)),
  });

  const canSubmit = taxLicenseKey.trim().length > 0 && sanitaryPermitKey.trim().length > 0;

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
          label="📤 KYC'yi gönder"
          onPress={() => submitMut.mutate()}
          disabled={!canSubmit}
          loading={submitMut.isPending}
        />
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
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, lineHeight: 20, marginBottom: Spacing.sm },
  label: { fontSize: FontSize.sm, fontWeight: FontWeight.semibold, color: Colors.text, marginBottom: 6 },
  hint: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 4 },
  warningCard: { backgroundColor: '#FEF3C7', borderColor: '#FCD34D' },
  warningText: { fontSize: FontSize.xs, color: '#92400E', lineHeight: 18 },
});
