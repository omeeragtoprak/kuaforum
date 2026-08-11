import { Pressable, Text, StyleSheet, ActivityIndicator, ViewStyle, View } from 'react-native';
import { Colors, Radius, Spacing, FontSize, FontWeight, Layout } from '@/lib/theme';

/**
 * "Vitrin" pressed-hâli: opaklık düşürme YOK (metin/ikon da soluklaşıyor, istenmeyen).
 * Bunun yerine zemin hafifçe koyulaşır. theme.ts'e yeni bir "pressed" tonu eklemek
 * yerine (dokunma-durumu kalıcı bir tasarım tokenı değil, etkileşim detayıdır) mevcut
 * tondan runtime'da türetiyoruz — ham hex YAZMAmış oluyoruz, theme tek kaynak kalıyor.
 */
function darken(hex: string, amount: number): string {
  const n = parseInt(hex.slice(1), 16);
  const r = Math.round(((n >> 16) & 255) * (1 - amount));
  const g = Math.round(((n >> 8) & 255) * (1 - amount));
  const b = Math.round((n & 255) * (1 - amount));
  return `rgb(${r}, ${g}, ${b})`;
}

const PRESSED_BG = {
  primary: darken(Colors.ink, 0.22),
  secondary: darken(Colors.surfaceAlt, 0.06),
  outline: Colors.surfaceAlt,
  ghost: Colors.surfaceAlt,
  destructive: darken(Colors.error, 0.15),
};

type Variant = 'primary' | 'secondary' | 'ghost' | 'destructive' | 'outline';
type Size = 'sm' | 'md' | 'lg';

type ButtonProps = {
  label: string;
  onPress?: () => void;
  variant?: Variant;
  size?: Size;
  loading?: boolean;
  disabled?: boolean;
  icon?: React.ReactNode;
  fullWidth?: boolean;
  style?: ViewStyle;
  testID?: string;
};

export function Button({
  label,
  onPress,
  variant = 'primary',
  size = 'md',
  loading,
  disabled,
  icon,
  fullWidth = true,
  style,
  testID,
}: ButtonProps) {
  const isDisabled = disabled || loading;
  const sizeStyle = size === 'sm' ? styles.sizeSm : size === 'lg' ? styles.sizeLg : styles.sizeMd;
  const textColor = variant === 'primary' || variant === 'destructive'
    ? Colors.textInverse
    : Colors.ink;

  return (
    <Pressable
      onPress={onPress}
      disabled={isDisabled}
      testID={testID}
      style={({ pressed }) => [
        styles.base,
        sizeStyle,
        styles[variant],
        fullWidth && { alignSelf: 'stretch' },
        pressed && !isDisabled && { backgroundColor: PRESSED_BG[variant] },
        isDisabled && styles.disabled,
        pressed && !isDisabled && styles.pressed,
        style,
      ]}
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled: !!isDisabled, busy: !!loading }}
    >
      {loading ? (
        <ActivityIndicator color={textColor} size="small" />
      ) : (
        <View style={styles.row}>
          {icon ? <View style={styles.icon}>{icon}</View> : null}
          <Text style={[styles.label, { color: textColor }, size === 'sm' && styles.labelSm]}>
            {label}
          </Text>
        </View>
      )}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  base: {
    borderRadius: Radius.md,
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: Layout.tapTargetMin,
  },
  sizeSm: { paddingVertical: 8, paddingHorizontal: Spacing.md, minHeight: 36 },
  sizeMd: { paddingVertical: 14, paddingHorizontal: Spacing.lg, minHeight: 48 },
  sizeLg: { paddingVertical: 16, paddingHorizontal: Spacing.xl, minHeight: 56 },
  // Birincil: siyah zemin + beyaz metin, gölgesiz (Vitrin'de gölge yok — ayrım çizgi/boşlukla).
  primary: { backgroundColor: Colors.ink },
  // İkincil: dolgu + siyah metin, ÇERÇEVESİZ (önceki sürümde border vardı, kaldırıldı).
  secondary: { backgroundColor: Colors.surfaceAlt },
  outline: { backgroundColor: 'transparent', borderWidth: 1.5, borderColor: Colors.ink },
  // Üçüncül/link: çerçevesiz, ink metin.
  ghost: { backgroundColor: 'transparent' },
  destructive: { backgroundColor: Colors.error },
  disabled: { opacity: 0.4 },
  // Basılı hâl: opaklık değil hafif ölçek (geri bildirim zemin renginden gelir, bkz. PRESSED_BG).
  pressed: { transform: [{ scale: 0.98 }] },
  row: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  icon: { marginRight: 0 },
  label: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, letterSpacing: 0.2 },
  labelSm: { fontSize: FontSize.sm },
});
