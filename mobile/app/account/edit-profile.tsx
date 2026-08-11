import { useEffect, useState } from 'react';
import { View, Text, StyleSheet, Pressable, Alert, KeyboardAvoidingView, Platform, ScrollView } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { Icon } from '@/components/Icon';
import { Colors, FontSize, FontWeight, LetterSpacing, Radius, Spacing, Layout } from '@/lib/theme';
import { AuthApi } from '@/api/auth';
import { extractApiError } from '@/api/client';
import { Haptics } from '@/lib/haptics';
import { useAuthStore } from '@/store/auth-store';

export default function EditProfileScreen() {
  const router = useRouter();
  const qc = useQueryClient();
  const storeUser = useAuthStore((s) => s.user);
  const setUser = useAuthStore((s) => s.setUser);

  const profileQuery = useQuery({
    queryKey: ['my-profile'],
    queryFn: () => AuthApi.getMyProfile(),
  });

  const [displayName, setDisplayName] = useState('');
  const [avatarUrl, setAvatarUrl] = useState('');

  // Initialize from server profile (her query result değişiminde)
  useEffect(() => {
    const p = profileQuery.data as { displayName?: string | null; avatarUrl?: string | null } | undefined;
    if (p) {
      setDisplayName(p.displayName ?? '');
      setAvatarUrl(p.avatarUrl ?? '');
    }
  }, [profileQuery.data]);

  const updateMut = useMutation({
    mutationFn: async () => {
      const name = displayName.trim();
      const avatar = avatarUrl.trim();
      await AuthApi.updateMyProfile({
        displayName: name.length > 0 ? name : null,
        avatarUrl: avatar.length > 0 ? avatar : null,
      });
    },
    onSuccess: () => {
      Haptics.success();
      qc.invalidateQueries({ queryKey: ['my-profile'] });
      // Local store'u da güncel adla senkronla
      if (storeUser) {
        setUser({
          ...storeUser,
          displayName: displayName.trim() || null,
        });
      }
      Alert.alert('✅ Kaydedildi', 'Profil bilgilerin güncellendi.', [
        { text: 'Tamam', onPress: () => router.back() },
      ]);
    },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });

  const nameValid = displayName.trim().length === 0 || displayName.trim().length >= 2;
  const urlValid = avatarUrl.trim().length === 0 || /^https?:\/\//i.test(avatarUrl.trim());
  const formValid = nameValid && urlValid;

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <KeyboardAvoidingView
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
        style={styles.flex}
      >
        <ScrollView
          contentContainerStyle={styles.scroll}
          showsVerticalScrollIndicator={false}
          keyboardShouldPersistTaps="handled"
        >
          <Pressable
            onPress={() => router.back()}
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
              <Icon name="edit" size={26} color={Colors.textSecondary} strokeWidth={1.5} />
            </View>
            <Text style={styles.heading}>Profilini düzenle</Text>
            <Text style={styles.subtitle}>
              İsim ve avatar görselin kuaförün seni tanıması için kullanılır.
            </Text>
          </View>

          <Input
            label="Görünen isim"
            value={displayName}
            onChangeText={setDisplayName}
            placeholder="Örn: Ahmet Yılmaz"
            maxLength={80}
            hint={nameValid ? `${displayName.length}/80` : 'En az 2 karakter olmalı'}
            error={!nameValid ? 'En az 2 karakter girin (veya boş bırakın)' : undefined}
            autoCapitalize="words"
          />

          <Input
            label="Avatar URL (opsiyonel)"
            value={avatarUrl}
            onChangeText={setAvatarUrl}
            placeholder="https://..."
            keyboardType="url"
            autoCapitalize="none"
            autoCorrect={false}
            hint="Görsel barındırma servisinden bağlantı yapıştır"
            error={!urlValid ? 'https:// veya http:// ile başlamalı' : undefined}
          />

          <View style={styles.helperRow}>
            <Icon name="lock" size={13} color={Colors.textMuted} />
            <Text style={styles.helper}>
              Telefon numaranı buradan değiştiremezsin. Telefon değişikliği için
              "Telefon değiştir" akışı V1.5'te eklenecek.
            </Text>
          </View>

          <Button
            label="Kaydet"
            onPress={() => updateMut.mutate()}
            loading={updateMut.isPending}
            disabled={!formValid || profileQuery.isLoading}
            size="lg"
          />
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
  helperRow: { flexDirection: 'row', alignItems: 'flex-start', gap: 6 },
  helper: { flex: 1, fontSize: FontSize.xs, color: Colors.textMuted, lineHeight: 18 },
});
