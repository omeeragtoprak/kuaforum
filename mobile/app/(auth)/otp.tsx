import { useState, useEffect } from 'react';
import { View, Text, StyleSheet, KeyboardAvoidingView, Platform, Alert, Pressable } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { Colors, FontSize, Spacing } from '@/lib/theme';
import { AuthApi } from '@/api/auth';
import { extractApiError } from '@/api/client';
import { maskPhone } from '@/lib/format';
import { useAuthStore } from '@/store/auth-store';

export default function OtpScreen() {
  const params = useLocalSearchParams<{ otpId: string; phone: string }>();
  const router = useRouter();
  const setUser = useAuthStore((s) => s.setUser);

  const [code, setCode] = useState('');
  const [loading, setLoading] = useState(false);
  const [secondsLeft, setSecondsLeft] = useState(300);

  useEffect(() => {
    const timer = setInterval(() => {
      setSecondsLeft((s) => Math.max(0, s - 1));
    }, 1000);
    return () => clearInterval(timer);
  }, []);

  const expired = secondsLeft === 0;
  const minutes = Math.floor(secondsLeft / 60);
  const seconds = secondsLeft % 60;

  async function handleVerify() {
    if (code.length !== 6) {
      Alert.alert('Geçersiz kod', '6 haneli kodu girin.');
      return;
    }

    setLoading(true);
    try {
      const res = await AuthApi.verifyOtp(params.otpId!, code);
      setUser(res.user);
      router.replace('/(tabs)');
    } catch (err) {
      Alert.alert('Hata', extractApiError(err));
    } finally {
      setLoading(false);
    }
  }

  async function handleResend() {
    try {
      await AuthApi.sendOtp(params.phone!, 'Login');
      setSecondsLeft(300);
      Alert.alert('Yeni kod gönderildi');
    } catch (err) {
      Alert.alert('Hata', extractApiError(err));
    }
  }

  return (
    <SafeAreaView style={styles.container} edges={['top', 'bottom']}>
      <KeyboardAvoidingView
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
        style={styles.flex}
      >
        <View style={styles.content}>
          <Pressable onPress={() => router.back()} style={styles.back}>
            <Text style={styles.backText}>← Telefon değiştir</Text>
          </Pressable>

          <View style={styles.form}>
            <Text style={styles.heading}>Doğrulama kodu</Text>
            <Text style={styles.helper}>
              {maskPhone(params.phone ?? '')} numarasına gönderilen 6 haneli kodu girin.
            </Text>

            <Input
              value={code}
              onChangeText={(t) => setCode(t.replace(/\D/g, '').slice(0, 6))}
              keyboardType="number-pad"
              placeholder="• • • • • •"
              maxLength={6}
              style={styles.codeInput}
              accessibilityLabel="6 haneli OTP kodu"
            />

            {!expired ? (
              <Text style={styles.timer}>
                Kod {minutes.toString().padStart(2, '0')}:{seconds.toString().padStart(2, '0')} sonra geçersiz olur.
              </Text>
            ) : (
              <Pressable onPress={handleResend}>
                <Text style={styles.resend}>Kodu tekrar gönder</Text>
              </Pressable>
            )}

            <Button
              label="Doğrula"
              onPress={handleVerify}
              loading={loading}
              disabled={code.length !== 6 || expired}
              style={{ marginTop: Spacing.lg }}
            />
          </View>
        </View>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  flex: { flex: 1 },
  content: { flex: 1, padding: Spacing.lg },
  back: { paddingVertical: Spacing.sm },
  backText: { color: Colors.primary, fontSize: FontSize.sm },
  form: { marginTop: Spacing.xl },
  heading: { fontSize: FontSize.xxl, fontWeight: '700', color: Colors.text, marginBottom: Spacing.sm },
  helper: { fontSize: FontSize.sm, color: Colors.textSecondary, marginBottom: Spacing.lg },
  codeInput: { fontSize: FontSize.xxl, textAlign: 'center', letterSpacing: 8 },
  timer: { fontSize: FontSize.sm, color: Colors.textMuted, textAlign: 'center', marginTop: Spacing.sm },
  resend: { fontSize: FontSize.md, color: Colors.primary, textAlign: 'center', marginTop: Spacing.sm, fontWeight: '600' },
});
