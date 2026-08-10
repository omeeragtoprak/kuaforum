import {
  formatWorkingHoursImpactMessage,
  formatWorkingHoursSavedImpactMessage,
  haveSameAffectedBookings,
  stylistDeactivationWarningText,
  formatStylistDeactivationImpactMessage,
  canFileBookingComplaint,
  complaintConfirmMessage,
  complaintSuccessMessage,
  type AffectedBookingDto,
} from '@/lib/salon-changes';
import { formatDateTime } from '@/lib/format';

const sema: AffectedBookingDto = {
  bookingId: 'b-1',
  customerId: 'c-1',
  customerName: 'Sema Yalman',
  scheduledAt: '2026-08-11T11:30:00+00:00',
  scheduledEndsAt: '2026-08-11T12:20:00+00:00',
};

const cemil: AffectedBookingDto = {
  bookingId: 'b-2',
  customerId: 'c-2',
  customerName: 'Cemil Aydemir',
  scheduledAt: '2026-08-18T10:00:00+00:00',
  scheduledEndsAt: '2026-08-18T10:35:00+00:00',
};

const anonymous: AffectedBookingDto = {
  bookingId: 'b-3',
  customerId: 'c-3',
  customerName: null,
  scheduledAt: '2026-08-20T10:00:00+00:00',
  scheduledEndsAt: '2026-08-20T10:35:00+00:00',
};

describe('formatWorkingHoursImpactMessage (RFC-015 §2 D1 madde 2)', () => {
  it('boş liste — hiçbir randevu etkilenmiyor mesajı', () => {
    expect(formatWorkingHoursImpactMessage([])).toBe('Bu değişiklik hiçbir randevuyu etkilemiyor.');
  });

  it('tek randevu — sayı + müşteri adı + tarih içerir', () => {
    const msg = formatWorkingHoursImpactMessage([sema]);
    expect(msg).toContain('Bu değişiklik 1 randevunu etkiliyor');
    expect(msg).toContain('Sema Yalman');
    expect(msg).toContain(formatDateTime(sema.scheduledAt));
  });

  it('çoklu randevu — hepsi listelenir, doğru sayı', () => {
    const msg = formatWorkingHoursImpactMessage([sema, cemil]);
    expect(msg).toContain('Bu değişiklik 2 randevunu etkiliyor');
    expect(msg).toContain('Sema Yalman');
    expect(msg).toContain('Cemil Aydemir');
  });

  it('isimsiz müşteri — placeholder metin kullanılır, çökmez', () => {
    const msg = formatWorkingHoursImpactMessage([anonymous]);
    expect(msg).toContain('İsimsiz müşteri');
  });

  it('5\'ten fazla — ilk 5 listelenir, kalan sayı özetlenir', () => {
    const many: AffectedBookingDto[] = Array.from({ length: 7 }, (_, i) => ({
      bookingId: `b-${i}`,
      customerId: `c-${i}`,
      customerName: `Müşteri ${i}`,
      scheduledAt: '2026-08-11T11:30:00+00:00',
      scheduledEndsAt: '2026-08-11T12:20:00+00:00',
    }));
    const msg = formatWorkingHoursImpactMessage(many);
    expect(msg).toContain('Bu değişiklik 7 randevunu etkiliyor');
    expect(msg).toContain('Müşteri 0');
    expect(msg).toContain('Müşteri 4');
    expect(msg).not.toContain('Müşteri 5');
    expect(msg).toContain('…ve 2 randevu daha');
  });

  it('"emin misiniz" DEMEZ — direktif kuralı (ne olacağını yazar)', () => {
    expect(formatWorkingHoursImpactMessage([sema])).not.toMatch(/emin misin/i);
  });

  it('otomatik iptal edilmediğini açıkça söyler', () => {
    expect(formatWorkingHoursImpactMessage([sema])).toContain('otomatik iptal edilmez');
  });
});

describe('formatWorkingHoursSavedImpactMessage (RFC-015 §2 D1 madde 3 — kaydetme sonrası)', () => {
  it('boş liste — hiçbir randevu etkilenmedi (geçmiş zaman)', () => {
    expect(formatWorkingHoursSavedImpactMessage([])).toBe('Bu değişiklik hiçbir randevuyu etkilemedi.');
  });

  it('dolu liste — "kaydedildi" tonu, bildirim gönderildiğini söyler', () => {
    const msg = formatWorkingHoursSavedImpactMessage([sema]);
    expect(msg).toContain('Kaydedildi');
    expect(msg).toContain('Sema Yalman');
    expect(msg).toContain('bildirim gönderildi');
  });
});

describe('haveSameAffectedBookings (RFC-015 §2 D1 — preview vs. save fark kontrolü)', () => {
  it('aynı tek elemanlı kümeler — true', () => {
    expect(haveSameAffectedBookings([sema], [sema])).toBe(true);
  });

  it('farklı sırada aynı küme — true (sıra önemsiz)', () => {
    expect(haveSameAffectedBookings([sema, cemil], [cemil, sema])).toBe(true);
  });

  it('farklı boyut — false', () => {
    expect(haveSameAffectedBookings([sema], [sema, cemil])).toBe(false);
  });

  it('aynı boyut farklı id — false', () => {
    expect(haveSameAffectedBookings([sema], [cemil])).toBe(false);
  });

  it('iki boş liste — true', () => {
    expect(haveSameAffectedBookings([], [])).toBe(true);
  });
});

