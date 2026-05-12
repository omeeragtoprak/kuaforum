import React from 'react';
import { View, Text, Pressable, StyleSheet, ScrollView } from 'react-native';
import { Colors, FontSize, FontWeight, Radius, Spacing } from '@/lib/theme';
import { ErrorReporter } from '@/lib/error-reporter';

type Props = { children: React.ReactNode };
type State = { error: Error | null };

/**
 * Top-level safety net: yakalanmamış JS exception'ları kullanıcıya "beyaz ekran"
 * yerine açıklayıcı bir UI ile sunar. componentDidCatch ErrorReporter'a bildirir
 * (V1.5 Sentry'e yönlenir). "Tekrar dene" boundary state'ini sıfırlar — kullanıcı
 * uygulamayı kapatmadan toparlanabilir.
 */
export class AppErrorBoundary extends React.Component<Props, State> {
  constructor(props: Props) {
    super(props);
    this.state = { error: null };
  }

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: React.ErrorInfo) {
    ErrorReporter.capture(error, {
      tag: 'react.boundary',
      extra: { componentStack: info.componentStack ?? null },
    });
  }

  reset = () => this.setState({ error: null });

  render() {
    if (this.state.error) {
      return (
        <View style={styles.container}>
          <ScrollView contentContainerStyle={styles.scroll}>
            <Text style={styles.emoji}>😟</Text>
            <Text style={styles.title}>Bir aksilik oldu</Text>
            <Text style={styles.subtitle}>
              Uygulama beklenmedik bir hatayla karşılaştı. "Tekrar dene"ye basabilir veya uygulamayı yeniden başlatabilirsin.
            </Text>
            <View style={styles.errorBox}>
              <Text style={styles.errorLabel}>Detay (destek için)</Text>
              <Text style={styles.errorText}>{this.state.error.message}</Text>
            </View>
            <Pressable onPress={this.reset} style={styles.btn} accessibilityRole="button">
              <Text style={styles.btnText}>Tekrar dene</Text>
            </Pressable>
          </ScrollView>
        </View>
      );
    }
    return this.props.children;
  }
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  scroll: {
    flexGrow: 1,
    justifyContent: 'center',
    paddingHorizontal: Spacing.xl,
    paddingVertical: Spacing.xxl,
    gap: Spacing.md,
    alignItems: 'center',
  },
  emoji: { fontSize: 56 },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text, textAlign: 'center' },
  subtitle: { fontSize: FontSize.md, color: Colors.textSecondary, textAlign: 'center', lineHeight: 22 },
  errorBox: {
    backgroundColor: Colors.errorSoft,
    borderRadius: Radius.md,
    padding: Spacing.md,
    width: '100%',
    gap: 4,
  },
  errorLabel: { fontSize: FontSize.xs, color: Colors.error, fontWeight: FontWeight.bold, letterSpacing: 0.5 },
  errorText: { fontSize: FontSize.sm, color: Colors.text, fontFamily: 'Courier' },
  btn: {
    backgroundColor: Colors.primary,
    borderRadius: Radius.md,
    paddingVertical: Spacing.md,
    paddingHorizontal: Spacing.xl,
    marginTop: Spacing.md,
  },
  btnText: { color: Colors.textInverse, fontSize: FontSize.md, fontWeight: FontWeight.bold },
});
