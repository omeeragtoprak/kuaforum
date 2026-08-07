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

export type BookingPromiseInput = {
  /** Salonun BookingPolicy.AutoConfirmEnabled ayarı (RFC-014 §2 D2). Varsayılan false. */
  autoConfirmEnabled: boolean;
  /** Bu randevu online ödeme (kapora veya tam) gerektiriyor mu — mobile'da confirmMode === 'online-payment' ile eşdeğer. */
  requiresPayment: boolean;
};

/**
 * RFC-014 §2 D2 madde 6 — müşteriye gösterilecek TEK doğru vaat cümlesi, salonun auto-confirm
 * ayarına ve ödeme gerekip gerekmediğine göre. Saf fonksiyon: metin dallanması JSX'e gömülmez,
 * booking/confirm.tsx (özet ekranı) ve sonuç ekranlarında aynı kaynaktan okunur.
 *
 * Kritik çıkarım kuralı (backend Booking.cs XML doc'uyla birebir): auto-confirm AÇIK + ödeme
 * gerekiyorsa booking ödeme tamamlanana kadar Pending kalır — bunun TEK anlamı "ödeme
 * bekleniyor"dur, kuaför onayı beklenmez. Bu yüzden requiresPayment=true dalında "kuaför onayı
 * beklemezsin" ifadesi açıkça yer alır — "Pending" durumunu yanlışlıkla "onay bekliyor" sanan
 * kullanıcıyı düzeltir.
 */
export function resolveBookingPromise({ autoConfirmEnabled, requiresPayment }: BookingPromiseInput): string {
  if (!autoConfirmEnabled) {
    // Varsayılan davranış (auto-confirm kapalı) — RFC-013'ün "her randevuda kuaför onayı"
    // ilkesi. Ödeme gerekip gerekmediğinden bağımsız: her iki durumda da kuaför ayrı bir
    // OwnerConfirm adımı atmadan randevu kesinleşmez.
    return 'Kuaför onayladığında bildirim alacaksın.';
  }
  if (requiresPayment) {
    return 'Ödemen alınınca randevun anında kesinleşir — kuaför onayı beklemezsin.';
  }
  return 'Randevun anında kesinleşir.';
}

/**
 * Ödeme sonrası (booking/payment.tsx 'success' fazı) doğru "kim onayladı" ifadesi. Backend
 * Booking.MarkPaymentReceived XML doc'u: AutoConfirmSnapshot true iken owner'a "onayınızı
 * bekliyor" bildirimi (BookingPaymentReceivedEvent) HİÇ YAYINLANMAZ — yani owner'ın hiçbir
 * onay aksiyonu yoktur. "Kuaförün onayladı" demek bu durumda YANLIŞ bir atıf olurdu.
 */
export function resolvePaymentSuccessMessage(autoConfirmEnabled: boolean): string {
  return autoConfirmEnabled
    ? 'Ödemen alındı, randevun otomatik onaylandı.'
    : 'Ödemen alındı ve kuaförün onayladı.';
}
