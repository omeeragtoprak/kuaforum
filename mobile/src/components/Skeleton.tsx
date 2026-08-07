import { useEffect, useRef, useState } from 'react';
import { AccessibilityInfo, Animated, View, ViewStyle, StyleSheet } from 'react-native';
import { Colors, Radius, Spacing } from '@/lib/theme';

type SkeletonProps = {
  width?: number | `${number}%`;
  height?: number;
  radius?: number;
  style?: ViewStyle;
};

/**
 * Sistem "hareketi azalt" (reduce motion) açıksa true döner. Skeleton pulse
 * animasyonunu bu durumda çalıştırmıyoruz — statik bir plaseholder yeterli.
 * `AccessibilityInfo.isReduceMotionEnabled` Promise tabanlı olduğu için ilk
 * render'da varsayılan `false` ile başlar, sonuç gelince günceller; ayrıca
 * kullanıcı ekran açıkken ayarı değiştirirse `reduceMotionChanged` event'i dinler.
 */
function useReduceMotion(): boolean {
  const [reduceMotion, setReduceMotion] = useState(false);

  useEffect(() => {
    let mounted = true;
    AccessibilityInfo.isReduceMotionEnabled?.()
      .then((enabled) => {
        if (mounted) setReduceMotion(enabled);
      })
      .catch(() => {
        // Platform desteklemiyorsa (ör. bazı test/web ortamları) animasyonlu kalır.
      });
    const sub = AccessibilityInfo.addEventListener?.('reduceMotionChanged', setReduceMotion);
    return () => {
      mounted = false;
      sub?.remove?.();
    };
  }, []);

  return reduceMotion;
}

/**
 * Pulse-animation placeholder. Replaces "Yükleniyor…" spinners in lists with
 * shimmer cards so the user perceives faster load (UI shape appears instantly,
 * content fills in). Uses RN Animated (no extra dep) — Reanimated is overkill
 * for opacity loop. Reduce-motion açıksa animasyon durur, statik plaseholder kalır.
 */
export function Skeleton({ width = '100%', height = 16, radius = Radius.sm, style }: SkeletonProps) {
  const opacity = useRef(new Animated.Value(0.4)).current;
  const reduceMotion = useReduceMotion();

  useEffect(() => {
    if (reduceMotion) {
      opacity.setValue(0.6);
      return;
    }
    const loop = Animated.loop(
      Animated.sequence([
        Animated.timing(opacity, { toValue: 0.85, duration: 700, useNativeDriver: true }),
        Animated.timing(opacity, { toValue: 0.4, duration: 700, useNativeDriver: true }),
      ]),
    );
    loop.start();
    return () => loop.stop();
  }, [opacity, reduceMotion]);

  return (
    <Animated.View
      accessibilityLabel="Yükleniyor"
      style={[
        { width, height, borderRadius: radius, backgroundColor: Colors.surfaceAlt, opacity },
        style,
      ]}
    />
  );
}

export function SkeletonCityRow() {
  return (
    <View style={styles.cityRow}>
      <Skeleton width={44} height={44} radius={22} />
      <View style={{ flex: 1, gap: 6 }}>
        <Skeleton width={'55%'} height={16} />
        <Skeleton width={'30%'} height={12} />
      </View>
      <Skeleton width={70} height={22} radius={Radius.full} />
    </View>
  );
}

export function SkeletonShopCard() {
  return (
    <View style={styles.shopCard}>
      <View style={styles.shopHeader}>
        <View style={{ flex: 1, gap: 6 }}>
          <Skeleton width={'70%'} height={18} />
          <Skeleton width={'45%'} height={12} />
        </View>
        <Skeleton width={56} height={20} radius={Radius.full} />
      </View>
      <Skeleton width={'90%'} height={12} />
      <Skeleton width={'75%'} height={12} />
      <View style={styles.shopFooter}>
        <Skeleton width={70} height={12} />
        <Skeleton width={60} height={14} />
      </View>
    </View>
  );
}

export function SkeletonBookingRow() {
  return (
    <View style={styles.bookingRow}>
      <View style={{ flex: 1, gap: 8 }}>
        <Skeleton width={'40%'} height={14} />
        <Skeleton width={'60%'} height={12} />
        <Skeleton width={'50%'} height={12} />
      </View>
      <Skeleton width={64} height={22} radius={Radius.full} />
    </View>
  );
}

/**
 * G11 fix: kuaför detay ekranı yüklenirken artık ekranın tamamı boş + düz
 * "Yükleniyor…" metni değil, hedef içeriğin gerçek şekline (hero + hizmet
 * listesi + stilist chip'leri) yakın bir iskelet gösteriyor.
 */
