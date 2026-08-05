export type ConsentGateRole = 'Customer' | 'BarberOwner' | 'Stylist' | 'Admin';

export type ConsentGateUser = {
  role: ConsentGateRole;
  requiresTosConsent?: boolean;
};

/**
 * RFC-008 — tek doğruluk kaynağı için tek gate: hem `app/_layout.tsx`'teki AuthGate'in
 * reactive redirect'i hem OTP-verify/dev-login sonrası explicit navigasyon bu saf
 * fonksiyonu çağırır. `requiresTosConsent` true ise rol fark etmeksizin `/consent`'e
 * (KVKK m.5 açık rıza olmadan uygulamaya geçilemez); değilse mevcut rol-bazlı hedefe
 * (barber paneli / customer tabs) — davranış RFC-006/007'deki mevcut rol yönlendirmesiyle
 * birebir aynı, buraya sadece taşındı.
 *
 * RFC-010 — `redirectTo` (misafir randevu kapısı: login öncesi hedeflenen path+query,
 * örn. `/booking/confirm?stylistId=...`) opsiyonel 2. parametre olarak eklendi. Sıralama
 * kuralı: consent gate'i HER ZAMAN önce çalışır — `requiresTosConsent` true ise hedef
 * `/consent`'tir, `redirectTo` kaybolmaz ama query'de taşınır (consent ekranı onayladıktan
 * sonra kendi `useLocalSearchParams`'ından okuyup bu fonksiyonu tekrar çağırır). Consent
 * gerekmiyorsa `redirectTo` varsa doğrudan oraya gidilir, yoksa eski rol-bazlı davranış.
 */
export function resolvePostAuthRoute(user: ConsentGateUser, redirectTo?: string | null): string {
  if (user.requiresTosConsent) {
    return redirectTo ? `/consent?redirectTo=${encodeURIComponent(redirectTo)}` : '/consent';
  }
  if (redirectTo) return redirectTo;
  const isBarber = user.role === 'BarberOwner' || user.role === 'Stylist';
  return isBarber ? '/(barber)/today' : '/(tabs)';
}

/**
 * RFC-010 — misafir gezinme: unauthenticated kullanıcının AuthGate tarafından login'e
 * ATILMADAN erişebileceği route grupları. `true` dönerse AuthGate hiçbir şey yapmaz —
 * route'un/ekranın kendisi (varsa) `AuthPrompt` ile karşılar (bkz. `AuthPrompt.tsx` ve onu
 * kullanan `(tabs)/bookings.tsx`, `(tabs)/notifications.tsx`, `(tabs)/profile.tsx`,
 * `booking/confirm.tsx`). `false` dönerse eski davranış aynen sürer: doğrudan
 * `/(auth)/login`'e redirect.
 *
 * Kapsam (RFC-010 "Kararlar" 1-2): keşfet/arama/kuaför-profili misafire açık;
 * randevu OLUŞTURMA adımına (booking/confirm) gelindiğinde kayıt şart — ama kullanıcıyı
 * sessizce login'e fırlatmak yerine ekranın kendisi randevu seçimlerini `redirectTo`
 * query'sinde taşıyan bir `AuthPrompt` gösterir. `(tabs)` grubunun TAMAMI (bookings/
 * notifications/profile dahil) route seviyesinde serbest bırakılıyor çünkü hangi sekmenin
 * prompt göstereceğine ekranın kendisi karar veriyor — bu route-level allow-list o kararı
 * override ETMEZ, sadece "AuthGate redirect etmesin" der. booking/[id] (detay) ve
 * booking/payment kapsam dışı bırakıldı: guest bu ekranlara geçerli bir booking/payment id
 * olmadan zaten ulaşamaz (booking/confirm'den geçmeden oluşmazlar), deep-link edge-case'i
 * için klasik redirect-to-login davranışı yeterli.
 */
export function isGuestAccessibleRoute(segments: readonly string[]): boolean {
  const [group, sub] = segments;
  if (group === '(auth)' || group === 'legal' || group === 'consent') return true;
  if (group === '(tabs)') return true;
  if (group === 'barber') return true;
  if (group === 'booking' && sub === 'confirm') return true;
  return false;
}

/**
 * RFC-010 — `redirectTo` string'i üretmek için basit path+query builder. RN/Hermes'te
 * `URLSearchParams` polyfill bağımlılığı almamak için elle encode ediyor. `undefined`/boş
 * string değerler atlanır (ör. `stylistName` opsiyonel olabilir).
 */
export function buildRedirectPath(
  pathname: string,
  params: Record<string, string | undefined>,
): string {
  const entries = Object.entries(params).filter(
    (entry): entry is [string, string] => entry[1] !== undefined && entry[1] !== '',
  );
  if (entries.length === 0) return pathname;
  const qs = entries.map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(v)}`).join('&');
  return `${pathname}?${qs}`;
}
