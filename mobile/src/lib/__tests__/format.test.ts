import {
  formatTry,
  formatDuration,
  maskPhone,
  formatPhoneTr,
  phoneToE164,
  isValidTrMobile,
  normalizeTr,
  bookingStatusInfo,
  isBookingCancelled,
  isBookingRevenueCounted,
  isBookingPending,
} from '@/lib/format';

describe('formatTry', () => {
  it('minor birimi (kuruş) TL para birimine çevirir', () => {
    // 123456 kuruş -> 1234.56 TL -> tr-TR gösterimde "1.234,56 ₺" biçiminde
    expect(formatTry(123456)).toContain('1.234,56');
    expect(formatTry(123456)).toContain('₺');
  });

  it('tam sayı TL için gereksiz ondalık basmaz', () => {
    expect(formatTry(10000)).toContain('100');
    expect(formatTry(10000)).not.toContain(',00');
  });
});

describe('formatDuration', () => {
  it('60 dakikanın altında dakika cinsinden gösterir', () => {
    expect(formatDuration(45)).toBe('45 dk');
  });

  it('tam saatlerde dakikayı basmaz', () => {
    expect(formatDuration(120)).toBe('2 sa');
  });

  it('saat + dakika kombinasyonunu doğru formatlar', () => {
    expect(formatDuration(95)).toBe('1 sa 35 dk');
  });
});

describe('maskPhone', () => {
  it('10+ haneli telefonu maskeler', () => {
    expect(maskPhone('5551234567')).toBe('555 123 •• •• 67');
  });

  it('kısa/geçersiz girdide değişiklik yapmadan döner', () => {
    expect(maskPhone('555')).toBe('555');
  });
});

describe('formatPhoneTr / phoneToE164 / isValidTrMobile round-trip', () => {
  it('ham 10 haneli girdiyi görüntü formatına çevirir', () => {
    expect(formatPhoneTr('5551234567')).toBe('+90 555 123 45 67');
  });

  it('0 önekini ve 90 ülke kodunu temizler', () => {
    expect(formatPhoneTr('05551234567')).toBe('+90 555 123 45 67');
    expect(formatPhoneTr('905551234567')).toBe('+90 555 123 45 67');
  });

  it('görüntü formatından E.164 formatına geri döner', () => {
    expect(phoneToE164('+90 555 123 45 67')).toBe('+905551234567');
  });

  it('eksik haneli girdide E.164 boş string döner', () => {
    expect(phoneToE164('+90 555 12')).toBe('');
  });

  it('geçerli TR mobil numarasını onaylar', () => {
    expect(isValidTrMobile('+90 555 123 45 67')).toBe(true);
  });

  it('5 ile başlamayan numarayı reddeder (sabit hat)', () => {
    expect(isValidTrMobile('+90 212 123 45 67')).toBe(false);
  });
});

describe('normalizeTr', () => {
  it('Türkçe aksanlı karakterleri ASCII karşılığına indirger', () => {
    expect(normalizeTr('İstanbul')).toBe('istanbul');
    expect(normalizeTr('Şişli Güzellik Çırağı')).toBe('sisli guzellik ciragi');
  });

  it('boş string için boş string döner', () => {
    expect(normalizeTr('')).toBe('');
  });
});

describe('bookingStatusInfo', () => {
  it('sayısal ve string enum değerlerini aynı sonuca eşler', () => {
    expect(bookingStatusInfo(1)).toEqual({ label: 'Onaylandı', tone: 'success' });
    expect(bookingStatusInfo('1')).toEqual({ label: 'Onaylandı', tone: 'success' });
    expect(bookingStatusInfo('Confirmed')).toEqual({ label: 'Onaylandı', tone: 'success' });
  });

  it('bilinmeyen değer için "Bilinmiyor" fallback döner', () => {
    expect(bookingStatusInfo(999)).toEqual({ label: 'Bilinmiyor', tone: 'neutral' });
  });
});

describe('isBookingCancelled / isBookingRevenueCounted', () => {
  it.each([4, '4', 'Cancelled', 5, '5', 'NoShow'])(
    'iptal/no-show statüsünü (%p) iptal olarak işaretler',
    (status) => {
      expect(isBookingCancelled(status)).toBe(true);
    },
  );

  it.each([1, 2, 3, 'Confirmed', 'Started', 'Completed'])(
    'aktif statüyü (%p) ciroya dahil eder',
    (status) => {
      expect(isBookingRevenueCounted(status)).toBe(true);
    },
  );

  it('beklemedeki (Pending) rezervasyon ciroya dahil edilmez', () => {
    expect(isBookingRevenueCounted('Pending')).toBe(false);
    expect(isBookingRevenueCounted(0)).toBe(false);
  });

  it('iptal edilmiş rezervasyon ciroya dahil edilmez', () => {
    expect(isBookingRevenueCounted('Cancelled')).toBe(false);
  });
});

describe('isBookingPending', () => {
  it.each([0, '0', 'Pending'])(
    'beklemedeki statüyü (%p) pending olarak işaretler',
    (status) => {
      expect(isBookingPending(status)).toBe(true);
    },
  );

  it.each([1, '1', 'Confirmed', 2, 'Started', 3, 'Completed', 4, 'Cancelled', 5, 'NoShow'])(
    'beklemede olmayan statüyü (%p) pending saymaz',
    (status) => {
      expect(isBookingPending(status)).toBe(false);
    },
  );
});
