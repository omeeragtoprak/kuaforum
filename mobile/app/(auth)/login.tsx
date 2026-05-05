import { useState } from 'react';
import { View, Text, StyleSheet, KeyboardAvoidingView, Platform, Alert } from 'react-native';
import { useRouter } from 'expo-router';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { Screen } from '@/components/Screen';
import { RoleQuickLogin, DemoRole } from '@/components/RoleQuickLogin';
import { Colors, FontSize, FontWeight, Spacing } from '@/lib/theme';
import { AuthApi } from '@/api/auth';
import { extractApiError } from '@/api/client';
import { useAuthStore } from '@/store/auth-store';
import { formatPhoneTr, isValidTrMobile, phoneToE164 } from '@/lib/format';

export default function LoginScreen() {
  const router = useRouter();
  const setUser = useAuthStore((s) => s.setUser);

  const [phone, setPhone] = useState('+90 ');
  const [loading, setLoading] = useState(false);
  const [quickRole, setQuickRole] = useState<DemoRole | null>(null);

  const isValid = isValidTrMobile(phone);

  async function handleSendOtp() {
    if (!isValid) {
      Alert.alert('Geçersiz numara', 'Lütfen 5 ile başlayan 10 haneli mobil numaranı gir.');
      return;
    }
    setLoading(true);
    try {
      const e164 = phoneToE164(phone);
      const res = await AuthApi.sendOtp(e164, 'Login');
      router.push({ pathname: '/(auth)/otp', params: { otpId: res.otpId, phone: e164 } });
    } catch (err) {
      Alert.alert('Hata', extractApiError(err));
    } finally {
      setLoading(false);
    }
  }

  async function handleQuickLogin(role: DemoRole) {
    setQuickRole(role);
    try {
      const res = await AuthApi.devLogin(role);
      setUser(res.user as any);
      // AuthGate role'e göre yönlendirir
      router.replace('/(tabs)');
    } catch (err) {
      Alert.alert('Hızlı giriş başarısız', extractApiError(err));
    } finally {
      setQuickRole(null);
    }
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

        <View style={styles.section}>
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
            Devam ederek <Text style={styles.legalLink}>Kullanım Koşulları</Text> ve{' '}
            <Text style={styles.legalLink}>KVKK Aydınlatma Metni</Text>'ni kabul etmiş olursun.
          </Text>
        </View>
      </KeyboardAvoidingView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  brand: { alignItems: 'center', marginTop: Spacing.xl, marginBottom: Spacing.xl },
  logo: { fontSize: FontSize.display, fontWeight: FontWeight.bold, color: Colors.primary, letterSpacing: -1 },
  tagline: { fontSize: FontSize.md, color: Colors.textSecondary, marginTop: 4 },
  section: { marginBottom: Spacing.xl },
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
  legalLink: { color: Colors.primary, textDecorationLine: 'underline' },
});
