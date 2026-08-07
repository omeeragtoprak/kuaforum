import {
  BookingPolicyDraft,
  BookingPolicyDto,
  draftFromDto,
  draftToPayload,
  hasPolicyChanges,
  parseDecimalOrNull,
  sanitizeDecimalText,
  validateBookingPolicy,
} from '@/lib/booking-policy';

const BOUNDS = { depositPercentMin: 0.05, depositPercentMax: 0.3 };

const BASE_DTO: BookingPolicyDto = {
  depositEnabled: false,
  depositPercent: null,
  effectiveDepositActive: false,
  maxPendingPerCustomer: 2,
  minDaysBetweenBookings: null,
  maxBookingsPerMonth: null,
  autoConfirmEnabled: false,
  depositPercentMin: BOUNDS.depositPercentMin,
  depositPercentMax: BOUNDS.depositPercentMax,
};

function baseDraft(overrides: Partial<BookingPolicyDraft> = {}): BookingPolicyDraft {
  return {
    depositEnabled: false,
    depositPercent: null,
    maxPendingPerCustomer: 2,
    minDaysBetweenBookingsEnabled: false,
    minDaysBetweenBookings: null,
    maxBookingsPerMonthEnabled: false,
    maxBookingsPerMonth: null,
    autoConfirmEnabled: false,
    ...overrides,
  };
}

describe('validateBookingPolicy', () => {
  it('aralık dışı kapora yüzdesi hata verir', () => {
    const draft = baseDraft({ depositEnabled: true, depositPercent: 0.5 });
    const errors = validateBookingPolicy(draft, BOUNDS);
    expect(errors.depositPercent).toBe('Kapora oranı %5 ile %30 arasında olmalı.');
  });

  it('kapora kapalıyken yüzde ne olursa olsun yok sayılır (aralık dışı bile olsa hata yok)', () => {
    const draft = baseDraft({ depositEnabled: false, depositPercent: 0.99 });
    const errors = validateBookingPolicy(draft, BOUNDS);
    expect(errors.depositPercent).toBeUndefined();
  });

  it('kapora açıkken yüzde girilmemişse hata verir', () => {
    const draft = baseDraft({ depositEnabled: true, depositPercent: null });
    const errors = validateBookingPolicy(draft, BOUNDS);
    expect(errors.depositPercent).toBeDefined();
  });

  it('kapora aralığın tam sınırlarında (min/max) geçerlidir', () => {
    const min = baseDraft({ depositEnabled: true, depositPercent: BOUNDS.depositPercentMin });
    const max = baseDraft({ depositEnabled: true, depositPercent: BOUNDS.depositPercentMax });
    expect(validateBookingPolicy(min, BOUNDS).depositPercent).toBeUndefined();
    expect(validateBookingPolicy(max, BOUNDS).depositPercent).toBeUndefined();
  });

  it('bekleyen randevu limiti 1-3 dışındaysa hata verir (0 ve 4 için)', () => {
    expect(validateBookingPolicy(baseDraft({ maxPendingPerCustomer: 0 }), BOUNDS).maxPendingPerCustomer).toBeDefined();
    expect(validateBookingPolicy(baseDraft({ maxPendingPerCustomer: 4 }), BOUNDS).maxPendingPerCustomer).toBeDefined();
    expect(validateBookingPolicy(baseDraft({ maxPendingPerCustomer: 3 }), BOUNDS).maxPendingPerCustomer).toBeUndefined();
  });

  it('min gün limiti kapalıyken 0 veya 999 girilse bile yok sayılır', () => {
    const draft = baseDraft({ minDaysBetweenBookingsEnabled: false, minDaysBetweenBookings: 999 });
    expect(validateBookingPolicy(draft, BOUNDS).minDaysBetweenBookings).toBeUndefined();
  });

  it('min gün limiti açıkken 366 (tavan üstü) hata verir, 365 (tavanda) geçerlidir', () => {
    const tooHigh = baseDraft({ minDaysBetweenBookingsEnabled: true, minDaysBetweenBookings: 366 });
    const atCeiling = baseDraft({ minDaysBetweenBookingsEnabled: true, minDaysBetweenBookings: 365 });
    expect(validateBookingPolicy(tooHigh, BOUNDS).minDaysBetweenBookings).toBeDefined();
    expect(validateBookingPolicy(atCeiling, BOUNDS).minDaysBetweenBookings).toBeUndefined();
  });

  it('aylık limit açıkken 101 (tavan üstü) hata verir, 100 (tavanda) geçerlidir', () => {
    const tooHigh = baseDraft({ maxBookingsPerMonthEnabled: true, maxBookingsPerMonth: 101 });
    const atCeiling = baseDraft({ maxBookingsPerMonthEnabled: true, maxBookingsPerMonth: 100 });
    expect(validateBookingPolicy(tooHigh, BOUNDS).maxBookingsPerMonth).toBeDefined();
    expect(validateBookingPolicy(atCeiling, BOUNDS).maxBookingsPerMonth).toBeUndefined();
  });
});

