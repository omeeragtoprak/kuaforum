import { View, Text, StyleSheet } from 'react-native';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { Icon } from '@/components/Icon';
import { Colors, FontSize, FontWeight, Spacing } from '@/lib/theme';
import { formatTry } from '@/lib/format';
import type { PaymentKind } from '@/api/payment';

type MockCheckoutCardProps = {
  /** null = param eksik/sayısal değil (ör. NaN) — bu durumda tutar satırı hiç render edilmez. */
  amountMinor: number | null;
  remainingAtShopMinor: number | null;
  kind: PaymentKind;
  loading: boolean;
  onSimulate: () => void;
};

/**
 * Development-only mock ödeme sağlayıcı ekranı. Backend Development ortamında gerçek
 * PSP yok — checkoutUrl `/v1/payments/mock/complete` içeriyorsa bu kart gösterilir.
 * Buton, mock complete endpoint'ini tetikleyip ödemeyi "başarılı" olarak işaretler.
 */
export function MockCheckoutCard({
  amountMinor,
  remainingAtShopMinor,
  kind,
  loading,
  onSimulate,
}: MockCheckoutCardProps) {
  return (
    <Card style={{ gap: Spacing.md }}>
      <View style={styles.titleRow}>
        <Icon name="flask" size={16} color={Colors.warning} />
        <Text style={styles.title}>Test ödemesi</Text>
      </View>
      <Text style={styles.helper}>
        Bu ortamda gerçek ödeme sağlayıcısı bağlı değil — aşağıdaki buton ödemeyi başarıyla
        tamamlanmış gibi simüle eder.
      </Text>
      {amountMinor !== null ? (
        <View style={styles.amountRow}>
          <Text style={styles.amountLabel}>Şimdi ödenecek</Text>
          <Text style={styles.amountValue}>{formatTry(amountMinor)}</Text>
        </View>
      ) : null}
      {kind === 'Deposit' && !!remainingAtShopMinor && remainingAtShopMinor > 0 ? (
        <View style={styles.amountRow}>
          <Text style={styles.amountLabel}>Salonda ödenecek</Text>
          <Text style={styles.amountValueMuted}>{formatTry(remainingAtShopMinor)}</Text>
        </View>
      ) : null}
      <Button label="Ödemeyi simüle et" onPress={onSimulate} loading={loading} />
    </Card>
  );
}

const styles = StyleSheet.create({
  titleRow: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  title: { fontSize: FontSize.lg, fontWeight: FontWeight.bold, color: Colors.text },
  helper: { fontSize: FontSize.sm, color: Colors.textSecondary, lineHeight: 20 },
  amountRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  amountLabel: { fontSize: FontSize.sm, color: Colors.textSecondary },
  amountValue: { fontSize: FontSize.xl, fontWeight: FontWeight.bold, color: Colors.text },
  amountValueMuted: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, color: Colors.textSecondary },
});
