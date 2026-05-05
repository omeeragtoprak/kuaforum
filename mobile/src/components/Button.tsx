import { Pressable, Text, StyleSheet, ActivityIndicator, ViewStyle, View } from 'react-native';
import { Colors, Radius, Spacing, FontSize, FontWeight, Shadow, Layout } from '@/lib/theme';

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
    : variant === 'outline' || variant === 'ghost'
    ? Colors.primary
    : Colors.text;

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
  primary: { backgroundColor: Colors.primary, ...Shadow.sm },
  secondary: { backgroundColor: Colors.surfaceAlt, borderWidth: 1, borderColor: Colors.border },
  outline: { backgroundColor: 'transparent', borderWidth: 1.5, borderColor: Colors.primary },
  ghost: { backgroundColor: 'transparent' },
  destructive: { backgroundColor: Colors.error },
  disabled: { opacity: 0.4 },
  pressed: { opacity: 0.85, transform: [{ scale: 0.98 }] },
  row: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  icon: { marginRight: 0 },
  label: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, letterSpacing: 0.2 },
  labelSm: { fontSize: FontSize.sm },
});
