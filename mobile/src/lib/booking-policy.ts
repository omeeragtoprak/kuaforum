/**
 * RFC-013 C5b — kuaför paneli "Randevu ayarları" ekranı için saf mantık.
 * Backend kaynağı: Kuaforum.Domain.Modules.Barber.BookingPolicy.Create() +
 * Kuaforum.Application.Modules.Barber.BookingPolicyCommands (GetBookingPolicyQuery /
 * UpdateBookingPolicyCommand). Sınır sabitleri (ceiling'ler) backend'le pin'lenmiş —
 * biri değişip diğeri unutulursa bu dosyadaki testler yakalamaz, backend'deki
 * PolicyConstantsPinTests yakalar; burada yalnız UI-tarafı doğrulamayı erken göstermek
 * için kopyalandı. Sunucu her zaman son sözü söyler (400 mesajı ekrana aynen basılır).
 */

/** Backend GET/PUT /v1/owner/barber-shop/booking-policy response şekli (alan adları koddan doğrulandı). */
export type BookingPolicyDto = {
  depositEnabled: boolean;
  depositPercent: number | null;
  effectiveDepositActive: boolean;
  maxPendingPerCustomer: number;
  minDaysBetweenBookings: number | null;
  maxBookingsPerMonth: number | null;
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
  };
}

/** PUT body şekli — kapalı toggle'ların değerini null'a normalize eder (backend de aynı davranışı domain'de yapıyor, ama request body'yi net tutmak için burada da yapılır). */
export type BookingPolicyPayload = {
  depositEnabled: boolean;
  depositPercent: number | null;
  maxPendingPerCustomer: number | null;
  minDaysBetweenBookings: number | null;
  maxBookingsPerMonth: number | null;
};

export function draftToPayload(draft: BookingPolicyDraft): BookingPolicyPayload {
  return {
    depositEnabled: draft.depositEnabled,
    depositPercent: draft.depositEnabled ? draft.depositPercent : null,
    maxPendingPerCustomer: draft.maxPendingPerCustomer,
    minDaysBetweenBookings: draft.minDaysBetweenBookingsEnabled ? draft.minDaysBetweenBookings : null,
    maxBookingsPerMonth: draft.maxBookingsPerMonthEnabled ? draft.maxBookingsPerMonth : null,
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

/** Kaydet butonunun disabled/enabled durumu için: sunucudaki son bilinen halle karşılaştırır. */
export function hasPolicyChanges(original: BookingPolicyDto, draft: BookingPolicyDraft): boolean {
  const a = draftToPayload(draftFromDto(original));
  const b = draftToPayload(draft);
  return (
    a.depositEnabled !== b.depositEnabled ||
    !percentEquals(a.depositPercent, b.depositPercent) ||
    a.maxPendingPerCustomer !== b.maxPendingPerCustomer ||
    a.minDaysBetweenBookings !== b.minDaysBetweenBookings ||
    a.maxBookingsPerMonth !== b.maxBookingsPerMonth
  );
}

function percentEquals(a: number | null, b: number | null): boolean {
  if (a === null || b === null) return a === b;
  return Math.round(a * 10000) === Math.round(b * 10000);
}
