import { ScrollView, View, StyleSheet, RefreshControl, ViewStyle } from 'react-native';
import { SafeAreaView, Edge } from 'react-native-safe-area-context';
import { Colors, Layout, Spacing } from '@/lib/theme';

type ScreenProps = {
  children: React.ReactNode;
  scroll?: boolean;
  edges?: Edge[];
  refreshing?: boolean;
  onRefresh?: () => void;
  contentStyle?: ViewStyle;
  background?: string;
  padded?: boolean;
};

export function Screen({
  children,
  scroll = true,
  edges = ['top', 'bottom'],
  refreshing,
  onRefresh,
  contentStyle,
  background = Colors.background,
  padded = true,
}: ScreenProps) {
  const Content = (
    <View
      style={[
        padded && styles.padded,
        { width: '100%', maxWidth: Layout.maxContentWidth, alignSelf: 'center' },
        contentStyle,
      ]}
    >
      {children}
    </View>
  );

  return (
    <SafeAreaView style={[styles.flex, { backgroundColor: background }]} edges={edges}>
      {scroll ? (
        <ScrollView
          style={styles.flex}
          contentContainerStyle={styles.scrollContent}
          keyboardShouldPersistTaps="handled"
          showsVerticalScrollIndicator={false}
          refreshControl={
            onRefresh ? (
              <RefreshControl refreshing={!!refreshing} onRefresh={onRefresh} tintColor={Colors.primary} />
            ) : undefined
          }
        >
          {Content}
        </ScrollView>
      ) : (
        <View style={styles.flex}>{Content}</View>
      )}
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  scrollContent: { paddingBottom: Spacing.xl, flexGrow: 1 },
  padded: { paddingHorizontal: Layout.screenPaddingX },
});
