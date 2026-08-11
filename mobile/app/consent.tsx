import { useState } from 'react';
import { View, Text, StyleSheet, ScrollView, Pressable, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useMutation } from '@tanstack/react-query';
import { Button } from '@/components/Button';
import { Badge } from '@/components/Badge';
import { Icon, IconName } from '@/components/Icon';
import { Colors, FontSize, FontWeight, LetterSpacing, Spacing, Radius, Layout } from '@/lib/theme';
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
          <View style={styles.iconBubble}>
            <Icon name="lock" size={28} color={Colors.ink} strokeWidth={1.6} />
          </View>
          <Text style={styles.heading}>Gizliliğin bizim için önemli</Text>
          <Text style={styles.subtitle}>
            Devam etmeden önce aşağıdaki metinleri incele ve Kullanım Koşulları'nı onayla.
          </Text>
        </View>

        <LegalLinkRow
          icon="shield"
          label="Kişisel Verilerin İşlenmesine İlişkin Aydınlatma Metni"
          onPress={() => router.push('/legal/aydinlatma' as never)}
        />
        <LegalLinkRow
          icon="document"
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
            label="Kampanya ve fırsatlardan haberdar olmak istiyorum."
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

function LegalLinkRow({ icon, label, onPress }: { icon: IconName; label: string; onPress: () => void }) {
  return (
    <Pressable
      onPress={onPress}
      style={({ pressed }) => [styles.linkRow, pressed && styles.linkRowPressed]}
      accessibilityRole="link"
      accessibilityLabel={label}
    >
      <Icon name={icon} size={18} color={Colors.textSecondary} strokeWidth={1.6} />
      <Text style={styles.linkText}>{label}</Text>
      <Icon name="chevronRight" size={18} color={Colors.textMuted} strokeWidth={1.6} />
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
        {checked ? <Icon name="check" size={14} color={Colors.textInverse} strokeWidth={2.4} /> : null}
      </View>
      <View style={styles.checkboxBody}>
        <Text style={styles.checkboxLabel}>{label}</Text>
        {/* Zorunlu/opsiyonel ayrımı rozetle NET ama kırmızı/uyarı rengi olmadan — korkutmadan bilgilendirir. */}
        <Badge
          label={required ? 'Zorunlu' : 'Opsiyonel'}
          tone={required ? 'brand' : 'neutral'}
          style={styles.checkboxBadge}
        />
      </View>
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
  iconBubble: {
    width: 64,
    height: 64,
    borderRadius: 32,
    backgroundColor: Colors.surfaceAlt,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: Spacing.md,
  },
  heading: {
    fontSize: FontSize.xl,
    fontWeight: FontWeight.bold,
    color: Colors.text,
    letterSpacing: LetterSpacing.title,
    textAlign: 'center',
  },
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
  linkText: { flex: 1, fontSize: FontSize.sm, color: Colors.text, fontWeight: FontWeight.medium },
  checkboxGroup: { marginTop: Spacing.lg, gap: Spacing.sm },
  checkboxRow: {
    flexDirection: 'row',
    alignItems: 'flex-start',
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
    marginTop: 2,
  },
  checkboxChecked: { backgroundColor: Colors.ink, borderColor: Colors.ink },
  checkboxBody: { flex: 1, gap: 6 },
  checkboxBadge: { alignSelf: 'flex-start' },
  checkboxLabel: { fontSize: FontSize.sm, color: Colors.text, lineHeight: 20 },
  errorText: {
    color: Colors.error,
    fontSize: FontSize.sm,
    textAlign: 'center',
    marginTop: Spacing.md,
  },
  actions: { marginTop: Spacing.xl, gap: Spacing.sm },
});
