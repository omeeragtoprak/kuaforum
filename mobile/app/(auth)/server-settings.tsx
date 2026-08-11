import { useState } from 'react';
import { View, Text, StyleSheet, KeyboardAvoidingView, Platform, Alert, Pressable } from 'react-native';
import { useRouter } from 'expo-router';
import { useQueryClient } from '@tanstack/react-query';
import axios from 'axios';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { Screen } from '@/components/Screen';
import { Icon } from '@/components/Icon';
import { Colors, FontSize, FontWeight, LetterSpacing, Layout, Radius, Spacing } from '@/lib/theme';
import { TokenStorage } from '@/lib/secure-storage';
import { useAuthStore } from '@/store/auth-store';
import {
  getApiBaseUrl,
  getBuildDefaultApiBaseUrl,
  setApiBaseUrlOverride,
  resetApiBaseUrlToDefault,
} from '@/api/client';
import { isTestBuild, normalizeBaseUrl } from '@/lib/runtime-base-url';

type TestState = 'idle' | 'testing' | 'success' | 'error';

const SHORTCUTS: Array<{ label: string; hint: string; url: () => string }> = [
  {
    label: 'Android emülatör',
    hint: 'Emülatörün host bilgisayara loopback adresi — IP değişse bile çalışır.',
    url: () => 'http://10.0.2.2:5100',
  },
  {
    label: 'Derleme varsayılanı',
    hint: 'Bu APK derlenirken gömülen adres (EXPO_PUBLIC_API_BASE_URL).',
    url: () => getBuildDefaultApiBaseUrl(),
  },
];

/**
 * Test build'lerde API taban adresini çalışma zamanında değiştirme ekranı.
 *
 * GÜVENLİK: `isTestBuild()` (== `EXPO_PUBLIC_ENABLE_TEST_LOGIN === '1'`, `__DEV__`
 * DAHİL DEĞİL) false ise ekran hiçbir şey yapmaz — kaydet/sıfırla no-op'a düşer
 * (persist katmanı zaten `runtime-base-url.ts`'te aynı bayrakla kapalı), ayrıca
 * kullanıcı otomatik login'e geri döndürülür. Bu, birisi store build'de bu route'a
 * (derlenmiş kod içinde kalsa da) deep-link ile erişmeye çalışsa bile hiçbir etkisi
 * olmayacağını garanti eder — çift katman savunma.
 */
