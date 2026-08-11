import { View, Text, StyleSheet } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Button } from './Button';
import { Icon } from './Icon';
import { Colors, Radius, FontSize, FontWeight, Spacing, Layout } from '@/lib/theme';

type AuthPromptProps = {
  /** Neden giriş gerektiğini açıklayan, ekrana özel metin (örn. "Randevu almak için..."). */
  description: string;
  /**
   * Login/OTP/(varsa) consent zincirinden sonra dönülecek path+query — örn.
   * `/booking/confirm?stylistId=...`. `buildRedirectPath` (consent-gate.ts) ile üretilir.
   * Verilmezse başarılı girişten sonra kullanıcı rol-bazlı varsayılan rotaya gider.
   */
  redirectTo?: string;
  title?: string;
};

/**
 * RFC-010 — misafir (unauthenticated) kullanıcı korumalı bir sekmeye/ekrana ulaştığında
 * (randevularım, bildirimler, profil sekmesi, randevu onay ekranı) global bir redirect'le
 * login'e fırlatılmak yerine bu kart gösterilir. "Giriş yap / Kayıt ol" login ekranına
 * geçer; `redirectTo` doluysa login → OTP → (varsa) consent zincirinin sonunda kullanıcı
 * BURAYA (seçimleri korunarak) geri döner — bkz. `resolvePostAuthRoute` (consent-gate.ts).
 */
export function AuthPrompt({ description, redirectTo, title = 'Giriş yapman gerekiyor' }: AuthPromptProps) {
  const router = useRouter();

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <View style={styles.card} accessible accessibilityLabel={`${title}. ${description}`}>
        <View style={styles.iconWrap}>
          <Icon name="lock" size={30} color={Colors.textSecondary} strokeWidth={1.5} />
        </View>
        <Text style={styles.title}>{title}</Text>
        <Text style={styles.description}>{description}</Text>
        <Button
          label="Giriş yap / Kayıt ol"
          onPress={() =>
            router.push({
              pathname: '/(auth)/login',
              params: redirectTo ? { redirectTo } : undefined,
            })
          }
          size="lg"
        />
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  card: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: Layout.screenPaddingX,
    gap: Spacing.sm,
  },
  iconWrap: {
    width: 72,
    height: 72,
    borderRadius: Radius.full,
    backgroundColor: Colors.surfaceAlt,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: Spacing.sm,
  },
  title: { fontSize: FontSize.xl, fontWeight: FontWeight.bold, color: Colors.text, textAlign: 'center' },
  description: {
    fontSize: FontSize.sm,
    color: Colors.textSecondary,
    textAlign: 'center',
    lineHeight: 20,
    maxWidth: 320,
    marginBottom: Spacing.md,
  },
});
