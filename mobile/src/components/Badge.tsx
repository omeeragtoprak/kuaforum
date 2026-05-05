import { StyleSheet, Text, View, ViewStyle } from 'react-native';
import { Colors, Radius, FontSize, FontWeight } from '@/lib/theme';

type Tone = 'neutral' | 'success' | 'warning' | 'error' | 'info' | 'brand';

type BadgeProps = {
  label: string;
  tone?: Tone;
  style?: ViewStyle;
};

const TONE_BG: Record<Tone, string> = {
  neutral: Colors.surfaceAlt,
  success: Colors.successSoft,
  warning: Colors.warningSoft,
  error: Colors.errorSoft,
  info: Colors.infoSoft,
  brand: Colors.primarySoft,
};
const TONE_FG: Record<Tone, string> = {
  neutral: Colors.textSecondary,
  success: Colors.success,
  warning: Colors.warning,
  error: Colors.error,
  info: Colors.info,
  brand: Colors.primaryDark,
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
