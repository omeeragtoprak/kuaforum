import { useEffect, useRef } from 'react';
import { Animated, View, ViewStyle, StyleSheet } from 'react-native';
import { Colors, Radius, Spacing } from '@/lib/theme';

type SkeletonProps = {
  width?: number | `${number}%`;
  height?: number;
  radius?: number;
  style?: ViewStyle;
};

/**
 * Pulse-animation placeholder. Replaces "Yükleniyor…" spinners in lists with
 * shimmer cards so the user perceives faster load (UI shape appears instantly,
 * content fills in). Uses RN Animated (no extra dep) — Reanimated is overkill
 * for opacity loop.
 */
export function Skeleton({ width = '100%', height = 16, radius = Radius.sm, style }: SkeletonProps) {
  const opacity = useRef(new Animated.Value(0.4)).current;

  useEffect(() => {
    const loop = Animated.loop(
      Animated.sequence([
        Animated.timing(opacity, { toValue: 0.85, duration: 700, useNativeDriver: true }),
        Animated.timing(opacity, { toValue: 0.4, duration: 700, useNativeDriver: true }),
      ]),
    );
    loop.start();
    return () => loop.stop();
  }, [opacity]);

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
});
