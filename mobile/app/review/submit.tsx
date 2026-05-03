import { useState } from 'react';
import { View, Text, StyleSheet, Pressable, Alert, KeyboardAvoidingView, Platform } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { Colors, FontSize, Spacing } from '@/lib/theme';
import { ReviewApi } from '@/api/review';
import { extractApiError } from '@/api/client';

export default function ReviewSubmitScreen() {
  const { bookingId } = useLocalSearchParams<{ bookingId: string }>();
  const router = useRouter();

  const [rating, setRating] = useState<number>(5);
  const [body, setBody] = useState('');
  const [loading, setLoading] = useState(false);

  async function handleSubmit() {
    setLoading(true);
    try {
      await ReviewApi.submit(bookingId!, rating, body || undefined);
      Alert.alert(
        'Yorumun alındı',
        'Yorumunuz inceleniyor. Onaylandığında profilde görünür hale gelecek.',
        [{ text: 'Tamam', onPress: () => router.replace('/(tabs)/bookings') }],
      );
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
          <Pressable onPress={() => router.back()}>
            <Text style={styles.back}>← Geri</Text>
          </Pressable>

          <Text style={styles.heading}>Deneyimini değerlendir</Text>
          <Text style={styles.subtitle}>Yorumun başkalarına yardımcı olur.</Text>

          <View style={styles.starsRow}>
            {[1, 2, 3, 4, 5].map((s) => (
              <Pressable key={s} onPress={() => setRating(s)} style={styles.starBtn}>
                <Text style={styles.starText}>{s <= rating ? '★' : '☆'}</Text>
              </Pressable>
            ))}
          </View>
          <Text style={styles.ratingLabel}>{ratingLabel(rating)}</Text>

          <Input
            label="Yorum (opsiyonel)"
            value={body}
            onChangeText={setBody}
            placeholder="Deneyimini paylaş..."
            multiline
            numberOfLines={6}
            maxLength={1000}
            style={styles.bodyInput}
          />

          <Text style={styles.helper}>
            Yorumlar moderasyona girer. Küfür/spam içeriyorsa pending kalır, admin onayı gerekir.
          </Text>

          <Button label="Gönder" onPress={handleSubmit} loading={loading} disabled={rating < 1} />
        </View>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

function ratingLabel(rating: number): string {
  switch (rating) {
    case 1: return 'Çok kötü';
    case 2: return 'Kötü';
    case 3: return 'Fena değil';
    case 4: return 'İyi';
    case 5: return 'Mükemmel';
    default: return '';
  }
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  flex: { flex: 1 },
  content: { flex: 1, padding: Spacing.lg, gap: Spacing.md },
  back: { color: Colors.primary, fontSize: FontSize.sm, marginBottom: Spacing.sm },
  heading: { fontSize: FontSize.xxl, fontWeight: '700', color: Colors.text },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginBottom: Spacing.md },
  starsRow: { flexDirection: 'row', justifyContent: 'center', gap: Spacing.xs, marginVertical: Spacing.md },
  starBtn: { padding: Spacing.xs },
  starText: { fontSize: 48, color: Colors.accent },
  ratingLabel: { textAlign: 'center', fontSize: FontSize.lg, color: Colors.text, fontWeight: '600' },
  bodyInput: { minHeight: 120, textAlignVertical: 'top' },
  helper: { fontSize: FontSize.xs, color: Colors.textMuted, marginVertical: Spacing.sm },
});
