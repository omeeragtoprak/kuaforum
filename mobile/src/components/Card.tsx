import { Pressable, StyleProp, StyleSheet, View, ViewStyle } from 'react-native';
import { Colors, Radius, Spacing } from '@/lib/theme';

type CardProps = {
  children: React.ReactNode;
  onPress?: () => void;
  /**
   * Dizi de kabul eder: `style={[base, cond && extra]}` kalıbı React Native'de
   * yaygın ve koşullu vurgu (ör. "onay bekliyor" kartının kenar çizgisi) için
   * gerekli. Tek nesneye kısıtlamak çağıran ekranları derlenemez hâle getiriyordu.
   */
  style?: StyleProp<ViewStyle>;
  padded?: boolean;
  testID?: string;
  /**
   * 'outlined' (varsayılan) — mevcut davranış: `border` çizgisi + `Radius.lg`, gölge YOK.
   * 'plain' — çizgisiz; içerik yoğun listelerde (ör. bir Card içinde art arda satırlar)
   * her satırı ayrı kutuya almamak için — ayrım yalnız boşlukla yapılır. Yeni, opsiyonel;
   * varsayılan mevcut çağrıların görünümünü korur.
   */
  variant?: 'outlined' | 'plain';
};

export function Card({ children, onPress, style, padded = true, testID, variant = 'outlined' }: CardProps) {
  const baseStyle = [styles.card, variant === 'outlined' && styles.outlined, padded && styles.padded];
  if (onPress) {
    // Pressed geri bildirimi doğrudan kartın kendi zemininde (bir sarmalayıcıda değil) —
    // aksi halde `pressed` arka planı, üstünü kaplayan iç View'ın arkasında görünmez kalırdı.
    return (
      <Pressable
        onPress={onPress}
        testID={testID}
        style={({ pressed }) => [...baseStyle, pressed && styles.pressed, style]}
        accessibilityRole="button"
      >
        {children}
      </Pressable>
    );
  }
  return <View style={[...baseStyle, style]}>{children}</View>;
}

const styles = StyleSheet.create({
  // Gölge YOK — "Vitrin"de ayrım çizgi + boşlukla yapılır (theme.ts, Shadow yorumu).
  card: {
    backgroundColor: Colors.surface,
    borderRadius: Radius.lg,
  },
  outlined: {
    borderWidth: 1,
    borderColor: Colors.border,
  },
  padded: { padding: Spacing.lg },
  // Basılı hâl: opaklık yerine hafif zemin koyulaşması + ölçek — Button ile aynı ilke.
  pressed: { backgroundColor: Colors.surfaceAlt, transform: [{ scale: 0.99 }] },
});