describe('hasPolicyChanges', () => {
  it('sunucudan gelenle birebir aynı draft için değişiklik yok der', () => {
    const draft = draftFromDto(BASE_DTO);
    expect(hasPolicyChanges(BASE_DTO, draft)).toBe(false);
  });

  it('kapora açılırsa değişiklik tespit eder', () => {
    const draft = draftFromDto(BASE_DTO);
    draft.depositEnabled = true;
    draft.depositPercent = 0.15;
    expect(hasPolicyChanges(BASE_DTO, draft)).toBe(true);
  });

  it('bekleyen randevu limiti değişirse değişiklik tespit eder', () => {
    const draft = draftFromDto(BASE_DTO);
    draft.maxPendingPerCustomer = 3;
    expect(hasPolicyChanges(BASE_DTO, draft)).toBe(true);
  });

  it('kapalı bir toggle altındaki değer değişse bile (toggle kapalı kaldığı sürece) değişiklik sayılmaz', () => {
    const draft = draftFromDto(BASE_DTO);
    // minDaysBetweenBookingsEnabled false kalıyor ama arkadaki ham değeri değiştiriyoruz —
    // draftToPayload bunu null'a normalize ettiği için sunucuya giden payload aynı kalır.
    draft.minDaysBetweenBookings = 30;
    expect(hasPolicyChanges(BASE_DTO, draft)).toBe(false);
  });

  it('float yuvarlama farkı yüzünden yanlış pozitif vermez (0.15 vs 15/100)', () => {
    const dto: BookingPolicyDto = { ...BASE_DTO, depositEnabled: true, depositPercent: 0.15 };
    const draft = draftFromDto(dto);
    draft.depositPercent = 15 / 100;
    expect(hasPolicyChanges(dto, draft)).toBe(false);
  });

  it('M12: opsiyonel toggle açılıp altındaki değer henüz girilmemişse (null) bile değişiklik sayılır', () => {
    // Payload karşılaştırması tek başına bunu KAÇIRIR çünkü draftToPayload kapalı/boş
    // toggle'ı zaten null'a normalize ediyor — iki taraf da null görünür.
    const draft = draftFromDto(BASE_DTO);
    draft.minDaysBetweenBookingsEnabled = true;
    draft.minDaysBetweenBookings = null;
    expect(hasPolicyChanges(BASE_DTO, draft)).toBe(true);
  });

  it('M12: aylık limit toggle açılıp değer boşken de değişiklik sayılır', () => {
    const draft = draftFromDto(BASE_DTO);
    draft.maxBookingsPerMonthEnabled = true;
    draft.maxBookingsPerMonth = null;
    expect(hasPolicyChanges(BASE_DTO, draft)).toBe(true);
  });

  it('RFC-014 §2 D2: autoConfirmEnabled açılırsa değişiklik tespit eder', () => {
    const draft = draftFromDto(BASE_DTO);
    draft.autoConfirmEnabled = true;
    expect(hasPolicyChanges(BASE_DTO, draft)).toBe(true);
  });
});

describe('autoConfirmEnabled (RFC-014 §2 D2)', () => {
  it('draftFromDto sunucudaki değeri olduğu gibi taşır', () => {
    expect(draftFromDto({ ...BASE_DTO, autoConfirmEnabled: true }).autoConfirmEnabled).toBe(true);
    expect(draftFromDto({ ...BASE_DTO, autoConfirmEnabled: false }).autoConfirmEnabled).toBe(false);
  });

  it('draftToPayload sunucuya doğru değeri gönderir — diğer alanlardan farklı olarak normalize edilmez (altında bir sayı yok)', () => {
    const draft = baseDraft({ autoConfirmEnabled: true });
    expect(draftToPayload(draft).autoConfirmEnabled).toBe(true);
  });

  it('kapora açıkken de autoConfirmEnabled bağımsız çalışır (ikisi ayrı toggle)', () => {
    const draft = baseDraft({ depositEnabled: true, depositPercent: 0.15, autoConfirmEnabled: true });
    const payload = draftToPayload(draft);
    expect(payload.depositEnabled).toBe(true);
    expect(payload.autoConfirmEnabled).toBe(true);
  });
});

describe('sanitizeDecimalText', () => {
  it('TR ondalık virgülünü noktaya çevirir: "12,5" -> "12.5"', () => {
    expect(sanitizeDecimalText('12,5', 2)).toBe('12.5');
  });

  it('ikinci noktayı metinden atar: "1.2.3" -> "1.23"', () => {
    expect(sanitizeDecimalText('1.2.3', 2)).toBe('1.23');
  });

  it('ondalık basamağı kırpar (yuvarlamaz): "12.345" -> "12.34"', () => {
    expect(sanitizeDecimalText('12.345', 2)).toBe('12.34');
  });

  it('harf ve boşluk gibi geçersiz karakterleri atar', () => {
    expect(sanitizeDecimalText('1a2 b.5x', 2)).toBe('12.5');
  });

  it('tek başına nokta veya virgül metinde nokta olarak kalabilir (henüz sayı değil)', () => {
    expect(sanitizeDecimalText(',', 2)).toBe('.');
  });
});

describe('parseDecimalOrNull', () => {
  it('"12,5" TR virgülüyle yazılıp sanitize edildikten sonra 0.125 fraction\'a karşılık gelen 12.5\'i verir', () => {
    const sanitized = sanitizeDecimalText('12,5', 2);
    expect(parseDecimalOrNull(sanitized)).toBe(12.5);
    expect((parseDecimalOrNull(sanitized) ?? 0) / 100).toBeCloseTo(0.125, 10);
  });

  it('boş metin null döner', () => {
    expect(parseDecimalOrNull('')).toBeNull();
  });

  it('tek başına nokta null döner', () => {
    expect(parseDecimalOrNull('.')).toBeNull();
  });

  it('"12.34" -> 12.34', () => {
    expect(parseDecimalOrNull('12.34')).toBe(12.34);
  });
});
