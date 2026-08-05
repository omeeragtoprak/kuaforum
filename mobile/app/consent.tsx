import { useState } from 'react';
import { View, Text, StyleSheet, ScrollView, Pressable, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useMutation } from '@tanstack/react-query';
import { Button } from '@/components/Button';
import { Colors, FontSize, FontWeight, Spacing, Radius, Layout } from '@/lib/theme';
import { AuthApi } from '@/api/auth';
import { extractApiError } from '@/api/client';
import { useAuthStore } from '@/store/auth-store';
import { resolvePostAuthRoute, sanitizeRedirectTo } from '@/lib/consent-gate';
import { Haptics } from '@/lib/haptics';

/**
 * RFC-008 — OTP-sonrası / mevcut kullanıcının ilk açılışındaki tek seferlik KVKK rıza
 * ekranı. `app/_layout.tsx`'teki AuthGate `user.requiresTosConsent` true olduğu sürece
 * kullanıcıyı her rotadan buraya geri döndürür (`/legal/*` hariç) — bu ekranda "geri"
 * yok, bilerek: onaylamadan (ya da çıkış yapmadan) uygulamaya geçilemez (KVKK m.5).
 */
export default function ConsentScreen() {
  const router = useRouter();
  // RFC-010 — misafir randevu kapısı: requiresTosConsent true olan kullanıcı önce
  // buraya düşer (bkz. resolvePostAuthRoute); redirectTo query'de taşınmış olabilir,
  // onaylayınca oraya (seçimler korunarak) devam edilir.
  const { redirectTo } = useLocalSearchParams<{ redirectTo?: string }>();
  const signOut = useAuthStore((s) => s.signOut);

  const [tosAccepted, setTosAccepted] = useState(false);
  const [marketingAccepted, setMarketingAccepted] = useState(false);
  const [signingOut, setSigningOut] = useState(false);

  const submitMut = useMutation({
    mutationFn: async () => {
      await AuthApi.grantInitialConsents(marketingAccepted);
      await useAuthStore.getState().hydrate();
    },
    onSuccess: () => {
      Haptics.success();
      const user = useAuthStore.getState().user;
      // I-1 (güvenlik review) — `user` null fallback dalı resolvePostAuthRoute'tan
      // GEÇMİYOR, o yüzden open-redirect sanitizer'ı burada ayrıca uygulanır.
      router.replace(
        (user ? resolvePostAuthRoute(user, redirectTo) : (sanitizeRedirectTo(redirectTo) ?? '/(tabs)')) as never,
      );
    },
    onError: () => {
      Haptics.error();
    },
  });

  function handleDecline() {
    Alert.alert(
      'Rıza vermeden devam edilemez',
      'Kullanım Koşulları onayı olmadan uygulamayı kullanamazsın. Şimdi çıkış yapmak istiyor musun?',
      [
        { text: 'Vazgeç', style: 'cancel' },
        {
          text: 'Çıkış yap',
          style: 'destructive',
          onPress: async () => {
            setSigningOut(true);
            await signOut();
            router.replace('/(auth)/login');
          },
        },
      ],
    );
  }

  return (
    <SafeAreaView style={styles.container} edges={['top', 'bottom']}>
      <ScrollView contentContainerStyle={styles.scroll} showsVerticalScrollIndicator={false}>
        <View style={styles.header}>
          <Text style={styles.emoji}>🔐</Text>
          <Text style={styles.heading}>Gizliliğin bizim için önemli</Text>
          <Text style={styles.subtitle}>
            Devam etmeden önce aşağıdaki metinleri incele ve Kullanım Koşulları'nı onayla.
          </Text>
        </View>

        <LegalLinkRow
          icon="📄"
          label="Kişisel Verilerin İşlenmesine İlişkin Aydınlatma Metni"
          onPress={() => router.push('/legal/aydinlatma' as never)}
        />
        <LegalLinkRow
          icon="📜"
          label="Kullanım Koşulları"
          onPress={() => router.push('/legal/kullanim-kosullari' as never)}
        />

        <View style={styles.checkboxGroup}>
          <ConsentCheckboxRow
            checked={tosAccepted}
            onToggle={() => setTosAccepted((v) => !v)}
            label="Kullanım Koşulları'nı okudum, kabul ediyorum."
            required
            testID="consent-tos-checkbox"
          />
          <ConsentCheckboxRow
            checked={marketingAccepted}
            onToggle={() => setMarketingAccepted((v) => !v)}
            label="Kampanya ve fırsatlardan haberdar olmak istiyorum (opsiyonel)."
            testID="consent-marketing-checkbox"
          />
        </View>

        {submitMut.isError ? (
          <Text style={styles.errorText}>{extractApiError(submitMut.error)}</Text>
        ) : null}

        <View style={styles.actions}>
          <Button
            label="Onayla ve devam et"
            onPress={() => submitMut.mutate()}
            loading={submitMut.isPending}
            disabled={!tosAccepted}
            size="lg"
          />
          <Button
            label="Vazgeç (çıkış yap)"
            variant="ghost"
            onPress={handleDecline}
            loading={signingOut}
            disabled={submitMut.isPending}
          />
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

function LegalLinkRow({ icon, label, onPress }: { icon: string; label: string; onPress: () => void }) {
  return (
    <Pressable
      onPress={onPress}
      style={({ pressed }) => [styles.linkRow, pressed && styles.linkRowPressed]}
      accessibilityRole="link"
      accessibilityLabel={label}
    >
      <Text style={styles.linkIcon}>{icon}</Text>
      <Text style={styles.linkText}>{label}</Text>
      <Text style={styles.chevron}>›</Text>
    </Pressable>
  );
}

function ConsentCheckboxRow({
  checked,
  onToggle,
  label,
  required,
  testID,
}: {
  checked: boolean;
  onToggle: () => void;
  label: string;
  required?: boolean;
  testID?: string;
}) {
  return (
    <Pressable
      onPress={onToggle}
      style={styles.checkboxRow}
      accessibilityRole="checkbox"
      accessibilityState={{ checked }}
      // Görünen metin değişmeden screen reader'a zorunluluk bilgisi ver (a11y — RFC-008 K2 review M-2).
      accessibilityLabel={required ? `${label} (zorunlu)` : label}
      testID={testID}
      hitSlop={8}
    >
      <View style={[styles.checkbox, checked && styles.checkboxChecked]}>
        {checked ? <Text style={styles.checkboxMark}>✓</Text> : null}
      </View>
      <Text style={styles.checkboxLabel}>
        {label}
        {required ? <Text style={styles.required}> *</Text> : null}
      </Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  scroll: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingTop: Spacing.lg,
    paddingBottom: Spacing.xxl,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
    gap: Spacing.sm,
  },
  header: { alignItems: 'center', marginBottom: Spacing.lg },
  emoji: { fontSize: 48, marginBottom: Spacing.sm },
  heading: { fontSize: FontSize.xl, fontWeight: FontWeight.bold, color: Colors.text, textAlign: 'center' },
  subtitle: {
    fontSize: FontSize.sm,
    color: Colors.textSecondary,
    textAlign: 'center',
    marginTop: 6,
    lineHeight: 20,
  },
  linkRow: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: Colors.surface,
    borderRadius: Radius.md,
    borderWidth: 1,
    borderColor: Colors.border,
    paddingHorizontal: Spacing.md,
    minHeight: Layout.tapTargetMin,
    gap: Spacing.sm,
  },
  linkRowPressed: { backgroundColor: Colors.surfaceAlt },
  linkIcon: { fontSize: 18 },
  linkText: { flex: 1, fontSize: FontSize.sm, color: Colors.text, fontWeight: FontWeight.medium },
  chevron: { fontSize: 20, color: Colors.textMuted },
  checkboxGroup: { marginTop: Spacing.lg, gap: Spacing.xs },
  checkboxRow: {
    flexDirection: 'row',
    alignItems: 'center',
    minHeight: Layout.tapTargetMin,
    paddingVertical: Spacing.sm,
    gap: Spacing.md,
  },
  checkbox: {
    width: 24,
    height: 24,
    borderRadius: 6,
    borderWidth: 2,
    borderColor: Colors.borderStrong,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: Colors.surface,
  },
  checkboxChecked: { backgroundColor: Colors.primary, borderColor: Colors.primary },
  checkboxMark: { color: Colors.textInverse, fontSize: 15, fontWeight: FontWeight.bold },
  checkboxLabel: { flex: 1, fontSize: FontSize.sm, color: Colors.text, lineHeight: 20 },
  required: { color: Colors.error },
  errorText: {
    color: Colors.error,
    fontSize: FontSize.sm,
    textAlign: 'center',
    marginTop: Spacing.md,
  },
  actions: { marginTop: Spacing.xl, gap: Spacing.sm },
});
