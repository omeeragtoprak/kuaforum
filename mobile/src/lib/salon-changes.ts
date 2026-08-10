import { formatDateTime } from '@/lib/format';

/**
 * RFC-015 §4 madde 1 — backend `AffectedBookingDto` ile birebir (camelCase). D1 (çalışma
 * saati preview/save) ve D2 (çalışan pasifleştirme) İKİSİ de bu şekli döner; mobil tarafta da
 * kopyalanmaz, `src/api/barber.ts` bu tipi export eder ve bu dosya import eder.
 */
export type AffectedBookingDto = {
  bookingId: string;
  customerId: string;
  customerName: string | null;
  scheduledAt: string;
  scheduledEndsAt: string;
};

const MAX_LISTED = 5;

function formatAffectedLines(affected: AffectedBookingDto[]): string {
  const shown = affected.slice(0, MAX_LISTED);
  const lines = shown.map((a) => `• ${a.customerName ?? 'İsimsiz müşteri'} — ${formatDateTime(a.scheduledAt)}`);
  const remaining = affected.length - shown.length;
  if (remaining > 0) lines.push(`• …ve ${remaining} randevu daha`);
  return lines.join('\n');
}

/**
 * RFC-015 §2 D1 madde 2 — kaydetmeden ÖNCE gösterilen onay diyaloğunun gövdesi. Direktif
 * kuralı: NE OLACAĞINI yazar, "emin misiniz?" demez. Saf fonksiyon — sayı/isim/tarih dallanması
 * JSX'e gömülmez (bu projede resolveBookingPromise/formatConflictConfirmMessage ile aynı desen).
 *
 * Boş liste için caller bu fonksiyonu hiç çağırmamalı (etkilenen yoksa diyalog da yok) — yine de
 * defensive bir mesaj döner.
 */
export function formatWorkingHoursImpactMessage(affected: AffectedBookingDto[]): string {
  const n = affected.length;
  if (n === 0) return 'Bu değişiklik hiçbir randevuyu etkilemiyor.';
  return (
    `Bu değişiklik ${n} randevunu etkiliyor:\n${formatAffectedLines(affected)}\n\n` +
    'Kaydedersen bu müşterilere salonun o saatte artık kapalı olduğu bildirilecek. ' +
    'Randevular otomatik iptal edilmez — iptal ya da erteleme kararı sana ait.'
  );
}

/**
 * RFC-015 §2 D1 madde 3 — kaydetme YANITINDAKİ kesin sonuç önizlemeden farklı çıkarsa (yarış:
 * preview'dan sonra yeni bir randevu girdi/çıktı) kullanıcıya gösterilecek takip mesajı. Aynı
 * `formatWorkingHoursImpactMessage` gövdesini kullanmaz — burada "kaydettin, son durum bu" tonu
 * gerekir, "kaydedersen" değil.
 */
export function formatWorkingHoursSavedImpactMessage(affected: AffectedBookingDto[]): string {
  const n = affected.length;
  if (n === 0) return 'Bu değişiklik hiçbir randevuyu etkilemedi.';
  return (
    `Kaydedildi. Bu değişiklik ${n} randevunu etkiledi:\n${formatAffectedLines(affected)}\n\n` +
    'Etkilenen müşterilere bildirim gönderildi. Randevular otomatik iptal edilmedi.'
  );
}

/**
 * RFC-015 §2 D1 — iki `AffectedBookingDto` listesinin AYNI randevu kümesini taşıyıp taşımadığı
 * (sıra önemsiz, id kümesi karşılaştırılır). `formatWorkingHoursSavedImpactMessage`'ı yalnız
 * gerçekten farklıysa göstermek için kullanılır — kullanıcı zaten onayladığı aynı listeyi
 * kaydettikten hemen sonra ikinci kez görmemeli.
 */
export function haveSameAffectedBookings(a: AffectedBookingDto[], b: AffectedBookingDto[]): boolean {
  if (a.length !== b.length) return false;
  const idsA = new Set(a.map((x) => x.bookingId));
  return b.every((x) => idsA.has(x.bookingId));
}

