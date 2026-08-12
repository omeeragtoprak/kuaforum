import { useState } from 'react';
import { View, Text, StyleSheet, KeyboardAvoidingView, Platform, Alert, Pressable } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { Screen } from '@/components/Screen';
import { Icon } from '@/components/Icon';
import { RoleQuickLogin, DemoRole } from '@/components/RoleQuickLogin';
import { Colors, FontSize, FontWeight, Layout, LetterSpacing, Radius, Spacing } from '@/lib/theme';
import { AuthApi } from '@/api/auth';
import { extractApiError, getApiBaseUrl } from '@/api/client';
import { useAuthStore } from '@/store/auth-store';
import { resolvePostAuthRoute, toHref } from '@/lib/consent-gate';
import { formatPhoneTr, isValidTrMobile, phoneToE164 } from '@/lib/format';
import { isTestBuild } from '@/lib/runtime-base-url';

export default function LoginScreen() {
  const router = useRouter();
  // RFC-010 — misafir randevu kapısı: AuthPrompt buraya `redirectTo` ile push eder
  // (örn. `/booking/confirm?stylistId=...`); OTP doğrulama sonrası aynen taşınır.
  const { redirectTo } = useLocalSearchParams<{ redirectTo?: string }>();

  const [phone, setPhone] = useState('+90 ');
  const [loading, setLoading] = useState(false);
  const [quickRole, setQuickRole] = useState<DemoRole | null>(null);

  const isValid = isValidTrMobile(phone);

  // Test kolaylığı: normal/store build'de (env yok) hızlı-giriş GİZLİ kalır — mevcut
  // güvenlik davranışı korunur. Yalnız `EXPO_PUBLIC_ENABLE_TEST_LOGIN=1` ile prebuild
  // edilen "test APK"sında görünür (bkz. builds/README.md). __DEV__ zaten Expo Go /
  // `expo start` geliştirme modunda true olduğu için oradaki davranış değişmiyor.
  const showQuickLogin = __DEV__ || process.env.EXPO_PUBLIC_ENABLE_TEST_LOGIN === '1';
  // Sunucu adresi değiştirme satırı — hızlı-giriş butonlarından DAHA DAR bir kapsam:
  // yalnız gerçek test build (EXPO_PUBLIC_ENABLE_TEST_LOGIN=1), __DEV__ dahil değil.
  // Neden: bu özellik özellikle derlenmiş APK'nın LAN IP değişince ölmesi sorununu
  // çözmek için var; Expo Go'daki geliştirici zaten .env'i elle değiştirip reload atar.
  const testBuildActive = isTestBuild();

  async function handleSendOtp() {
    if (!isValid) {
      Alert.alert('Geçersiz numara', 'Lütfen 5 ile başlayan 10 haneli mobil numaranı gir.');
      return;
    }
    setLoading(true);
    try {
      const e164 = phoneToE164(phone);
      const res = await AuthApi.sendOtp(e164, 'Login');
      router.push({
        pathname: '/(auth)/otp',
        params: { otpId: res.otpId, phone: e164, ...(redirectTo ? { redirectTo } : {}) },
      });
    } catch (err) {
      Alert.alert('Hata', extractApiError(err));
    } finally {
      setLoading(false);
    }
  }

  async function handleQuickLogin(role: DemoRole) {
    setQuickRole(role);
    try {
      await AuthApi.devLogin(role);
      // RFC-008 — dev-login response'unda requiresTosConsent yok; hydrate() taze
      // profili çeker, aynı gate helper (AuthGate'in de kullandığı) hedefi belirler.
      await useAuthStore.getState().hydrate();
      const freshUser = useAuthStore.getState().user;
      if (freshUser) router.replace(toHref(resolvePostAuthRoute(freshUser, redirectTo)) as never);
    } catch (err) {
      Alert.alert('Hızlı giriş başarısız', extractApiError(err));
    } finally {
      setQuickRole(null);
    }
  }

  // RFC-010 karar 1 — misafir gezinme: login zorunlu değil, "şimdilik gez" ile
  // keşfete (root) dönülebilir. `redirectTo` burada BİLİNÇLİ olarak yok sayılır —
  // kullanıcı girişi erteliyor, randevu seçimini de erteliyor demektir.
  function handleContinueAsGuest() {
    router.replace('/(tabs)' as never);
  }

  return (
    <Screen scroll edges={['top', 'bottom']}>
      <KeyboardAvoidingView
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
        style={styles.flex}
      >
        <View style={styles.brand}>
          <Text style={styles.logo}>Kuaforum</Text>
          <Text style={styles.tagline}>Türkiye'nin kuaför pazaryeri</Text>
        </View>

        {showQuickLogin && (
          <>
            <View style={styles.section}>
              <View style={styles.testBuildBanner} accessibilityRole="text">
                <Icon name="alert" size={14} color={Colors.textSecondary} strokeWidth={1.6} />
                <Text style={styles.testBuildBannerText}>
                  TEST BUILD — bu ekran yalnız iç test için. Gerçek kullanıcıya dağıtma.
                </Text>
              </View>

              {testBuildActive && (
                <View style={styles.serverRow}>
                  <Text style={styles.serverRowText} numberOfLines={1}>
                    Sunucu: {getApiBaseUrl()}
                  </Text>
                  <Pressable
                    onPress={() => router.push('/(auth)/server-settings' as never)}
                    style={styles.serverChangeBtn}
                    accessibilityRole="button"
                    accessibilityLabel="Sunucu adresini değiştir"
                  >
                    <Text style={styles.serverChangeBtnText}>Değiştir</Text>
                  </Pressable>
                </View>
              )}

              <Text style={styles.sectionTitle}>Hızlı Giriş (Demo)</Text>
              <Text style={styles.sectionHint}>
                Geliştirme modunda — tek dokunuşla rol bazında giriş yap.
              </Text>
              <View style={styles.quickStack}>
                <RoleQuickLogin
                  role="Customer"
                  loading={quickRole === 'Customer'}
                  onPress={() => handleQuickLogin('Customer')}
                />
                <RoleQuickLogin
                  role="CustomerFresh"
                  loading={quickRole === 'CustomerFresh'}
                  onPress={() => handleQuickLogin('CustomerFresh')}
                />
                <RoleQuickLogin
                  role="BarberOwner"
                  loading={quickRole === 'BarberOwner'}
                  onPress={() => handleQuickLogin('BarberOwner')}
                />
                <RoleQuickLogin
                  role="BarberOwnerDraft"
                  loading={quickRole === 'BarberOwnerDraft'}
                  onPress={() => handleQuickLogin('BarberOwnerDraft')}
                />
                <RoleQuickLogin
                  role="Admin"
                  loading={quickRole === 'Admin'}
                  onPress={() => handleQuickLogin('Admin')}
                />
              </View>
            </View>

            <View style={styles.dividerWrap}>
              <View style={styles.dividerLine} />
              <Text style={styles.dividerText}>veya telefonla giriş</Text>
              <View style={styles.dividerLine} />
            </View>
          </>
        )}

        <View style={styles.section}>
          <Input
            label="Telefon numaran"
            required
            value={phone}
            onChangeText={(text) => setPhone(formatPhoneTr(text))}
            keyboardType="phone-pad"
            placeholder="+90 555 123 45 67"
            autoComplete="tel"
            maxLength={20}
            hint="SMS ile 6 haneli doğrulama kodu göndereceğiz."
            accessibilityLabel="Telefon numarası"
          />

          <Button
            label="Kod gönder"
            onPress={handleSendOtp}
            loading={loading}
            disabled={!isValid}
            size="lg"
          />

          <Text style={styles.legal}>
            <Text
              style={styles.legalLink}
              onPress={() => router.push('/legal/kullanim-kosullari' as never)}
              accessibilityRole="link"
              accessibilityLabel="Kullanım Koşulları"
            >
              Kullanım Koşulları
            </Text>{' '}
            ve{' '}
            <Text
              style={styles.legalLink}
              onPress={() => router.push('/legal/aydinlatma' as never)}
              accessibilityRole="link"
              accessibilityLabel="KVKK Aydınlatma Metni"
            >
              KVKK Aydınlatma Metni
            </Text>
            'ni buradan okuyabilirsin. Giriş sonrası onayın alınacak.
          </Text>

          <Pressable
            onPress={handleContinueAsGuest}
            style={styles.guestLink}
            accessibilityRole="button"
            accessibilityLabel="Misafir olarak devam et, keşfet sekmesine dön"
          >
            <Text style={styles.guestLinkText}>Şimdilik gez</Text>
          </Pressable>
        </View>
      </KeyboardAvoidingView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  brand: { alignItems: 'center', marginTop: Spacing.xl, marginBottom: Spacing.xl },
  logo: {
    fontSize: FontSize.display,
    fontWeight: FontWeight.bold,
    color: Colors.ink,
    letterSpacing: LetterSpacing.display,
  },
  tagline: { fontSize: FontSize.md, color: Colors.textSecondary, marginTop: 4 },
  section: { marginBottom: Spacing.xl },
  testBuildBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.sm,
    backgroundColor: Colors.surfaceAlt,
    borderRadius: Radius.md,
    borderWidth: 1,
    borderColor: Colors.borderStrong,
    paddingVertical: Spacing.sm,
    paddingHorizontal: Spacing.md,
    marginBottom: Spacing.md,
  },
  testBuildBannerText: {
    flex: 1,
    fontSize: FontSize.xs,
    fontWeight: FontWeight.medium,
    color: Colors.textSecondary,
  },
  serverRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: Spacing.sm,
    backgroundColor: Colors.surfaceAlt,
    borderRadius: Radius.md,
    borderWidth: 1,
    borderColor: Colors.border,
    paddingVertical: Spacing.sm,
    paddingHorizontal: Spacing.md,
    marginBottom: Spacing.md,
  },
  serverRowText: { flex: 1, fontSize: FontSize.xs, color: Colors.textSecondary },
  serverChangeBtn: {
    minHeight: Layout.tapTargetMin,
    minWidth: 44,
    paddingHorizontal: Spacing.md,
    alignItems: 'center',
    justifyContent: 'center',
  },
  serverChangeBtnText: {
    color: Colors.ink,
    fontSize: FontSize.xs,
    fontWeight: FontWeight.bold,
    textDecorationLine: 'underline',
  },
  sectionTitle: {
    fontSize: FontSize.lg,
    fontWeight: FontWeight.semibold,
    color: Colors.text,
    marginBottom: 4,
  },
  sectionHint: { fontSize: FontSize.sm, color: Colors.textMuted, marginBottom: Spacing.md },
  quickStack: { gap: Spacing.md },
  dividerWrap: {
    flexDirection: 'row',
    alignItems: 'center',
    marginVertical: Spacing.lg,
    gap: Spacing.md,
  },
  dividerLine: { flex: 1, height: 1, backgroundColor: Colors.border },
  dividerText: { fontSize: FontSize.xs, color: Colors.textMuted, fontWeight: FontWeight.medium },
  legal: {
    fontSize: FontSize.xs,
    color: Colors.textMuted,
    marginTop: Spacing.md,
    textAlign: 'center',
    lineHeight: 18,
  },
  // accentInk (bakırın koyu tonu), ham `accent` DEĞİL — accent beyaz zeminde ~3.6:1
  // kontrast ölçüyor (AA metin eşiği 4.5:1'in altında); accentInk ~6.6:1 ile geçiyor.
  legalLink: { color: Colors.accentInk, textDecorationLine: 'underline' },
  guestLink: {
    marginTop: Spacing.lg,
    alignSelf: 'center',
    paddingVertical: Spacing.sm,
    paddingHorizontal: Spacing.md,
    minHeight: Layout.tapTargetMin,
    justifyContent: 'center',
  },
  guestLinkText: {
    fontSize: FontSize.sm,
    color: Colors.textSecondary,
    fontWeight: FontWeight.semibold,
    textDecorationLine: 'underline',
  },
});