export default function ServerSettingsScreen() {
  const router = useRouter();
  const queryClient = useQueryClient();

  const testBuildActive = isTestBuild();

  const [value, setValue] = useState(getApiBaseUrl());
  const [testState, setTestState] = useState<TestState>('idle');
  const [testMessage, setTestMessage] = useState('');
  const [saving, setSaving] = useState(false);

  if (!testBuildActive) {
    return (
      <Screen>
        <View style={styles.blockedWrap}>
          <Text style={styles.blockedText}>Bu ekran yalnız test build'lerinde kullanılabilir.</Text>
          <Button label="Girişe dön" onPress={() => router.replace('/(auth)/login')} />
        </View>
      </Screen>
    );
  }

  function handlePickShortcut(url: string) {
    setValue(url);
    setTestState('idle');
    setTestMessage('');
  }

  async function handleTestConnection() {
    const normalized = normalizeBaseUrl(value);
    if (!normalized) {
      setTestState('error');
      setTestMessage('Geçersiz adres — http:// veya https:// ile başlamalı (örn. http://192.168.1.11:5100).');
      return;
    }
    setTestState('testing');
    setTestMessage('');
    try {
      const res = await axios.get(`${normalized}/health`, { timeout: 6_000 });
      if (res.status >= 200 && res.status < 300) {
        setTestState('success');
        setTestMessage('Bağlantı başarılı — sunucuya ulaşıldı.');
      } else {
        setTestState('error');
        setTestMessage(`Sunucu beklenmeyen bir yanıt verdi (HTTP ${res.status}).`);
      }
    } catch (err) {
      const axErr = err as { response?: { status?: number }; code?: string };
      if (axErr.response) {
        // Sunucuya ulaşıldı ama /health beklenmeyen kod döndü — yine de adres muhtemelen
        // doğru (yanlış port/servis olabilir), kullanıcıyı net bilgilendir.
        setTestState('error');
        setTestMessage(`Sunucu yanıt verdi ama beklenmeyen durum kodu (HTTP ${axErr.response.status}).`);
      } else {
        setTestState('error');
        setTestMessage('Sunucuya ulaşılamadı. Adresi, aynı ağda olduğunu ve backend\'in çalıştığını kontrol et.');
      }
    }
  }

  /**
   * Eski sunucunun token'ı yeni sunucuda geçersizdir — sessiz 401 döngüsü oluşmasın
   * diye adres değiştiğinde kullanıcı ÇIKIŞA ZORLANIR. `TokenStorage.clear()` sonra
   * `clearLocalAuth()` sırası, `api/client.ts`'teki refresh-failure interceptor'ının
   * kullandığı AYNI kalıp (best-effort local-only wipe, backend logout çağrısı YOK —
   * zaten eski sunucudaki oturumun bir önemi kalmadı).
   */
  async function forceLogoutAndClearCache() {
    await TokenStorage.clear();
    queryClient.clear();
    useAuthStore.getState().clearLocalAuth();
  }

  async function handleSave() {
    const normalized = normalizeBaseUrl(value);
    if (!normalized) {
      Alert.alert('Geçersiz adres', 'Adres http:// veya https:// ile başlamalı (örn. http://192.168.1.11:5100).');
      return;
    }
    setSaving(true);
    try {
      await setApiBaseUrlOverride(normalized);
      await forceLogoutAndClearCache();
      Alert.alert(
        'Sunucu adresi güncellendi',
        `Yeni adres: ${normalized}\n\nGüvenlik için oturum kapatıldı — tekrar giriş yapman gerekiyor.`,
        [{ text: 'Tamam', onPress: () => router.replace('/(auth)/login') }],
      );
    } finally {
      setSaving(false);
    }
  }

  async function handleResetDefault() {
    setSaving(true);
    try {
      await resetApiBaseUrlToDefault();
      await forceLogoutAndClearCache();
      const def = getBuildDefaultApiBaseUrl();
      setValue(def);
      Alert.alert(
        'Varsayılana dönüldü',
        `Sunucu adresi derleme varsayılanına döndü: ${def}\n\nGüvenlik için oturum kapatıldı — tekrar giriş yapman gerekiyor.`,
        [{ text: 'Tamam', onPress: () => router.replace('/(auth)/login') }],
      );
    } finally {
      setSaving(false);
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
          accessibilityLabel="Geri dön"
        >
          <Icon name="chevronLeft" size={18} color={Colors.ink} strokeWidth={2} />
          <Text style={styles.backText}>Geri</Text>
        </Pressable>

        <Text style={styles.heading}>Sunucu adresi</Text>
        <Text style={styles.helper}>
          Test APK'sı bu adresteki backend'e bağlanır. Geliştirme makinesinin LAN IP'si
          değiştiğinde APK'yı yeniden derlemek yerine buradan güncelle.
        </Text>

        <Input
          label="API taban adresi"
          required
          value={value}
          onChangeText={(t) => { setValue(t); setTestState('idle'); setTestMessage(''); }}
          placeholder="http://192.168.1.11:5100"
          autoCapitalize="none"
          autoCorrect={false}
          keyboardType="url"
          accessibilityLabel="API taban adresi"
          hint="http:// veya https:// ile başlamalı, sondaki / otomatik temizlenir."
        />

        <Button
          label={testState === 'testing' ? 'Test ediliyor…' : 'Bağlantıyı test et'}
          variant="outline"
          onPress={handleTestConnection}
          loading={testState === 'testing'}
          disabled={testState === 'testing' || saving}
        />

        {testState === 'success' || testState === 'error' ? (
          <View
            style={[
              styles.resultBanner,
              testState === 'success' ? styles.resultSuccess : styles.resultError,
            ]}
            accessibilityRole="text"
            accessibilityLabel={testMessage}
          >
            <Icon
              name={testState === 'success' ? 'check' : 'alert'}
              size={15}
              color={testState === 'success' ? Colors.success : Colors.error}
            />
            <Text style={styles.resultText}>{testMessage}</Text>
          </View>
        ) : null}

        <View style={styles.actions}>
          <Button label="Kaydet" onPress={handleSave} loading={saving} disabled={saving} size="lg" />
          <Button
            label="Varsayılana dön"
            variant="secondary"
            onPress={handleResetDefault}
            loading={saving}
            disabled={saving}
          />
        </View>

        <Text style={styles.shortcutsTitle}>Hızlı seçim</Text>
        <View style={styles.shortcutList}>
          {SHORTCUTS.map((s) => (
            <Pressable
              key={s.label}
              onPress={() => handlePickShortcut(s.url())}
              style={({ pressed }) => [styles.shortcutCard, pressed && styles.shortcutPressed]}
              accessibilityRole="button"
              accessibilityLabel={`${s.label} adresini kullan`}
              accessibilityHint={s.hint}
            >
              <Text style={styles.shortcutLabel}>{s.label}</Text>
              <Text style={styles.shortcutUrl}>{s.url()}</Text>
              <Text style={styles.shortcutHint}>{s.hint}</Text>
            </Pressable>
          ))}
        </View>
      </KeyboardAvoidingView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  blockedWrap: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: Spacing.lg, padding: Spacing.xl },
  blockedText: { fontSize: FontSize.md, color: Colors.textSecondary, textAlign: 'center' },
  back: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 2,
    paddingVertical: Spacing.sm,
    alignSelf: 'flex-start',
    marginBottom: Spacing.md,
    marginLeft: -4,
    minHeight: Layout.tapTargetMin,
    justifyContent: 'center',
  },
  backText: { color: Colors.ink, fontSize: FontSize.sm, fontWeight: FontWeight.medium },
  heading: { fontSize: FontSize.xl, fontWeight: FontWeight.bold, color: Colors.text, marginBottom: 6, letterSpacing: LetterSpacing.title },
  helper: {
    fontSize: FontSize.sm,
    color: Colors.textSecondary,
    lineHeight: 20,
    marginBottom: Spacing.lg,
  },
  resultBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    borderRadius: Radius.md,
    borderWidth: 1,
    paddingVertical: Spacing.sm,
    paddingHorizontal: Spacing.md,
    marginTop: -Spacing.sm,
    marginBottom: Spacing.lg,
  },
  resultSuccess: { backgroundColor: Colors.successSoft, borderColor: Colors.success },
  resultError: { backgroundColor: Colors.errorSoft, borderColor: Colors.error },
  resultText: { flex: 1, fontSize: FontSize.sm, color: Colors.text, fontWeight: FontWeight.medium },
  actions: { gap: Spacing.md, marginBottom: Spacing.xl },
  shortcutsTitle: {
    fontSize: FontSize.md,
    fontWeight: FontWeight.semibold,
    color: Colors.text,
    marginBottom: Spacing.sm,
  },
  shortcutList: { gap: Spacing.sm },
  shortcutCard: {
    borderWidth: 1,
    borderColor: Colors.border,
    borderRadius: Radius.md,
    backgroundColor: Colors.surface,
    padding: Spacing.md,
    minHeight: Layout.tapTargetMin,
  },
  shortcutPressed: { opacity: 0.85, backgroundColor: Colors.surfaceAlt },
  shortcutLabel: { fontSize: FontSize.sm, fontWeight: FontWeight.semibold, color: Colors.text },
  shortcutUrl: { fontSize: FontSize.xs, color: Colors.ink, marginTop: 2, fontVariant: ['tabular-nums'] },
  shortcutHint: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 2 },
});