describe('stylistDeactivationWarningText (RFC-015 §2 D2 madde 3 — mobil metin düzeltmesi)', () => {
  const text = stylistDeactivationWarningText();

  it('eski yanıltıcı ifadeyi İÇERMEZ ("etkilenmez")', () => {
    expect(text).not.toMatch(/etkilenmez/i);
  });

  it('mevcut randevuların hâlâ çalışana bağlı kaldığını söyler', () => {
    expect(text).toContain('bağlı');
  });

  it('kuaförün halletmesi gerektiğini söyler (devretme veya iptal)', () => {
    expect(text).toContain('devretmen');
    expect(text).toContain('iptal etmen');
  });

  it('otomatik yapılmadığını açıkça söyler', () => {
    expect(text).toMatch(/otomatik yapmaz/i);
  });
});

describe('formatStylistDeactivationImpactMessage (RFC-015 §2 D2 madde 1-2)', () => {
  it('boş liste — randevusu yoktu mesajı, çalışan adı geçer', () => {
    const msg = formatStylistDeactivationImpactMessage([], 'Selin Kaya');
    expect(msg).toContain('Selin Kaya');
    expect(msg).toContain('pasifleştirildi');
  });

  it('dolu liste — sayı + müşteri listesi + devretme/iptal çağrısı', () => {
    const msg = formatStylistDeactivationImpactMessage([sema, cemil], 'Ayşe Modern');
    expect(msg).toContain('Ayşe Modern');
    expect(msg).toContain('2 randevusu');
    expect(msg).toContain('Sema Yalman');
    expect(msg).toContain('Cemil Aydemir');
    expect(msg).toContain('devretmen');
  });
});

describe('canFileBookingComplaint (RFC-015 §2 D3 madde 1 — buton görünürlüğü)', () => {
  const NOW = new Date('2026-08-10T12:00:00Z').getTime();
  const pastEndsAt = '2026-08-07T06:00:00+00:00';
  const futureEndsAt = '2026-08-20T06:00:00+00:00';

  // Karar tablosu:
  // | isBarber | status      | scheduledEndsAt | hasComplaint | beklenen |
  // |----------|-------------|------------------|--------------|----------|
  // | true     | Completed   | geçmiş           | false        | false (kuaför göremez) |
  // | false    | Completed   | geçmiş           | false        | true     |
  // | false    | Confirmed   | geçmiş           | false        | true     |
  // | false    | Started     | geçmiş           | false        | true     |
  // | false    | Pending     | geçmiş           | false        | false (henüz taahhüt yok) |
  // | false    | Cancelled   | geçmiş           | false        | false (terminal, ilgisiz) |
  // | false    | NoShow      | geçmiş           | false        | false (ters yönde zaten kayıt) |
  // | false    | Completed   | gelecek          | false        | false (backend 400 döner, UI göstermemeli) |
  // | false    | Completed   | geçmiş           | true         | false (tek bildirim, tekrar no-op) |

  it('kuaför (isBarber=true) hiçbir zaman göremez', () => {
    expect(canFileBookingComplaint({ isBarber: true, status: 'Completed', scheduledEndsAt: pastEndsAt, hasComplaint: false }, NOW)).toBe(false);
  });

  it.each(['Completed', 'Confirmed', 'Started', 3, 1, 2])('müşteri + geçmiş + uygun durum (%p) → true', (status) => {
    expect(canFileBookingComplaint({ isBarber: false, status, scheduledEndsAt: pastEndsAt, hasComplaint: false }, NOW)).toBe(true);
  });

  it.each(['Pending', 'Cancelled', 'NoShow', 0, 4, 5])('müşteri + geçmiş + uygun OLMAYAN durum (%p) → false', (status) => {
    expect(canFileBookingComplaint({ isBarber: false, status, scheduledEndsAt: pastEndsAt, hasComplaint: false }, NOW)).toBe(false);
  });

  it('gelecekteki randevu → false (backend 400 döner, ama UI zaten göstermemeli)', () => {
    expect(canFileBookingComplaint({ isBarber: false, status: 'Completed', scheduledEndsAt: futureEndsAt, hasComplaint: false }, NOW)).toBe(false);
  });

  it('tam scheduledEndsAt anında (sınır) → true (backend UtcNow >= ScheduledEndsAt ile aynı sınır)', () => {
    const exactNow = new Date(NOW).toISOString();
    expect(canFileBookingComplaint({ isBarber: false, status: 'Completed', scheduledEndsAt: exactNow, hasComplaint: false }, NOW)).toBe(true);
  });

  it('zaten şikayet edilmiş (hasComplaint=true) → false, tekrar gösterilmez', () => {
    expect(canFileBookingComplaint({ isBarber: false, status: 'Completed', scheduledEndsAt: pastEndsAt, hasComplaint: true }, NOW)).toBe(false);
  });

  it('now enjekte edilmezse gerçek Date.now() kullanılır (varsayılan parametre çalışıyor)', () => {
    // Uzak geçmiş bir randevu her zaman geçmiştedir — varsayılan `now` ile de true dönmeli.
    expect(canFileBookingComplaint({ isBarber: false, status: 'Completed', scheduledEndsAt: '2020-01-01T00:00:00Z', hasComplaint: false })).toBe(true);
  });
});

describe('complaint metinleri (RFC-015 §2 D3 madde 5 — yanlış beklenti YOK)', () => {
  it('confirm mesajı: admin inceleyecek der, otomatik iade YOK der', () => {
    const msg = complaintConfirmMessage();
    expect(msg).toContain('incelenecek');
    expect(msg).toMatch(/otomatik iade YAPILMAZ/i);
  });

  it('confirm mesajı: "paran iade edilecek" gibi yanlış bir vaat İÇERMEZ', () => {
    const msg = complaintConfirmMessage();
    expect(msg).not.toMatch(/paran iade edilecek/i);
    expect(msg).not.toMatch(/iade alacaksın/i);
  });

  it('success mesajı net bir geri bildirim verir', () => {
    expect(complaintSuccessMessage()).toContain('alındı');
  });
});
