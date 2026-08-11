import { Pressable, StyleSheet, Text, ViewStyle } from 'react-native';
import { Colors, Radius, Spacing, FontSize, FontWeight } from '@/lib/theme';

type ChipProps = {
  label: string;
  selected?: boolean;
  onPress?: () => void;
  style?: ViewStyle;
  /** Varsayılan 'button' — mevcut ekranlardaki davranışı korur. Tek-seçim grupları (ör.
   *  "1/2/3 seç" segmented control) 'radio' geçmeli; o zaman accessibilityState 'checked'
   *  kullanır ('selected' yerine), TalkBack/VoiceOver'ın radio-group semantiğiyle eşleşsin diye. */
  accessibilityRole?: 'button' | 'radio';
  /** Varsayılan `label` — bağlamsız tek karakter/sayı etiketler (ör. "2") screen reader'da
   *  anlamsız kalır; çağıran taraf "Bekleyen talep limiti: 2" gibi bağlamlı bir metin geçebilir. */
  accessibilityLabel?: string;
};

export function Chip({ label, selected, onPress, style, accessibilityRole = 'button', accessibilityLabel }: ChipProps) {
  return (
    <Pressable
      onPress={onPress}
      // Görsel chip kompakt kalır (pill), ama dokunma alanı 44pt asgarisine hitSlop ile
      // genişletilir — ekranı büyük pill'lerle doldurmadan erişilebilirlik kuralına uyar.
      hitSlop={{ top: 10, bottom: 10, left: 4, right: 4 }}
      style={({ pressed }) => [
        styles.chip,
        selected && styles.chipSelected,
        pressed && !selected && styles.pressed,
        style,
      ]}
      accessibilityRole={accessibilityRole}
      accessibilityLabel={accessibilityLabel ?? label}
      accessibilityState={accessibilityRole === 'radio' ? { checked: !!selected } : { selected: !!selected }}
    >
      <Text style={[styles.label, selected && styles.labelSelected]}>{label}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  chip: {
    paddingHorizontal: Spacing.lg,
    paddingVertical: 8,
    borderRadius: Radius.full,
    backgroundColor: Colors.surfaceAlt,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  chipSelected: { backgroundColor: Colors.ink, borderColor: Colors.ink },
  // Seçili chip zaten koyu zeminde; basılıya opaklıkla soldurmak yerine seçili-olmayan
  // (açık zeminli) chip'te hafif koyulaşma yeterli geri bildirim — Button/Card ile aynı ilke.
  pressed: { backgroundColor: Colors.border },
  label: { color: Colors.textSecondary, fontSize: FontSize.sm, fontWeight: FontWeight.medium },
  labelSelected: { color: Colors.textInverse },
});
