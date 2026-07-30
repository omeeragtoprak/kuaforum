import { View, Text, ActivityIndicator, StyleSheet } from 'react-native';
import { WebView } from 'react-native-webview';
// WebViewErrorEvent/WebViewHttpErrorEvent paketin public index.d.ts'inde re-export edilmiyor
// (sadece FileDownload/WebViewMessageEvent/WebViewNavigation var) — lib/WebViewTypes'tan
// doğrudan alınıyor, orası bu tiplerin tanımlandığı gerçek yer.
import type { WebViewErrorEvent, WebViewHttpErrorEvent } from 'react-native-webview/lib/WebViewTypes';
import { Colors, FontSize, Spacing } from '@/lib/theme';
import { ErrorReporter } from '@/lib/error-reporter';

type ShouldStartLoadRequest = { url: string };

type CheckoutWebViewProps = {
  checkoutUrl: string;
  onSuccess: () => void;
  onCancel: () => void;
  onError: () => void;
};

// PayTR/Iyzico gerçek checkout'u işini bitirince custom scheme'e (app.json "scheme": "kuaforum")
// redirect eder. Universal link değil, custom scheme — WebView bu navigasyonu native'e
// bırakmadan (return false) burada yakalayıp ekran state'ine çeviriyor.
const SUCCESS_PREFIX = 'kuaforum://payment/success';
const CANCEL_PREFIX = 'kuaforum://payment/cancel';

// react-native-webview `originWhitelist` kontrolü `onShouldStartLoadWithRequest`'ten ÖNCE
// çalışır — 'kuaforum://' whitelist'te olmazsa custom handler'a hiç düşmeden kütüphane
// isteği OS'e (Linking.openURL) yönlendirir, yani success/cancel intercept'i ölü kod olur.
// Bu yüzden şema burada açıkça whitelist'e eklenmek zorunda.
const ORIGIN_WHITELIST = ['https://*', 'http://*', 'kuaforum://*'];

// TODO(PayTR entegrasyonu): Gerçek PSP checkout'una geçildiğinde WebView'ı yüklemeden önce
// checkoutUrl'in host'unu bilinen PayTR/Iyzico host whitelist'ine karşı doğrula — backend zaten
// güvenilir bir kaynak (initiate response) ama deep-link/deeplink-replay senaryolarında rastgele
// bir host'un checkout gibi görünüp phishing sayfası göstermesini (WebView Authorization header
// taşımasa da kullanıcıyı kandırabilir) engellemek için ek bir savunma katmanı gerekir.
/** Gerçek PSP checkout akışı — PayTR/Iyzico'nun barındırdığı harici ödeme sayfası. */
export function CheckoutWebView({ checkoutUrl, onSuccess, onCancel, onError }: CheckoutWebViewProps) {
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

  function handleLoadingError(event: WebViewErrorEvent) {
    ErrorReporter.capture(new Error(event.nativeEvent.description), {
      tag: 'booking.payment.webview-error',
      extra: { code: event.nativeEvent.code },
    });
    onError();
  }

  function handleHttpError(event: WebViewHttpErrorEvent) {
    ErrorReporter.capture(new Error(event.nativeEvent.description), {
      tag: 'booking.payment.webview-http-error',
      extra: { statusCode: event.nativeEvent.statusCode },
    });
    onError();
  }

  return (
    <WebView
      source={{ uri: checkoutUrl }}
      originWhitelist={ORIGIN_WHITELIST}
      onShouldStartLoadWithRequest={handleShouldStartLoad}
      onError={handleLoadingError}
      onHttpError={handleHttpError}
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
