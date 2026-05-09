import { useState } from 'react';
import { View, Text, StyleSheet, Pressable, Alert, KeyboardAvoidingView, Platform, ScrollView } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { Colors, FontSize, FontWeight, Spacing, Layout } from '@/lib/theme';
import { ReviewApi } from '@/api/review';
import { extractApiError } from '@/api/client';
import { Haptics } from '@/lib/haptics';

export default function ReviewSubmitScreen() {
  const { bookingId } = useLocalSearchParams<{ bookingId: string }>();
  const router = useRouter();

  const [rating, setRating] = useState<number>(0);
  const [body, setBody] = useState('');
  const [loading, setLoading] = useState(false);

  async function handleSubmit() {
    if (rating < 1) return;
    setLoading(true);
    try {
      await ReviewApi.submit(bookingId!, rating, body || undefined);
      Haptics.success();
      Alert.alert(
        '🙏 Yorumun alındı',
        'Yorumun inceleniyor. Onaylanınca profilde görünür.',
        [{ text: 'Tamam', onPress: () => router.replace('/(tabs)/bookings') }],
      );
    } catch (err) {
      Haptics.error();
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
        <ScrollView
          contentContainerStyle={styles.scroll}
          showsVerticalScrollIndicator={false}
          keyboardShouldPersistTaps="handled"
        >
          <Pressable onPress={() => router.back()} style={styles.backBtn}>
            <Text style={styles.back}>← Geri</Text>
          </Pressable>

          <View style={styles.header}>
            <Text style={styles.emoji}>⭐</Text>
            <Text style={styles.heading}>Deneyimini değerlendir</Text>
            <Text style={styles.subtitle}>Yorumun başkalarına yardımcı olur.</Text>
          </View>

          <View style={styles.ratingBlock}>
            <View style={styles.starsRow}>
              {[1, 2, 3, 4, 5].map((s) => (
                <Pressable
                  key={s}
                  onPress={() => { Haptics.light(); setRating(s); }}
                  style={styles.starBtn}
                  hitSlop={6}
                  accessibilityRole="button"
                  accessibilityLabel={`${s} yıldız`}
                  accessibilityState={{ selected: rating === s }}
                >
                  <Text style={[styles.starText, s <= rating && styles.starActive]}>
                    {s <= rating ? '★' : '☆'}
                  </Text>
                </Pressable>
              ))}
            </View>
            <Text style={styles.ratingLabel}>
              {rating === 0 ? 'Yıldıza dokun' : ratingLabel(rating)}
            </Text>
          </View>

          <Input
            label="Yorumun"
            value={body}
            onChangeText={setBody}
            placeholder="Hizmet hakkında ne düşünüyorsun?"
            multiline
            numberOfLines={5}
            maxLength={1000}
            hint={`${body.length}/1000`}
          />

          <Text style={styles.helper}>
            🛡️ Yorumlar moderasyondan geçer. Spam veya küfür içerirse onay beklenir.
          </Text>

          <Button
            label="Yorumu gönder"
            onPress={handleSubmit}
            loading={loading}
            disabled={rating < 1}
            size="lg"
          />
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

function ratingLabel(rating: number): string {
  return ['', 'Çok kötü 😞', 'Kötü 😕', 'Fena değil 🙂', 'İyi 😊', 'Mükemmel 🤩'][rating] ?? '';
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
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 6, textAlign: 'center' },
  ratingBlock: { alignItems: 'center', paddingVertical: Spacing.md },
  starsRow: { flexDirection: 'row', justifyContent: 'center', gap: 6 },
  starBtn: { padding: 4 },
  starText: { fontSize: 56, color: Colors.border },
  starActive: { color: Colors.star },
  ratingLabel: { textAlign: 'center', fontSize: FontSize.lg, color: Colors.text, fontWeight: FontWeight.semibold, marginTop: Spacing.md },
  helper: { fontSize: FontSize.xs, color: Colors.textMuted, lineHeight: 18 },
});
