import { View, Text, StyleSheet } from 'react-native';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { Icon, IconName } from '@/components/Icon';
import { Colors, FontSize, FontWeight, Radius, Spacing } from '@/lib/theme';

type PaymentResultCardProps = {
  icon: IconName;
  /** Varsayılan Colors.textSecondary (nötr). Terminal fazın anlamına göre çağıran verir
   *  (ör. success → Colors.success, error/cancelled → Colors.error). */
  iconColor?: string;
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
  iconColor = Colors.textSecondary,
  title,
  message,
  primaryLabel,
  onPrimary,
  secondaryLabel,
  onSecondary,
}: PaymentResultCardProps) {
  return (
    <Card style={styles.card}>
      <View style={styles.iconWrap} accessible={false}>
        <Icon name={icon} size={28} color={iconColor} strokeWidth={1.5} />
      </View>
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
  iconWrap: {
    width: 64,
    height: 64,
    borderRadius: Radius.full,
    backgroundColor: Colors.surfaceAlt,
    alignItems: 'center',
    justifyContent: 'center',
  },
  title: { fontSize: FontSize.xl, fontWeight: FontWeight.bold, color: Colors.text, textAlign: 'center' },
  message: { fontSize: FontSize.sm, color: Colors.textSecondary, textAlign: 'center', lineHeight: 20 },
  actions: { width: '100%', gap: Spacing.sm },
});
