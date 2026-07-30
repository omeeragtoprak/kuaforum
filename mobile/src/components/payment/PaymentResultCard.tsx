import { View, Text, StyleSheet } from 'react-native';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { Colors, FontSize, FontWeight, Spacing } from '@/lib/theme';

type PaymentResultCardProps = {
  icon: string;
  title: string;
  message: string;
  primaryLabel: string;
  onPrimary: () => void;
  secondaryLabel?: string;
  onSecondary?: () => void;
};

/**
 * Ödeme akışının terminal fazları (success / cancelled / pendingWebhook / error) için
 * ortak sonuç kartı — kullanıcı hiçbir fazda çıkmazda kalmasın diye her varyant en az
 * bir CTA taşır (bkz. RFC-006 T7 spec).
 */
export function PaymentResultCard({
  icon,
  title,
  message,
  primaryLabel,
  onPrimary,
  secondaryLabel,
  onSecondary,
}: PaymentResultCardProps) {
  return (
    <Card style={styles.card}>
      <Text style={styles.icon} accessibilityElementsHidden importantForAccessibility="no-hide-descendants">
        {icon}
      </Text>
      <Text style={styles.title}>{title}</Text>
      <Text style={styles.message}>{message}</Text>
      <View style={styles.actions}>
        <Button label={primaryLabel} onPress={onPrimary} />
        {secondaryLabel && onSecondary ? (
          <Button label={secondaryLabel} onPress={onSecondary} variant="outline" />
        ) : null}
      </View>
    </Card>
  );
}

const styles = StyleSheet.create({
  card: { gap: Spacing.md, alignItems: 'center' },
  icon: { fontSize: 56 },
  title: { fontSize: FontSize.xl, fontWeight: FontWeight.bold, color: Colors.text, textAlign: 'center' },
  message: { fontSize: FontSize.sm, color: Colors.textSecondary, textAlign: 'center', lineHeight: 20 },
  actions: { width: '100%', gap: Spacing.sm },
});
