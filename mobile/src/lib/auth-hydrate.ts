/**
 * RFC-008 kapanış — hydrate hata ayrımı.
 *
 * hydrate() artık OTP-verify sonrası ve consent sonrası kritik yollarda da çağrılıyor;
 * her hatada TokenStorage.clear() yapmak geçici bir network/timeout/5xx hatasını sessiz
 * logout'a çevirir. Token temizliği YALNIZ sunucunun token'ı gerçekten reddettiği
 * durumlarda (HTTP 401/403) yapılmalı; diğer tüm durumlarda (status yok = network/timeout,
 * 5xx, parse hatası) token'lar korunur ve hata ErrorReporter'a düşer.
 */
export function shouldClearTokensOnHydrateError(status?: number): boolean {
  return status === 401 || status === 403;
}
