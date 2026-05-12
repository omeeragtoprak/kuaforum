/**
 * Mobile error reporting facade.
 *
 * V1: console.error fallback (TestFlight/dev — Reactotron veya Expo logs'tan görünür).
 * V1.5: @sentry/react-native ekle, captureException fn'i Sentry.captureException'a bağla.
 *
 * Why: error handler call sites'i tek noktadan yönet — Sentry eklendiğinde
 * her async catch bloğunu tekrar dolaşmaya gerek kalmasın. KVKK için PII
 * maskelenir; user.id ya da telefon Sentry'e gönderilmez.
 */

type ErrorContext = {
  /** Tag/level — Sentry breadcrumb category olarak kullanılır */
  tag?: string;
  /** Non-PII metadata: route, action, endpoint vs. */
  extra?: Record<string, unknown>;
};

export const ErrorReporter = {
  /** Caught exception'ı raporla. Default: console.error. */
  capture(err: unknown, context?: ErrorContext): void {
    const tag = context?.tag ?? 'app';
    if (__DEV__) {
      console.error(`[${tag}]`, err, context?.extra ?? '');
    }
    // V1.5: Sentry.captureException(err, { tags: { area: tag }, extra: context?.extra });
  },

  /** Genel breadcrumb / info log (Sentry'e breadcrumb olarak gider). */
  breadcrumb(message: string, data?: Record<string, unknown>): void {
    if (__DEV__) {
      console.log('[breadcrumb]', message, data ?? '');
    }
    // V1.5: Sentry.addBreadcrumb({ message, data, level: 'info' });
  },
};
