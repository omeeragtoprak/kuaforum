export type BookingConfirmMode = 'online-payment' | 'salon-payment';

/**
 * RFC-009 ücretsiz büyüme modu — `MyProfileDto.onlinePaymentsEnabled` false iken
 * `booking/confirm.tsx` ödeme adımını tamamen atlar (kapora/tam ödeme seçim kartı gizlenir,
 * `PaymentApi.initiate` hiç çağrılmaz) — randevu `Pending` kalır, kuaför `OwnerConfirm` ile
 * onaylar (mevcut, ödeme gerektirmeyen üretim yolu — RFC-006'nın "PSP unavailable / walk-in"
 * senaryosu için zaten vardı). Flag true iken RFC-006 kapora akışı aynen çalışır.
 *
 * Server-authoritative: mobile kendi kararını vermez, profile'daki bu bool'a güvenir —
 * `requiresTosConsent` / `resolvePostAuthRoute` (RFC-008) ile aynı desen. `undefined` (henüz
 * hydrate edilmemiş profil) fail-closed olarak 'salon-payment'a düşer.
 */
export function resolveBookingConfirmMode(onlinePaymentsEnabled: boolean | undefined): BookingConfirmMode {
  return onlinePaymentsEnabled === true ? 'online-payment' : 'salon-payment';
}
