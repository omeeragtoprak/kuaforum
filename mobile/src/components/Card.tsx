import { Pressable, StyleSheet, View, ViewStyle } from 'react-native';
import { Colors, Radius, Spacing, Shadow } from '@/lib/theme';

type CardProps = {
  children: React.ReactNode;
  onPress?: () => void;
  style?: ViewStyle;
  padded?: boolean;
  testID?: string;
};

export function Card({ children, onPress, style, padded = true, testID }: CardProps) {
  const content = (
    <View style={[styles.card, padded && styles.padded, style]}>{children}</View>
  );
  if (onPress) {
    return (
      <Pressable
        onPress={onPress}
        testID={testID}
        style={({ pressed }) => [pressed && styles.pressed]}
        accessibilityRole="button"
      >
        {content}
      </Pressable>
    );
  }
  return content;
}

const styles = StyleSheet.create({
  card: {
    backgroundColor: Colors.surface,
    borderRadius: Radius.lg,
    borderWidth: 1,
    borderColor: Colors.border,
    ...Shadow.sm,
  },
  padded: { padding: Spacing.lg },
  pressed: { opacity: 0.85, transform: [{ scale: 0.99 }] },
});