export function SkeletonBarberDetail() {
  return (
    <View style={styles.detailContainer}>
      <View style={styles.detailHero}>
        <Skeleton width={64} height={64} radius={32} />
        <Skeleton width={'60%'} height={22} style={{ marginTop: Spacing.md }} />
        <Skeleton width={'85%'} height={14} style={{ marginTop: 8 }} />
        <View style={{ flexDirection: 'row', gap: 8, marginTop: Spacing.md }}>
          <Skeleton width={80} height={22} radius={Radius.full} />
          <Skeleton width={70} height={22} radius={Radius.full} />
        </View>
      </View>
      <View style={styles.detailSection}>
        <Skeleton width={100} height={16} />
        {[0, 1, 2].map((i) => (
          <View key={i} style={styles.detailServiceRow}>
            <Skeleton width={24} height={24} radius={12} />
            <View style={{ flex: 1, gap: 6 }}>
              <Skeleton width={'55%'} height={14} />
              <Skeleton width={'30%'} height={11} />
            </View>
            <Skeleton width={50} height={14} />
          </View>
        ))}
      </View>
      <View style={styles.detailSection}>
        <Skeleton width={70} height={16} />
        <View style={{ flexDirection: 'row', gap: 8, marginTop: Spacing.sm }}>
          <Skeleton width={90} height={36} radius={Radius.sm} />
          <Skeleton width={90} height={36} radius={Radius.sm} />
        </View>
      </View>
    </View>
  );
}

/**
 * G15 fix (booking/[id].tsx): randevu detay ekranı yüklenirken artık düz
 * "Yükleniyor…" metni değil, hedef içeriğin gerçek şekline (başlık+durum rozeti
 * + detay satırları + hizmet satırları + aksiyon butonları) yakın bir iskelet
 * gösteriyor. `SkeletonBarberDetail` ile aynı container/section stillerini
 * paylaşır — yeni ağır bağımlılık yok.
 */
export function SkeletonBookingDetail() {
  return (
    <View style={styles.detailContainer}>
      <View style={styles.detailHero}>
        <Skeleton width={90} height={22} radius={Radius.full} />
        <Skeleton width={'55%'} height={22} style={{ marginTop: Spacing.md }} />
        <Skeleton width={'40%'} height={14} style={{ marginTop: 8 }} />
      </View>
      <View style={styles.detailSection}>
        <Skeleton width={60} height={16} />
        {[0, 1, 2, 3].map((i) => (
          <View key={i} style={styles.detailServiceRow}>
            <Skeleton width={'35%'} height={13} />
            <View style={{ flex: 1 }} />
            <Skeleton width={'25%'} height={13} />
          </View>
        ))}
      </View>
      <View style={styles.detailSection}>
        <Skeleton width={80} height={16} />
        {[0, 1].map((i) => (
          <View key={i} style={styles.detailServiceRow}>
            <View style={{ flex: 1, gap: 6 }}>
              <Skeleton width={'55%'} height={14} />
              <Skeleton width={'30%'} height={11} />
            </View>
            <Skeleton width={50} height={14} />
          </View>
        ))}
      </View>
      <View style={{ gap: Spacing.sm, marginTop: Spacing.sm }}>
        <Skeleton width={'100%'} height={48} radius={Radius.md} />
        <Skeleton width={'100%'} height={48} radius={Radius.md} />
      </View>
    </View>
  );
}

export function SkeletonList({ count, render }: { count: number; render: (i: number) => React.ReactNode }) {
  return (
    <View style={{ gap: Spacing.md }}>
      {Array.from({ length: count }).map((_, i) => (
        <View key={i}>{render(i)}</View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  cityRow: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: Colors.surface,
    borderRadius: Radius.lg,
    padding: Spacing.md,
    gap: Spacing.md,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  shopCard: {
    backgroundColor: Colors.surface,
    borderRadius: Radius.lg,
    padding: Spacing.md,
    gap: Spacing.sm,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  shopHeader: { flexDirection: 'row', alignItems: 'flex-start', gap: Spacing.sm },
  shopFooter: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    paddingTop: Spacing.sm,
    borderTopWidth: 1,
    borderTopColor: Colors.divider,
  },
  bookingRow: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: Colors.surface,
    borderRadius: Radius.lg,
    padding: Spacing.md,
    gap: Spacing.md,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  detailContainer: { paddingHorizontal: Spacing.lg, paddingTop: Spacing.md, gap: Spacing.xl },
  detailHero: { alignItems: 'flex-start' },
  detailSection: { gap: Spacing.md },
  detailServiceRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.md,
    backgroundColor: Colors.surface,
    borderRadius: Radius.md,
    padding: Spacing.md,
    borderWidth: 1,
    borderColor: Colors.border,
  },
});
