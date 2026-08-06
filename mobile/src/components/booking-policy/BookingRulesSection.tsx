import { StyleSheet, Switch, Text, View } from 'react-native';
import { Card } from '@/components/Card';
import { Chip } from '@/components/Chip';
import { Input } from '@/components/Input';
import { Colors, FontSize, FontWeight, Layout, Spacing } from '@/lib/theme';
import { MAX_BOOKINGS_PER_MONTH_CEILING, MAX_PENDING_PER_CUSTOMER_CEILING, MIN_DAYS_BETWEEN_BOOKINGS_CEILING } from '@/lib/booking-policy';

const PENDING_OPTIONS = Array.from({ length: MAX_PENDING_PER_CUSTOMER_CEILING }, (_, i) => i + 1);

// Chip bileşeni birçok ekranda paylaşılıyor (arama filtreleri, keşif, hizmet cinsiyeti) —
// varsayılan ~33pt yüksekliği oralarda değiştirmek riskli olduğundan (review I4), 44pt
// dokunma hedefi yalnız BU ekrandaki kullanım için `style` override'ıyla sağlanıyor.
const chipTapTarget = { minHeight: Layout.tapTargetMin, minWidth: Layout.tapTargetMin, justifyContent: 'center' as const, alignItems: 'center' as const };

type BookingRulesSectionProps = {
  maxPendingPerCustomer: number | null;
  onSelectPending: (n: number) => void;
  pendingError?: string;

  minDaysEnabled: boolean;
  onToggleMinDays: (value: boolean) => void;
  minDaysText: string;
  onChangeMinDaysText: (text: string) => void;
  onBlurMinDays: () => void;
  minDaysError?: string;

  maxMonthlyEnabled: boolean;
  onToggleMaxMonthly: (value: boolean) => void;
  maxMonthlyText: string;
  onChangeMaxMonthlyText: (text: string) => void;
  onBlurMaxMonthly: () => void;
  maxMonthlyError?: string;
};

/** RFC-013 C5b — "Randevu kuralları" kartı: bekleyen talep limiti + iki opsiyonel anti-spam kuralı. */
export function BookingRulesSection({
  maxPendingPerCustomer,
  onSelectPending,
  pendingError,
  minDaysEnabled,
  onToggleMinDays,
  minDaysText,
  onChangeMinDaysText,
  onBlurMinDays,
  minDaysError,
  maxMonthlyEnabled,
  onToggleMaxMonthly,
  maxMonthlyText,
  onChangeMaxMonthlyText,
  onBlurMaxMonthly,
  maxMonthlyError,
}: BookingRulesSectionProps) {
  return (
    <>
      <Text style={styles.sectionTitle}>Randevu kuralları</Text>
      <Card style={{ gap: Spacing.lg }}>
        <View>
          <Text style={styles.rowLabel}>Bekleyen talep limiti</Text>
          <Text style={styles.rowHint}>
            Bir müşterinin aynı anda kaç tane onay bekleyen randevusu olabilir? Düşük tutmak spam rezervasyonu önler.
          </Text>
          <View style={styles.chipsRow} accessibilityRole="radiogroup" accessibilityLabel="Bekleyen talep limiti">
            {PENDING_OPTIONS.map((n) => (
              <Chip
                key={n}
                label={String(n)}
                selected={maxPendingPerCustomer === n}
                onPress={() => onSelectPending(n)}
                accessibilityRole="radio"
                accessibilityLabel={`Bekleyen talep limiti: ${n}`}
                style={chipTapTarget}
              />
            ))}
          </View>
          {pendingError ? <Text style={styles.errorText}>{pendingError}</Text> : null}
        </View>

        <View style={styles.divider} />

        <View>
          <View style={styles.switchRow}>
            <View style={{ flex: 1, paddingRight: Spacing.md }}>
              <Text style={styles.rowLabel}>Randevular arası minimum gün</Text>
              <Text style={styles.rowHint}>
                Aynı müşteri, önceki randevusundan en az bu kadar gün geçmeden yeni randevu alamaz.
              </Text>
            </View>
            <Switch
              value={minDaysEnabled}
              onValueChange={onToggleMinDays}
              trackColor={{ true: Colors.success, false: '#E5E7EB' }}
              accessibilityRole="switch"
              accessibilityLabel="Randevular arası minimum gün sınırı"
              accessibilityState={{ checked: minDaysEnabled }}
            />
          </View>
          {minDaysEnabled ? (
            <Input
              label="Minimum gün"
              value={minDaysText}
              onChangeText={onChangeMinDaysText}
              onBlur={onBlurMinDays}
              keyboardType="number-pad"
              rightSlot={<Text style={styles.unitText}>gün</Text>}
              hint={`1 ile ${MIN_DAYS_BETWEEN_BOOKINGS_CEILING} arasında bir değer gir`}
              error={minDaysError}
            />
          ) : null}
        </View>

        <View style={styles.divider} />

        <View>
          <View style={styles.switchRow}>
            <View style={{ flex: 1, paddingRight: Spacing.md }}>
              <Text style={styles.rowLabel}>Müşteri başına aylık limit</Text>
              <Text style={styles.rowHint}>Bir müşteri bu salondan bir ayda en fazla bu kadar randevu alabilir.</Text>
            </View>
            <Switch
              value={maxMonthlyEnabled}
              onValueChange={onToggleMaxMonthly}
              trackColor={{ true: Colors.success, false: '#E5E7EB' }}
              accessibilityRole="switch"
              accessibilityLabel="Müşteri başına aylık randevu limiti"
              accessibilityState={{ checked: maxMonthlyEnabled }}
            />
          </View>
          {maxMonthlyEnabled ? (
            <Input
              label="Aylık limit"
              value={maxMonthlyText}
              onChangeText={onChangeMaxMonthlyText}
              onBlur={onBlurMaxMonthly}
              keyboardType="number-pad"
              rightSlot={<Text style={styles.unitText}>randevu</Text>}
              hint={`1 ile ${MAX_BOOKINGS_PER_MONTH_CEILING} arasında bir değer gir`}
              error={maxMonthlyError}
            />
          ) : null}
        </View>
      </Card>
    </>
  );
}

const styles = StyleSheet.create({
  sectionTitle: {
    fontSize: FontSize.xs,
    color: Colors.textMuted,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    marginTop: Spacing.lg,
    marginBottom: Spacing.sm,
    paddingHorizontal: Spacing.xs,
    fontWeight: FontWeight.semibold,
  },
  switchRow: { flexDirection: 'row', alignItems: 'center' },
  rowLabel: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, color: Colors.text },
  rowHint: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 4, lineHeight: 16 },
  chipsRow: { flexDirection: 'row', flexWrap: 'wrap', gap: 8, marginTop: Spacing.sm },
  errorText: { color: Colors.error, fontSize: FontSize.xs, marginTop: 6, fontWeight: FontWeight.medium },
  unitText: { fontSize: FontSize.md, color: Colors.textMuted, fontWeight: FontWeight.semibold, paddingHorizontal: 4 },
  divider: { height: 1, backgroundColor: Colors.divider },
});
