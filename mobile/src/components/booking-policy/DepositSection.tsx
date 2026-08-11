import { StyleSheet, Switch, Text, View } from 'react-native';
import { Card } from '@/components/Card';
import { Input } from '@/components/Input';
import { Icon } from '@/components/Icon';
import { Colors, FontSize, FontWeight, Radius, Spacing } from '@/lib/theme';
import { DepositPercentBounds, formatPercent } from '@/lib/booking-policy';

type DepositSectionProps = {
  depositEnabled: boolean;
  onToggleDeposit: (value: boolean) => void;
  percentText: string;
  onChangePercentText: (text: string) => void;
  onBlurPercent: () => void;
  percentError?: string;
  bounds: DepositPercentBounds;
  showMasterSwitchNotice: boolean;
};

/** RFC-013 C5b — "Kapora" kartı: aç/kapa + oran girişi + RFC-009 master-switch bilgi kutusu. */
export function DepositSection({
  depositEnabled,
  onToggleDeposit,
  percentText,
  onChangePercentText,
  onBlurPercent,
  percentError,
  bounds,
  showMasterSwitchNotice,
}: DepositSectionProps) {
  return (
    <>
      <Text style={styles.sectionTitle}>Kapora</Text>
      <Card style={{ gap: Spacing.md }}>
        <View style={styles.switchRow}>
          <View style={{ flex: 1, paddingRight: Spacing.md }}>
            <Text style={styles.rowLabel}>Kapora ödemesi</Text>
            <Text style={styles.rowHint}>
              Müşteri randevu alırken belirlediğin yüzdeyi online öder, kalanını salonda ödersiniz.
            </Text>
          </View>
          <Switch
            value={depositEnabled}
            onValueChange={onToggleDeposit}
            trackColor={{ true: Colors.success, false: Colors.switchTrackOff }}
            accessibilityRole="switch"
            accessibilityLabel="Kapora ödemesi"
            accessibilityState={{ checked: depositEnabled }}
          />
        </View>

        {depositEnabled ? (
          <Input
            label="Kapora oranı"
            required
            value={percentText}
            onChangeText={onChangePercentText}
            onBlur={onBlurPercent}
            keyboardType="decimal-pad"
            rightSlot={<Text style={styles.unitText}>%</Text>}
            hint={`İzin verilen aralık: %${formatPercent(bounds.depositPercentMin)} – %${formatPercent(bounds.depositPercentMax)}`}
            error={percentError}
          />
        ) : null}

        {showMasterSwitchNotice ? (
          <View style={styles.noticeBox} accessibilityRole="text">
            <Icon name="info" size={14} color={Colors.warning} />
            <Text style={styles.noticeText}>
              Online ödeme platform genelinde henüz kapalı — ayarın kaydedildi, ödeme açıldığında otomatik devreye girecek.
            </Text>
          </View>
        ) : null}
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
  unitText: { fontSize: FontSize.md, color: Colors.textMuted, fontWeight: FontWeight.semibold, paddingHorizontal: 4 },
  noticeBox: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: 6,
    backgroundColor: Colors.warningSoft,
    borderRadius: Radius.md,
    padding: Spacing.md,
    borderWidth: 1,
    borderColor: Colors.warning,
  },
  noticeText: { flex: 1, fontSize: FontSize.xs, color: Colors.text, lineHeight: 18 },
});
