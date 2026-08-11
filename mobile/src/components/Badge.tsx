import { StyleSheet, Text, View, ViewStyle } from 'react-native';
import { Colors, Radius, FontSize, FontWeight } from '@/lib/theme';

type Tone = 'neutral' | 'success' | 'warning' | 'error' | 'info' | 'brand';

type BadgeProps = {
  label: string;
  tone?: Tone;
  style?: ViewStyle;
};

/**
 * KONTRAST DÜZELTMESİ: `Colors.success` (#1E7F53) `successSoft` üzerinde 4.39:1,
 * `Colors.warning` (#9A6A12) `warningSoft` üzerinde 4.21:1 ölçüldü — ikisi de
 * WCAG AA metin eşiği olan 4.5:1'in ALTINDA (node ile hesaplandı, bkz. PR notu).
 * `error`/`info` zaten yeterli marj (5.6+). theme.ts'e dokunmadan (kapsam dışı,
 * tek-kaynak dosya) aynı tonu %14 koyulaştırıp burada kullanıyoruz — accentInk'in
 * theme.ts'te zaten kurduğu "soft zemin + koyu metin" kalıbının aynısı.
 */
function darken(hex: string, amount: number): string {
  const n = parseInt(hex.slice(1), 16);
  const r = Math.round(((n >> 16) & 255) * (1 - amount));
  const g = Math.round(((n >> 8) & 255) * (1 - amount));
  const b = Math.round((n & 255) * (1 - amount));
  return `rgb(${r}, ${g}, ${b})`;
}

const TONE_BG: Record<Tone, string> = {
  neutral: Colors.surfaceAlt,
  success: Colors.successSoft,
  warning: Colors.warningSoft,
  error: Colors.errorSoft,
  info: Colors.infoSoft,
  brand: Colors.surfaceAlt,
};
const TONE_FG: Record<Tone, string> = {
  neutral: Colors.textSecondary,
  success: darken(Colors.success, 0.14), // 4.39:1 → ~5.5:1
  warning: darken(Colors.warning, 0.14), // 4.21:1 → ~5.3:1
  error: Colors.error,
  info: Colors.info,
  brand: Colors.ink,
};

export function Badge({ label, tone = 'neutral', style }: BadgeProps) {
  return (
    <View style={[styles.badge, { backgroundColor: TONE_BG[tone] }, style]}>
      <Text style={[styles.text, { color: TONE_FG[tone] }]}>{label}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  badge: {
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: Radius.full,
    alignSelf: 'flex-start',
  },
  text: { fontSize: FontSize.xs, fontWeight: FontWeight.semibold, letterSpacing: 0.3 },
});
