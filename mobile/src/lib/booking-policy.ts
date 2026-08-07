/**
 * RFC-013 C5b — kuaför paneli "Randevu ayarları" ekranı için saf mantık.
 * Backend kaynağı: Kuaforum.Domain.Modules.Barber.BookingPolicy.Create() +
 * Kuaforum.Application.Modules.Barber.BookingPolicyCommands (GetBookingPolicyQuery /
 * UpdateBookingPolicyCommand). Sınır sabitleri (ceiling'ler) aşağıda backend'le ELLE
 * senkron tutuluyor — OTOMATİK PIN YOK. Backend'deki PolicyConstantsPinTests yalnız iki
 * backend sabitini (BookingPolicy.MaxPendingPerCustomerCeiling ↔
 * InitiateBookingCommandHandler.MaxActivePendingBookingsPerCustomer) birbirine pin'liyor;
 * bu dosyadaki mobile kopyalarını hiçbir test backend'e karşı doğrulamıyor. Backend'de
 * aralık değişirse (ör. MinDaysBetweenBookingsCeiling) buradaki sabit de elle güncellenmeli
 * — unutulursa kullanıcı istemci tarafında yanlış aralıkla erken reddedilir/kabul edilir,
 * sunucu 400'ü yine de son sözü söyler ama UX kötüleşir. Sunucu her zaman son sözü söyler
 * (400 mesajı ekrana aynen basılır) — bu dosya yalnız erken/inline geri bildirim içindir.
 */

/** Backend GET/PUT /v1/owner/barber-shop/booking-policy response şekli (alan adları koddan doğrulandı). */
export type BookingPolicyDto = {
  depositEnabled: boolean;
  depositPercent: number | null;
  effectiveDepositActive: boolean;
  maxPendingPerCustomer: number;
  minDaysBetweenBookings: number | null;
  maxBookingsPerMonth: number | null;
  /** RFC-014 §2 D2 — varsayılan false. Kapora'nın aksine global bir master-switch'e tabi değil. */
  autoConfirmEnabled: boolean;
  depositPercentMin: number;
  depositPercentMax: number;
};

/** Admin tarafından ayarlanan kapora oranı aralığı (PlatformSettings'ten gelir). */
export type DepositPercentBounds = {
  depositPercentMin: number;
  depositPercentMax: number;
};

/** Ekranın form state'i — açık/kapalı toggle'lar değerden AYRI tutulur ki
 *  "kapalıyken eski değer kaybolmasın, tekrar açınca geri gelsin" mümkün olsun. */
export type BookingPolicyDraft = {
  depositEnabled: boolean;
  /** Fraction — 0.15 = %15. Yalnız depositEnabled iken anlamlı. */
  depositPercent: number | null;
  maxPendingPerCustomer: number | null;
  minDaysBetweenBookingsEnabled: boolean;
  minDaysBetweenBookings: number | null;
  maxBookingsPerMonthEnabled: boolean;
  maxBookingsPerMonth: number | null;
  /** RFC-014 §2 D2 — toggle kendi başına anlamlı (opsiyonel limitlerin aksine altında bir sayı yok, ayrı bir "enabled" state gerekmiyor). */
  autoConfirmEnabled: boolean;
};

/** Backend'deki BookingPolicy sabitleriyle pin'li (bkz. dosya başı yorum). */
export const MAX_PENDING_PER_CUSTOMER_CEILING = 3;
export const MIN_DAYS_BETWEEN_BOOKINGS_CEILING = 365;
export const MAX_BOOKINGS_PER_MONTH_CEILING = 100;

export function draftFromDto(dto: BookingPolicyDto): BookingPolicyDraft {
  return {
    depositEnabled: dto.depositEnabled,
    depositPercent: dto.depositPercent,
    maxPendingPerCustomer: dto.maxPendingPerCustomer,
    minDaysBetweenBookingsEnabled: dto.minDaysBetweenBookings !== null,
    minDaysBetweenBookings: dto.minDaysBetweenBookings,
    maxBookingsPerMonthEnabled: dto.maxBookingsPerMonth !== null,
    maxBookingsPerMonth: dto.maxBookingsPerMonth,
    autoConfirmEnabled: dto.autoConfirmEnabled,
  };
}

