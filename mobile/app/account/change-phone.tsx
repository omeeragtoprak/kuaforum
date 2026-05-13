import { useState } from 'react';
import { View, Text, StyleSheet, Pressable, Alert, KeyboardAvoidingView, Platform, ScrollView } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { Colors, FontSize, FontWeight, Spacing, Layout } from '@/lib/theme';
import { AuthApi } from '@/api/auth';
import { extractApiError } from '@/api/client';
import { Haptics } from '@/lib/haptics';

type Step = 'enter-phone' | 'enter-code';

/**
 * Telefon değiştirme akışı (2-step):
 * 1. Yeni numara girilir → backend OTP gönderir (NetGSM / mock)
 * 2. 6 haneli kod doğrulanır → User.PhoneE164 güncellenir + audit log
 *
 * Rate limit: saatte 3 deneme (backend), 6 dk OTP TTL.
 */
export default function ChangePhoneScreen() {
  const router = useRouter();
  const qc = useQueryClient();

  const [step, setStep] = useState<Step>('enter-phone');
  const [newPhone, setNewPhone] = useState('+90');
  const [otpId, setOtpId] = useState<string | null>(null);
  const [expiresAt, setExpiresAt] = useState<string | null>(null);
  const [code, setCode] = useState('');

  const initiateMut = useMutation({
    mutationFn: () => AuthApi.initiatePhoneChange(newPhone.trim()),
    onSuccess: (res) => {
      Haptics.success();
      setOtpId(res.otpId);
      setExpiresAt(res.expiresAt);
      setStep('enter-code');
    },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });

  const confirmMut = useMutation({
    mutationFn: () => AuthApi.confirmPhoneChange(otpId!, code.trim()),
    onSuccess: () => {
      Haptics.success();
      qc.invalidateQueries({ queryKey: ['my-profile'] });
      Alert.alert(
        '✅ Telefon güncellendi',
        'Yeni telefon numaran aktif. Bir sonraki girişten itibaren bu numarayla giriş yapacaksın.',
        [{ text: 'Tamam', onPress: () => router.back() }],
      );
    },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });

  const phoneValid = /^\+90\s?5\d{9}$/.test(newPhone.replace(/\s/g, ''));
  const codeValid = /^\d{6}$/.test(code);

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} style={styles.flex}>
        <ScrollView
          contentContainerStyle={styles.scroll}
          showsVerticalScrollIndicator={false}
          keyboardShouldPersistTaps="handled"
        >
          <Pressable
            onPress={() => (step === 'enter-code' ? setStep('enter-phone') : router.back())}
            style={styles.backBtn}
          >
            <Text style={styles.back}>← Geri</Text>
          </Pressable>

          <View style={styles.header}>
            <Text style={styles.emoji}>📱</Text>
            <Text style={styles.heading}>
              {step === 'enter-phone' ? 'Telefon numaranı değiştir' : 'Doğrulama kodu'}
            </Text>
            <Text style={styles.subtitle}>
              {step === 'enter-phone'
                ? 'Yeni numarana 6 haneli bir SMS kodu göndereceğiz. Saatte en fazla 3 deneme yapabilirsin.'
                : `${newPhone} numarasına gönderilen 6 haneli kodu gir.`}
            </Text>
          </View>

          {step === 'enter-phone' ? (
            <>
              <Input
                label="Yeni telefon"
                value={newPhone}
                onChangeText={setNewPhone}
                placeholder="+90 5XX XXX XX XX"
                keyboardType="phone-pad"
                autoComplete="tel"
                hint="E.164 formatında: +90 ile başlayan 13 hane"
                error={newPhone.length > 4 && !phoneValid ? 'Geçerli bir Türk cep numarası gir' : undefined}
                maxLength={16}
              />
              <Button
                label="Kod gönder"
                onPress={() => initiateMut.mutate()}
                loading={initiateMut.isPending}
                disabled={!phoneValid}
                size="lg"
              />
              <Text style={styles.helper}>
                🔒 Eski numarayla giriş şu an aktif. Yeni numara doğrulanana kadar değişmez.
              </Text>
            </>
          ) : (
            <>
              <Input
                label="SMS kodu"
                value={code}
                onChangeText={(v) => setCode(v.replace(/\D/g, '').slice(0, 6))}
                placeholder="000000"
                keyboardType="number-pad"
                autoFocus
                maxLength={6}
                hint={expiresAt ? `Geçerlilik: ${new Date(expiresAt).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })}` : undefined}
              />
              <Button
                label="Doğrula ve değiştir"
                onPress={() => confirmMut.mutate()}
                loading={confirmMut.isPending}
                disabled={!codeValid}
                size="lg"
              />
              <Pressable
                onPress={() => {
                  setStep('enter-phone');
                  setCode('');
                }}
                accessibilityRole="button"
              >
                <Text style={styles.resend}>← Numara hatalı mı? Tekrar gir</Text>
              </Pressable>
            </>
          )}
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  flex: { flex: 1 },
  scroll: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingBottom: Spacing.xxl,
    gap: Spacing.lg,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  backBtn: { paddingVertical: 6, alignSelf: 'flex-start' },
  back: { color: Colors.primary, fontSize: FontSize.sm, fontWeight: FontWeight.medium },
  header: { alignItems: 'center', paddingVertical: Spacing.md },
  emoji: { fontSize: 56, marginBottom: Spacing.sm },
  heading: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text, textAlign: 'center' },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 6, textAlign: 'center', lineHeight: 20 },
  helper: { fontSize: FontSize.xs, color: Colors.textMuted, lineHeight: 18, textAlign: 'center' },
  resend: { fontSize: FontSize.sm, color: Colors.primary, textAlign: 'center', fontWeight: FontWeight.semibold, paddingVertical: Spacing.md },
});