/**
 * RFC-015 §2 D2 madde 3 — `availability.tsx`'teki eski metin ("Pasif çalışan müşterilere
 * görünmez. Aktif randevuları etkilenmez.") teknik olarak doğruydu ama "ilgilenmem gereken bir
 * şey yok" hissi veriyordu. Yeni metin mevcut randevuların HÂLÂ o çalışana bağlı kaldığını ve
 * kuaförün bunları halletmesi gerektiğini açıkça söyler. Otomatik devretme YAPILMAZ (V1.5).
 */
export function stylistDeactivationWarningText(): string {
  return (
    'Pasif çalışan yeni randevu alamaz ve müşterilere görünmez. Ama mevcut randevuları hâlâ ona ' +
    'bağlı kalır — bunları devretmen veya iptal etmen gerekir, sistem otomatik yapmaz.'
  );
}

/**
 * RFC-015 §2 D2 madde 1-2 — pasifleştirme YANITINDAKİ etkilenen randevu listesi. Preview yok
 * (D1'in aksine): kuaför önce pasifleştirir, sonuç PATCH yanıtıyla birlikte gelir. Liste doluysa
 * bu mesaj ayrı bir bilgilendirme diyaloğunda gösterilir — pasifleştirme zaten YAPILMIŞTIR,
 * burada bir onay sorulmaz, yalnız "şimdi ne yapman gerekiyor" bilgisi verilir.
 */
export function formatStylistDeactivationImpactMessage(affected: AffectedBookingDto[], stylistName: string): string {
  const n = affected.length;
  if (n === 0) return `${stylistName} pasifleştirildi. Gelecekte planlanmış randevusu yoktu.`;
  return (
    `${stylistName} pasifleştirildi. Ama hâlâ ${n} randevusu ona bağlı:\n${formatAffectedLines(affected)}\n\n` +
    'Bu müşterileri başka bir çalışana devretmen ya da randevuyu iptal etmen gerekiyor.'
  );
}

/** RFC-015 §2 D3 — booking status alanı backend'de sayısal enum ya da string olarak gelebilir (bkz. lib/format.ts BOOKING_STATUS deseni). */
const COMPLAINT_ELIGIBLE_STATUSES = new Set<string>(['1', 'Confirmed', '2', 'Started', 'InProgress', '3', 'Completed']);

export type ComplaintEligibilityInput = {
  status: number | string;
  scheduledEndsAt: string;
  hasComplaint: boolean;
  isBarber: boolean;
};

/**
 * RFC-015 §2 D3 madde 1 — "Kuaför gelmedi / salon kapalıydı" butonunun görünürlük kuralı. Backend
 * `FileBookingComplaintCommandHandler`'ın kabul kuralıyla BİREBİR aynı sınır: yalnız
 * Confirmed/Started/Completed VE randevu fiilen bitmiş (`scheduledEndsAt <= now`). Backend zaten
 * 400 döner ama arayüz görünmeyen bir buton için hata beklememeli — bu fonksiyon UI'ı backend'in
 * kabul edeceği durumla aynı hizaya getirir. `hasComplaint` true ise buton hiç gösterilmez
 * (randevu başına tek bildirim, ux_booking_complaints_booking).
 *
 * `now` test edilebilirlik için enjekte edilir (varsayılan gerçek saat).
 */
export function canFileBookingComplaint(input: ComplaintEligibilityInput, now: number = Date.now()): boolean {
  if (input.isBarber) return false;
  if (input.hasComplaint) return false;
  if (new Date(input.scheduledEndsAt).getTime() > now) return false;
  return COMPLAINT_ELIGIBLE_STATUSES.has(String(input.status));
}

/**
 * RFC-015 §2 D3 madde 5 — gönderme öncesi onay diyaloğu metni. Direktif kuralı: NE OLACAĞINI
 * yazar (admin inceleyecek, otomatik iade YOK). Yanlış beklenti YARATMAZ — "paran iade edilecek"
 * demez, tam tersini açıkça söyler.
 */
export function complaintConfirmMessage(): string {
  return (
    'Bu bildirim ekibimize gidecek ve incelenecek. Otomatik iade YAPILMAZ — inceleme sonucuna göre ' +
    'gerekirse ekibimiz seninle iletişime geçer veya salonla ilgili işlem başlatır.'
  );
}

/** RFC-015 §2 D3 — gönderim başarılı olduktan sonra gösterilecek net geri bildirim. */
export function complaintSuccessMessage(): string {
  return 'Bildirimin alındı. Ekibimiz inceleyecek.';
}