/** PUT body şekli — kapalı toggle'ların değerini null'a normalize eder (backend de aynı davranışı domain'de yapıyor, ama request body'yi net tutmak için burada da yapılır). */
export type BookingPolicyPayload = {
  depositEnabled: boolean;
  depositPercent: number | null;
  maxPendingPerCustomer: number | null;
  minDaysBetweenBookings: number | null;
  maxBookingsPerMonth: number | null;
  autoConfirmEnabled: boolean;
};

export function draftToPayload(draft: BookingPolicyDraft): BookingPolicyPayload {
  return {
    depositEnabled: draft.depositEnabled,
    depositPercent: draft.depositEnabled ? draft.depositPercent : null,
    maxPendingPerCustomer: draft.maxPendingPerCustomer,
    minDaysBetweenBookings: draft.minDaysBetweenBookingsEnabled ? draft.minDaysBetweenBookings : null,
    maxBookingsPerMonth: draft.maxBookingsPerMonthEnabled ? draft.maxBookingsPerMonth : null,
    autoConfirmEnabled: draft.autoConfirmEnabled,
  };
}

export type BookingPolicyFieldErrors = {
  depositPercent?: string;
  maxPendingPerCustomer?: string;
  minDaysBetweenBookings?: string;
  maxBookingsPerMonth?: string;
};

/** %'lik değeri okunur şekilde biçimlendirir (float artığı olmadan): 0.30000000000000004 -> "30". */
export function formatPercent(fraction: number): string {
  return String(Math.round(fraction * 10000) / 100);
}

/**
 * Ondalık sayı TextInput'u için canlı-yazarken maske (review I1/I2 bulguları).
 * - TR klavyede ondalık ayırıcı VİRGÜLDÜR ("12,5") — sessizce noktaya çevrilir, aksi halde
 *   parseFloat("12,5") NaN döner ve kullanıcı "aralık dışı" sanılan anlaşılmaz bir hata görür.
 * - İkinci (ve sonraki) ondalık ayırıcılar METİNDEN atılır ("1.2.3" -> "1.23") — yalnız
 *   parseFloat'ta değil, EKRANDA GÖRÜNEN metinde de düzeltilir ki gösterilen değerle
 *   kaydedilecek değer arasında sessiz bir fark oluşmasın.
 * - Ondalık basamak sayısı `maxDecimals` ile KIRPILIR (yuvarlanmaz) — backend'in
 *   decimal(5,4) kapora alanı yüzde cinsinden 2 basamağa karşılık gelir.
 */
export function sanitizeDecimalText(raw: string, maxDecimals: number): string {
  let s = raw.replace(/,/g, '.').replace(/[^\d.]/g, '');
  const firstDot = s.indexOf('.');
  if (firstDot !== -1) {
    // İlk noktadan sonraki her ek nokta silinir (yeni bir ondalık ayırıcı değil, yazım hatası sayılır).
    s = s.slice(0, firstDot + 1) + s.slice(firstDot + 1).replace(/\./g, '');
    const [intPart, decPart] = s.split('.');
    if (decPart.length > maxDecimals) s = `${intPart}.${decPart.slice(0, maxDecimals)}`;
  }
  return s;
}

/** sanitizeDecimalText çıktısını sayıya çevirir; boş/yalnız-nokta/parse edilemeyen -> null. */
export function parseDecimalOrNull(sanitized: string): number | null {
  if (sanitized.length === 0 || sanitized === '.') return null;
  const n = parseFloat(sanitized);
  return Number.isNaN(n) ? null : n;
}

/**
 * Backend BookingPolicy.Create() ile aynı kurallar, alan bazlı hata mesajıyla (inline validation —
 * "errors near field", tek bir toplu mesaj yerine). Kapalı bir toggle'ın altındaki alan hiç
 * denetlenmez (kapora kapalıyken yüzde yok sayılır; opsiyonel limitler kapalıyken yok sayılır).
 */
