import { useEffect, useState } from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { useRouter } from 'expo-router';
import { Button } from '@/components/Button';
import { Icon } from '@/components/Icon';
import { Colors, FontSize, FontWeight, Radius, Spacing } from '@/lib/theme';

type PaymentCountdownProps = {
  /** ISO datetime — ödeme penceresinin (booking + payment) dolduğu an. */
  expiresAt: string;
};

/**
 * Ödeme süresi geri sayımı. Dakikada bir günceller (setInterval 60sn) — saniye
 * hassasiyeti gerekmiyor, backend zaten 30dk otomatik iptal penceresi kullanıyor.
 * Süre dolunca uyarı metni + "Randevularım" CTA'ya döner (booking muhtemelen
 * otomatik iptal edilmiş olabilir — kullanıcı çıkmazda kalmamalı).
 */
export function PaymentCountdown({ expiresAt }: PaymentCountdownProps) {
  const router = useRouter();
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 60_000);
    return () => clearInterval(t);
  }, []);

  const expiresMs = new Date(expiresAt).getTime();
  if (Number.isNaN(expiresMs)) return null;

  const remainingMin = Math.ceil((expiresMs - now) / 60_000);

  if (remainingMin <= 0) {
    return (
      <View
        style={[styles.bar, styles.expired]}
        accessible
        accessibilityLabel="Ödeme süresi doldu — randevu otomatik iptal edilmiş olabilir"
      >
        <View style={styles.textRow}>
          <Icon name="clock" size={15} color={Colors.error} strokeWidth={1.8} />
          <Text style={styles.expiredText}>Süre doldu — randevu otomatik iptal edilmiş olabilir</Text>
        </View>
        <Button
          label="Randevularım"
          variant="outline"
          size="sm"
          fullWidth={false}
          onPress={() => router.replace('/(tabs)/bookings')}
        />
      </View>
    );
  }

  return (
    <View
      style={styles.bar}
      accessible
      accessibilityLabel={`Bu randevu için ödeme süresi: ${remainingMin} dakika kaldı`}
    >
      <View style={styles.textRow}>
        <Icon name="clock" size={15} color={Colors.info} strokeWidth={1.8} />
        <Text style={styles.text}>Bu randevu için ödeme süresi: {remainingMin} dk</Text>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  bar: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: Spacing.sm,
    paddingVertical: Spacing.sm,
    paddingHorizontal: Spacing.md,
    borderRadius: Radius.md,
    backgroundColor: Colors.infoSoft,
  },
  textRow: { flexDirection: 'row', alignItems: 'center', gap: 6, flexShrink: 1 },
  text: { fontSize: FontSize.sm, fontWeight: FontWeight.medium, color: Colors.info, flexShrink: 1 },
  expired: { backgroundColor: Colors.errorSoft },
  expiredText: { fontSize: FontSize.sm, fontWeight: FontWeight.medium, color: Colors.error, flexShrink: 1 },
});
