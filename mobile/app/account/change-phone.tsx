import { useState } from 'react';
import { View, Text, StyleSheet, Pressable, Alert, KeyboardAvoidingView, Platform, ScrollView } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { Icon } from '@/components/Icon';
import { Colors, FontSize, FontWeight, LetterSpacing, Radius, Spacing, Layout } from '@/lib/theme';
import { AuthApi } from '@/api/auth';
import { extractApiError } from '@/api/client';
import { Haptics } from '@/lib/haptics';
import { formatPhoneTr, isValidTrMobile, phoneToE164 } from '@/lib/format';

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
  const [newPhone, setNewPhone] = useState('+90 ');
  const [otpId, setOtpId] = useState<string | null>(null);
  const [expiresAt, setExpiresAt] = useState<string | null>(null);
  const [code, setCode] = useState('');

  const initiateMut = useMutation({
    mutationFn: () => AuthApi.initiatePhoneChange(phoneToE164(newPhone)),
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
        'Telefon güncellendi',
        'Yeni telefon numaran aktif. Bir sonraki girişten itibaren bu numarayla giriş yapacaksın.',
        [{ text: 'Tamam', onPress: () => router.back() }],
      );
    },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });

  const phoneValid = isValidTrMobile(newPhone);
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
            hitSlop={8}
            accessibilityRole="button"
            accessibilityLabel="Geri dön"
          >
            <Icon name="chevronLeft" size={18} color={Colors.ink} strokeWidth={2} />
            <Text style={styles.back}>Geri</Text>
          </Pressable>

          <View style={styles.header}>
            <View style={styles.iconWrap}>
              <Icon name="phone" size={26} color={Colors.textSecondary} strokeWidth={1.5} />
            </View>
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
                onChangeText={(v) => setNewPhone(formatPhoneTr(v))}
                placeholder="+90 555 123 45 67"
                keyboardType="phone-pad"
                autoComplete="tel"
                hint="Türk cep numarası — login akışıyla aynı format"
                error={newPhone.replace(/\s/g, '').length > 4 && !phoneValid ? 'Geçerli bir Türk cep numarası gir' : undefined}
              />
              <Button
                label="Kod gönder"
                onPress={() => initiateMut.mutate()}
                loading={initiateMut.isPending}
                disabled={!phoneValid}
                size="lg"
              />
              <View style={styles.helperRow}>
                <Icon name="lock" size={13} color={Colors.textMuted} />
                <Text style={styles.helper}>
                  Eski numarayla giriş şu an aktif. Yeni numara doğrulanana kadar değişmez.
                </Text>
              </View>
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
                style={styles.resendRow}
              >
                <Icon name="chevronLeft" size={14} color={Colors.accent} strokeWidth={2} />
                <Text style={styles.resend}>Numara hatalı mı? Tekrar gir</Text>
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
  backBtn: { flexDirection: 'row', alignItems: 'center', gap: 2, paddingVertical: 6, alignSelf: 'flex-start', marginLeft: -4 },
  back: { color: Colors.ink, fontSize: FontSize.sm, fontWeight: FontWeight.medium },
  header: { alignItems: 'center', paddingVertical: Spacing.md },
  iconWrap: {
    width: 64,
    height: 64,
    borderRadius: Radius.full,
    backgroundColor: Colors.surfaceAlt,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: Spacing.md,
  },
  heading: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text, textAlign: 'center', letterSpacing: LetterSpacing.title },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 6, textAlign: 'center', lineHeight: 20 },
  helperRow: { flexDirection: 'row', alignItems: 'flex-start', gap: 6, justifyContent: 'center' },
  helper: { flex: 1, fontSize: FontSize.xs, color: Colors.textMuted, lineHeight: 18, textAlign: 'center' },
  resendRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 4, paddingVertical: Spacing.md },
  resend: { fontSize: FontSize.sm, color: Colors.accent, fontWeight: FontWeight.semibold },
});