export function validateBookingPolicy(
  draft: BookingPolicyDraft,
  bounds: DepositPercentBounds,
): BookingPolicyFieldErrors {
  const errors: BookingPolicyFieldErrors = {};

  if (draft.depositEnabled) {
    const p = draft.depositPercent;
    if (p === null || Number.isNaN(p) || p < bounds.depositPercentMin || p > bounds.depositPercentMax) {
      errors.depositPercent =
        `Kapora oranı %${formatPercent(bounds.depositPercentMin)} ile %${formatPercent(bounds.depositPercentMax)} arasında olmalı.`;
    }
  }

  const pending = draft.maxPendingPerCustomer;
  if (
    pending === null ||
    Number.isNaN(pending) ||
    !Number.isInteger(pending) ||
    pending < 1 ||
    pending > MAX_PENDING_PER_CUSTOMER_CEILING
  ) {
    errors.maxPendingPerCustomer = `Bekleyen randevu limiti 1 ile ${MAX_PENDING_PER_CUSTOMER_CEILING} arasında olmalı.`;
  }

  if (draft.minDaysBetweenBookingsEnabled) {
    const d = draft.minDaysBetweenBookings;
    if (d === null || Number.isNaN(d) || !Number.isInteger(d) || d < 1 || d > MIN_DAYS_BETWEEN_BOOKINGS_CEILING) {
      errors.minDaysBetweenBookings = `Randevular arası minimum gün 1 ile ${MIN_DAYS_BETWEEN_BOOKINGS_CEILING} arasında olmalı.`;
    }
  }

  if (draft.maxBookingsPerMonthEnabled) {
    const m = draft.maxBookingsPerMonth;
    if (m === null || Number.isNaN(m) || !Number.isInteger(m) || m < 1 || m > MAX_BOOKINGS_PER_MONTH_CEILING) {
      errors.maxBookingsPerMonth = `Aylık randevu limiti 1 ile ${MAX_BOOKINGS_PER_MONTH_CEILING} arasında olmalı.`;
    }
  }

  return errors;
}

export function isBookingPolicyValid(draft: BookingPolicyDraft, bounds: DepositPercentBounds): boolean {
  return Object.keys(validateBookingPolicy(draft, bounds)).length === 0;
}

/**
 * Kaydet butonunun disabled/enabled durumu için: sunucudaki son bilinen halle karşılaştırır.
 *
 * M12 review bulgusu: yalnız draftToPayload çıktısını karşılaştırmak YETMEZ. Opsiyonel bir
 * limit toggle'ı AÇILIP altındaki sayı henüz girilmediyse (değer hâlâ null), payload'da o alan
 * yine null kalır — toggle açık/kapalı farkı payload'da "görünmez" olur, Kaydet gri kalır ve
 * kullanıcı "neden kaydedemiyorum" der. Bu yüzden toggle bayrakları (minDaysBetweenBookingsEnabled,
 * maxBookingsPerMonthEnabled) payload'dan BAĞIMSIZ olarak da karşılaştırılır. depositEnabled zaten
 * payload'da normalize edilmeden taşınıyor (null'a düşmüyor) — o yüzden ayrı kontrol gerekmiyor.
 */
export function hasPolicyChanges(original: BookingPolicyDto, draft: BookingPolicyDraft): boolean {
  const originalDraft = draftFromDto(original);
  if (originalDraft.minDaysBetweenBookingsEnabled !== draft.minDaysBetweenBookingsEnabled) return true;
  if (originalDraft.maxBookingsPerMonthEnabled !== draft.maxBookingsPerMonthEnabled) return true;

  const a = draftToPayload(originalDraft);
  const b = draftToPayload(draft);
  return (
    a.depositEnabled !== b.depositEnabled ||
    !percentEquals(a.depositPercent, b.depositPercent) ||
    a.maxPendingPerCustomer !== b.maxPendingPerCustomer ||
    a.minDaysBetweenBookings !== b.minDaysBetweenBookings ||
    a.maxBookingsPerMonth !== b.maxBookingsPerMonth ||
    a.autoConfirmEnabled !== b.autoConfirmEnabled
  );
}

function percentEquals(a: number | null, b: number | null): boolean {
  if (a === null || b === null) return a === b;
  return Math.round(a * 10000) === Math.round(b * 10000);
}
