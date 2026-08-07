import { StyleSheet, Switch, Text, View } from 'react-native';
import { Card } from '@/components/Card';
import { Colors, FontSize, FontWeight, Radius, Spacing } from '@/lib/theme';

type AutoConfirmSectionProps = {
  autoConfirmEnabled: boolean;
  onToggle: (value: boolean) => void;
  /** Kapora açıksa ek açıklama gösterilir — RFC-014 §2 D2 madde 3: auto-confirm ödemeyi atlamaz. */
  depositEnabled: boolean;
};

/**
 * RFC-014 §2 D2 — "Otomatik onay" kartı. Varsayılan KAPALI: kuaför bilinçli olarak açar.
 * Kapora'nın aksine global bir master-switch'e tabi değil (owner açtığı an etkindir) — bu
 * yüzden DepositSection'daki "platform genelinde kapalı" bilgi kutusuna eşdeğer bir uyarı yok.
 */
export function AutoConfirmSection({ autoConfirmEnabled, onToggle, depositEnabled }: AutoConfirmSectionProps) {
  return (
    <>
      <Text style={styles.sectionTitle}>Otomatik onay</Text>
      <Card style={{ gap: Spacing.md }}>
        <View style={styles.switchRow}>
          <View style={{ flex: 1, paddingRight: Spacing.md }}>
            <Text style={styles.rowLabel}>Randevuları otomatik onayla</Text>
            <Text style={styles.rowHint}>
              Açıkken gelen randevu talepleri senin onayını beklemeden kesinleşir ve takvimini
              doğrudan doldurur. Kapalıyken (varsayılan) her talebi kendin onaylarsın.
            </Text>
          </View>
          <Switch
            value={autoConfirmEnabled}
            onValueChange={onToggle}
            trackColor={{ true: Colors.success, false: '#E5E7EB' }}
            accessibilityRole="switch"
            accessibilityLabel="Randevuları otomatik onayla"
            accessibilityState={{ checked: autoConfirmEnabled }}
          />
        </View>

        {autoConfirmEnabled && depositEnabled ? (
          <View style={styles.noticeBox} accessibilityRole="text">
            <Text style={styles.noticeText}>
              ℹ️ Kapora açık olduğu için otomatik onay ödemeyi atlamaz: randevu, müşteri ödemeyi
              tamamlayana kadar yine bekler. Ödeme geldiği an senin onayını beklemeden kesinleşir.
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
  noticeBox: {
    backgroundColor: Colors.infoSoft,
    borderRadius: Radius.md,
    padding: Spacing.md,
    borderWidth: 1,
    borderColor: Colors.info,
  },
  noticeText: { fontSize: FontSize.xs, color: Colors.text, lineHeight: 18 },
});
