import { View, Text, StyleSheet } from 'react-native';
import { Colors, FontSize, Spacing } from '@/lib/theme';

type EmptyStateProps = {
  title: string;
  description?: string;
};

export function EmptyState({ title, description }: EmptyStateProps) {
  return (
    <View style={styles.container}>
      <Text style={styles.title}>{title}</Text>
      {description ? <Text style={styles.description}>{description}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { alignItems: 'center', padding: Spacing.xl },
  title: { fontSize: FontSize.lg, fontWeight: '600', color: Colors.text, marginBottom: Spacing.xs },
  description: { fontSize: FontSize.sm, color: Colors.textSecondary, textAlign: 'center' },
});
