import { useState } from 'react';
import { View, Text, StyleSheet, KeyboardAvoidingView, Platform, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { Colors, FontSize, Spacing } from '@/lib/theme';
import { AuthApi } from '@/api/auth';
import { extractApiError } from '@/api/client';

export default function LoginScreen() {
  const router = useRouter();
  const [phone, setPhone] = useState('+905');
  const [loading, setLoading] = useState(false);

  const isValid = /^\+905\d{9}$/.test(phone.replace(/\s/g, ''));

  async function handleSendOtp() {
    if (!isValid) {
      Alert.alert('Geçersiz numara', 'Türk mobil numarası +905XXXXXXXXX biçiminde olmalı.');
      return;
    }

    setLoading(true);
    try {
      const cleanPhone = phone.replace(/\s/g, '');
      const res = await AuthApi.sendOtp(cleanPhone, 'Login');
      router.push({ pathname: '/(auth)/otp', params: { otpId: res.otpId, phone: cleanPhone } });
    } catch (err) {
      Alert.alert('Hata', extractApiError(err));
    } finally {
      setLoading(false);
    }
  }

  return (
    <SafeAreaView style={styles.container} edges={['top', 'bottom']}>
      <KeyboardAvoidingView
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
        style={styles.flex}
      >
        <View style={styles.content}>
          <View style={styles.brand}>
            <Text style={styles.title}>Kuaforum</Text>
            <Text style={styles.subtitle}>Türkiye'nin kuaför pazaryeri</Text>
          </View>

          <View style={styles.form}>
            <Text style={styles.heading}>Telefon numaran</Text>
            <Text style={styles.helper}>SMS ile 6 haneli doğrulama kodu göndereceğiz.</Text>

            <Input
              value={phone}
              onChangeText={setPhone}
              keyboardType="phone-pad"
              placeholder="+90 555 123 45 67"
              autoComplete="tel"
              maxLength={16}
              accessibilityLabel="Telefon numarası"
            />

            <Button
              label="Kod gönder"
              onPress={handleSendOtp}
              loading={loading}
              disabled={!isValid}
            />

            <Text style={styles.legal}>
              Devam ederek <Text style={styles.legalLink}>Kullanım Koşulları</Text> ve{' '}
              <Text style={styles.legalLink}>KVKK Aydınlatma Metni</Text>'ni kabul etmiş olursunuz.
            </Text>
          </View>
        </View>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  flex: { flex: 1 },
  content: { flex: 1, padding: Spacing.lg, justifyContent: 'space-between' },
  brand: { alignItems: 'center', marginTop: Spacing.xxl },
  title: { fontSize: FontSize.display, fontWeight: '700', color: Colors.primary },
  subtitle: { fontSize: FontSize.md, color: Colors.textSecondary, marginTop: Spacing.xs },
  form: { marginBottom: Spacing.xxl },
  heading: { fontSize: FontSize.xl, fontWeight: '600', marginBottom: Spacing.xs, color: Colors.text },
  helper: { fontSize: FontSize.sm, color: Colors.textSecondary, marginBottom: Spacing.lg },
  legal: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: Spacing.md, textAlign: 'center' },
  legalLink: { color: Colors.primary, textDecorationLine: 'underline' },
});
