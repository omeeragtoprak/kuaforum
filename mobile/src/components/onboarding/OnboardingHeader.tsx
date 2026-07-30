import { View, Text, StyleSheet, Pressable } from 'react-native';
import { Colors, FontSize, FontWeight, Spacing, Radius, Layout } from '@/lib/theme';

type OnboardingHeaderProps = {
  step: 1 | 2 | 3;
  onBack: () => void;
  title: string;
  subtitle?: string;
};

/** Sihirbazın 3 adımında ortak başlık + "Adım X/3" göstergesi + geri. */
export function OnboardingHeader({ step, onBack, title, subtitle }: OnboardingHeaderProps) {
  return (
    <View style={styles.container}>
      <View style={styles.topRow}>
        <Pressable
          onPress={onBack}
          hitSlop={8}
          style={styles.backBtn}
          accessibilityRole="button"
          accessibilityLabel="Geri"
        >
          <Text style={styles.back}>← Geri</Text>
        </Pressable>
        <Text style={styles.stepLabel}>Adım {step}/3</Text>
      </View>

      <View
        style={styles.progressTrack}
        accessible
        accessibilityLabel={`Onboarding ilerlemesi: ${step}. adım, toplam 3 adım`}
      >
        {[1, 2, 3].map((i) => (
          <View key={i} style={[styles.progressSeg, i <= step && styles.progressSegActive]} />
        ))}
      </View>

      <Text style={styles.title}>{title}</Text>
      {subtitle ? <Text style={styles.subtitle}>{subtitle}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { marginBottom: Spacing.lg },
  topRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', minHeight: Layout.tapTargetMin },
  backBtn: { paddingVertical: 6, paddingRight: Spacing.sm },
  back: { color: Colors.primary, fontSize: FontSize.sm, fontWeight: FontWeight.medium },
  stepLabel: { fontSize: FontSize.xs, color: Colors.textMuted, fontWeight: FontWeight.semibold },
  progressTrack: { flexDirection: 'row', gap: 6, marginTop: Spacing.sm, marginBottom: Spacing.lg },
  progressSeg: { flex: 1, height: 4, borderRadius: Radius.full, backgroundColor: Colors.border },
  progressSegActive: { backgroundColor: Colors.primary },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 6, lineHeight: 20 },
});
