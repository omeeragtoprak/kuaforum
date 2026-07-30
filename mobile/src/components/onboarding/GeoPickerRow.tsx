import { View, Text, StyleSheet, Pressable, ActivityIndicator } from 'react-native';
import { Colors, FontSize, FontWeight, Spacing, Radius } from '@/lib/theme';

export type GeoPickerRowStatus = 'idle' | 'loading' | 'ready' | 'error';

type GeoPickerRowProps = {
  label: string;
  value: string | null;
  placeholder: string;
  status: GeoPickerRowStatus;
  onPress: () => void;
  onRetry: () => void;
  disabled: boolean;
};

/** Adres adımında il/ilçe/mahalle satırı: seçili değer, yükleniyor spinner'ı veya hata + tekrar dene. */
export function GeoPickerRow({ label, value, placeholder, status, onPress, onRetry, disabled }: GeoPickerRowProps) {
  return (
    <View style={styles.wrapper}>
      <Text style={styles.label}>{label}</Text>
      {status === 'error' ? (
        <Pressable onPress={onRetry} style={styles.row} accessibilityRole="button" accessibilityLabel={`${label} yüklenemedi, tekrar dene`}>
          <Text style={styles.error}>⚠️ Yüklenemedi — tekrar dene</Text>
        </Pressable>
      ) : (
        <Pressable
          onPress={onPress}
          disabled={disabled}
          style={({ pressed }) => [styles.row, disabled && styles.rowDisabled, pressed && !disabled && styles.pressed]}
          accessibilityRole="button"
          accessibilityLabel={value ? `${label}: ${value}` : placeholder}
        >
          <Text style={value ? styles.value : styles.placeholder}>{value ?? placeholder}</Text>
          {status === 'loading' ? <ActivityIndicator size="small" color={Colors.primary} /> : <Text style={styles.chevron}>›</Text>}
        </Pressable>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: { marginBottom: Spacing.lg },
  label: { fontSize: FontSize.sm, color: Colors.textSecondary, marginBottom: 6, fontWeight: FontWeight.medium },
  row: {
    flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between',
    borderWidth: 1.5, borderColor: Colors.border, borderRadius: Radius.md,
    backgroundColor: Colors.surface, minHeight: 52, paddingHorizontal: Spacing.md,
  },
  rowDisabled: { opacity: 0.5 },
  pressed: { opacity: 0.85 },
  value: { fontSize: FontSize.md, color: Colors.text },
  placeholder: { fontSize: FontSize.md, color: Colors.textMuted },
  error: { fontSize: FontSize.sm, color: Colors.error, fontWeight: FontWeight.medium },
  chevron: { fontSize: 20, color: Colors.textMuted },
});
