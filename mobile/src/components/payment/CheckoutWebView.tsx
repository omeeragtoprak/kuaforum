import { View, Text, ActivityIndicator, StyleSheet } from 'react-native';
import { WebView } from 'react-native-webview';
import { Colors, FontSize, Spacing } from '@/lib/theme';

type ShouldStartLoadRequest = { url: string };

type CheckoutWebViewProps = {
  checkoutUrl: string;
  onSuccess: () => void;
  onCancel: () => void;
};

// PayTR/Iyzico gerçek checkout'u işini bitirince custom scheme'e (app.json "scheme": "kuaforum")
// redirect eder. Universal link değil, custom scheme — WebView bu navigasyonu native'e
// bırakmadan (return false) burada yakalayıp ekran state'ine çeviriyor.
const SUCCESS_PREFIX = 'kuaforum://payment/success';
const CANCEL_PREFIX = 'kuaforum://payment/cancel';

/** Gerçek PSP checkout akışı — PayTR/Iyzico'nun barındırdığı harici ödeme sayfası. */
export function CheckoutWebView({ checkoutUrl, onSuccess, onCancel }: CheckoutWebViewProps) {
  function handleShouldStartLoad(request: ShouldStartLoadRequest): boolean {
    if (request.url.startsWith(SUCCESS_PREFIX)) {
      onSuccess();
      return false;
    }
    if (request.url.startsWith(CANCEL_PREFIX)) {
      onCancel();
      return false;
    }
    return true;
  }

  return (
    <WebView
      source={{ uri: checkoutUrl }}
      onShouldStartLoadWithRequest={handleShouldStartLoad}
      startInLoadingState
      renderLoading={() => (
        <View
          style={styles.loadingOverlay}
          accessible
          accessibilityLabel="Ödeme sayfası yükleniyor"
        >
          <ActivityIndicator color={Colors.primary} size="large" />
          <Text style={styles.loadingText}>Ödeme sayfası yükleniyor…</Text>
        </View>
      )}
      style={styles.webview}
    />
  );
}

const styles = StyleSheet.create({
  webview: { flex: 1 },
  loadingOverlay: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: Colors.surface,
    gap: Spacing.sm,
  },
  loadingText: { fontSize: FontSize.sm, color: Colors.textSecondary },
});
