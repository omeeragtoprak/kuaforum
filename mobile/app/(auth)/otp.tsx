import { useState, useEffect, useRef } from 'react';
import { View, Text, StyleSheet, KeyboardAvoidingView, Platform, Alert, Pressable, TextInput } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Button } from '@/components/Button';
import { Screen } from '@/components/Screen';
import { Icon } from '@/components/Icon';
import { Colors, FontSize, FontWeight, LetterSpacing, Spacing, Radius } from '@/lib/theme';
import { AuthApi } from '@/api/auth';
import { extractApiError } from '@/api/client';
import { maskPhone } from '@/lib/format';
import { useAuthStore } from '@/store/auth-store';
import { resolvePostAuthRoute } from '@/lib/consent-gate';

const CODE_LEN = 6;

export default function OtpScreen() {
  const params = useLocalSearchParams<{ otpId: string; phone: string; redirectTo?: string }>();
  const router = useRouter();
  const inputRef = useRef<TextInput>(null);

  const [code, setCode] = useState('');
  const [loading, setLoading] = useState(false);
  const [secondsLeft, setSecondsLeft] = useState(300);

  useEffect(() => {
    const t = setTimeout(() => inputRef.current?.focus(), 250);
    return () => clearTimeout(t);
  }, []);

  useEffect(() => {
    const timer = setInterval(() => setSecondsLeft((s) => Math.max(0, s - 1)), 1000);
    return () => clearInterval(timer);
  }, []);

  const expired = secondsLeft === 0;
  const minutes = Math.floor(secondsLeft / 60);
  const seconds = secondsLeft % 60;

  async function handleVerify(submittedCode = code) {
    if (submittedCode.length !== CODE_LEN) return;
    setLoading(true);
    try {
      await AuthApi.verifyOtp(params.otpId!, submittedCode);
      // RFC-008 — verify response'unda requiresTosConsent yok; tek doğruluk kaynağı
      // GET /v1/users/me olduğu için hydrate() ile taze profil çekilir, sonra aynı
      // gate helper (AuthGate'in de kullandığı) hedefi belirler.
      await useAuthStore.getState().hydrate();
      const freshUser = useAuthStore.getState().user;
      // RFC-010 — misafir randevu kapısı: login.tsx'ten taşınan redirectTo varsa (ve
      // consent gerekmiyorsa) kullanıcı seçimlerini koruyarak oraya döner.
      if (freshUser) router.replace(resolvePostAuthRoute(freshUser, params.redirectTo) as never);
    } catch (err) {
      Alert.alert('Hata', extractApiError(err));
      setCode('');
    } finally {
      setLoading(false);
    }
  }

  async function handleResend() {
    try {
      await AuthApi.sendOtp(params.phone!, 'Login');
      setSecondsLeft(300);
      Alert.alert('Gönderildi', 'Yeni doğrulama kodu yola çıktı.');
    } catch (err) {
      Alert.alert('Hata', extractApiError(err));
    }
  }

  function handleChange(t: string) {
    const cleaned = t.replace(/\D/g, '').slice(0, CODE_LEN);
    setCode(cleaned);
    if (cleaned.length === CODE_LEN && !loading) {
      handleVerify(cleaned);
    }
  }

  return (
    <Screen scroll edges={['top', 'bottom']}>
      <KeyboardAvoidingView
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
        style={styles.flex}
      >
        <Pressable
          onPress={() => router.back()}
          style={styles.back}
          hitSlop={8}
          accessibilityRole="button"
          accessibilityLabel="Telefon numarasını değiştir, geri dön"
        >
          <Icon name="chevronLeft" size={18} color={Colors.ink} strokeWidth={1.8} />
          <Text style={styles.backText}>Telefon değiştir</Text>
        </Pressable>

        <View style={styles.header}>
          <View style={styles.iconBubble}>
            <Icon name="phone" size={30} color={Colors.ink} strokeWidth={1.6} />
          </View>
          <Text style={styles.heading}>Doğrulama kodu</Text>
          <Text style={styles.helper}>
            {maskPhone(params.phone ?? '')} numarasına gönderilen{' '}
            <Text style={styles.helperEmphasis}>6 haneli kodu</Text> gir.
          </Text>
        </View>

        <Pressable onPress={() => inputRef.current?.focus()} style={styles.cellsRow}>
          {Array.from({ length: CODE_LEN }).map((_, i) => {
            const ch = code[i];
            const isCurrent = i === code.length;
            return (
              <View
                key={i}
                style={[
                  styles.cell,
                  isCurrent && styles.cellActive,
                  ch && styles.cellFilled,
                ]}
              >
                <Text style={styles.cellText}>{ch ?? ''}</Text>
              </View>
            );
          })}
        </Pressable>

        <TextInput
          ref={inputRef}
          value={code}
          onChangeText={handleChange}
          keyboardType="number-pad"
          maxLength={CODE_LEN}
          autoFocus
          style={styles.hiddenInput}
          accessibilityLabel="6 haneli OTP kodu"
          textContentType="oneTimeCode"
        />

        {!expired ? (
          <Text style={styles.timer}>
            Kod{' '}
            <Text style={styles.timerNumber}>
              {minutes.toString().padStart(2, '0')}:{seconds.toString().padStart(2, '0')}
            </Text>{' '}
            sonra geçersiz olur.
          </Text>
        ) : (
          <Pressable onPress={handleResend} style={styles.resendBtn}>
            <Text style={styles.resend}>Kodu tekrar gönder</Text>
          </Pressable>
        )}

        <View style={styles.actions}>
          <Button
            label={loading ? 'Doğrulanıyor…' : 'Doğrula'}
            onPress={() => handleVerify()}
            loading={loading}
            disabled={code.length !== CODE_LEN || expired}
            size="lg"
          />
        </View>

        {!expired ? (
          <Pressable onPress={handleResend} style={styles.resendInline} disabled={secondsLeft > 270}>
            <Text style={[styles.resendText, secondsLeft > 270 && styles.resendDisabled]}>
              Kod gelmedi mi? Tekrar gönder
            </Text>
          </Pressable>
        ) : null}
      </KeyboardAvoidingView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  back: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    paddingVertical: Spacing.sm,
    alignSelf: 'flex-start',
    marginBottom: Spacing.md,
  },
  backText: { color: Colors.ink, fontSize: FontSize.sm, fontWeight: FontWeight.medium },
  header: { alignItems: 'center', marginBottom: Spacing.xl, paddingHorizontal: Spacing.md },
  iconBubble: {
    width: 72,
    height: 72,
    borderRadius: 36,
    backgroundColor: Colors.surfaceAlt,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: Spacing.md,
  },
  heading: {
    fontSize: FontSize.xxl,
    fontWeight: FontWeight.bold,
    color: Colors.text,
    letterSpacing: LetterSpacing.title,
    marginBottom: 6,
  },
  helper: { fontSize: FontSize.sm, color: Colors.textSecondary, textAlign: 'center', lineHeight: 20 },
  helperEmphasis: { color: Colors.text, fontWeight: FontWeight.semibold },
  cellsRow: { flexDirection: 'row', justifyContent: 'space-between', gap: 8, marginBottom: Spacing.lg },
  cell: {
    flex: 1,
    aspectRatio: 0.8,
    borderRadius: Radius.md,
    backgroundColor: Colors.surface,
    borderWidth: 2,
    borderColor: Colors.border,
    alignItems: 'center',
    justifyContent: 'center',
  },
  cellActive: { borderColor: Colors.ink, backgroundColor: Colors.surfaceAlt },
  cellFilled: { borderColor: Colors.ink, backgroundColor: Colors.surface },
  cellText: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text },
  hiddenInput: { position: 'absolute', opacity: 0, height: 1, width: 1 },
  timer: {
    fontSize: FontSize.sm,
    color: Colors.textMuted,
    textAlign: 'center',
    marginTop: Spacing.sm,
  },
  timerNumber: { color: Colors.text, fontWeight: FontWeight.semibold },
  resendBtn: { paddingVertical: Spacing.sm, alignSelf: 'center' },
  // accentInk — bu ekranın tek "vurgulanan" eylemi (kodu tekrar iste). Ham `accent`
  // beyaz zeminde AA metin eşiğini (4.5:1) geçmiyor, accentInk geçiyor (bkz. login.tsx).
  resend: { fontSize: FontSize.md, color: Colors.accentInk, fontWeight: FontWeight.semibold },
  actions: { marginTop: Spacing.xl },
  resendInline: { paddingVertical: Spacing.lg, alignSelf: 'center' },
  resendText: { color: Colors.accentInk, fontSize: FontSize.sm, fontWeight: FontWeight.medium },
  resendDisabled: { color: Colors.textMuted },
});
