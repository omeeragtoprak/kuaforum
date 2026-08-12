export function formatTry(minor: number): string {
  const major = minor / 100;
  return new Intl.NumberFormat('tr-TR', {
    style: 'currency',
    currency: 'TRY',
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
  }).format(major);
}

/**
 * Bir TL tutarına Türkçe AYRILMA HÂLİ eki ("...-dan/-den/-tan/-ten") ekler.
 * Örn. `formatTryFrom(9000)` → "₺90'dan", `formatTryFrom(17000)` → "₺170'ten".
 *
 * NEDEN GEREKLİ: keşfet ve arama kartlarında "en düşük fiyat" etiketi sabit `'den`
 * ile yazılıyordu. Emülatörde "₺90'den" görüldü — doğrusu "₺90'dan". Türkçede ek,
 * sayının OKUNUŞUNA göre değişir; rakamın kendisine bakmak yetmez:
 *   90  "doksan"  → kalın ünlü, yumuşak ünsüz → 'dan
 *   80  "seksen"  → ince ünlü,  yumuşak ünsüz → 'den
 *   170 "yetmiş"  → ince ünlü,  SERT ünsüz (ş) → 'ten
 *   40  "kırk"    → kalın ünlü, SERT ünsüz (k) → 'tan
 *
 * Ek iki kurala birden bağlıdır: (1) son hecenin ünlüsü kalın/ince (büyük ünlü uyumu),
 * (2) son sesin sert/yumuşak olması (ünsüz benzeşmesi — sert ünsüzden sonra d→t).
 * Bu yüzden tabloda her sayı sözcüğü için ekin tamamı tutulur; kural türetmeye
 * çalışmak "yetmiş"/"altmış" gibi sert-ünsüzlü sözcüklerde hataya açıktır.
 *
 * Belirleyici, tutarın SON OKUNAN sözcüğüdür: 1.250 → "bin iki yüz elli" → "elli" → 'den.
 * Sondaki sıfırlar basamağı yukarı taşır: 1.200 → "...iki yüz" → "yüz" → 'den.
 */
const NUMBER_WORD_SUFFIX: Record<number, string> = {
  0: "'dan",   // sıfır  — kalın
  1: "'den",   // bir
  2: "'den",   // iki
  3: "'ten",   // üç     — ç sert
  4: "'ten",   // dört   — t sert
  5: "'ten",   // beş    — ş sert
  6: "'dan",   // altı   — kalın
  7: "'den",   // yedi
  8: "'den",   // sekiz
  9: "'dan",   // dokuz  — kalın
  10: "'dan",  // on
  20: "'den",  // yirmi
  30: "'dan",  // otuz
  40: "'tan",  // kırk   — k sert + kalın
  50: "'den",  // elli
  60: "'tan",  // altmış — ş sert + kalın
  70: "'ten",  // yetmiş — ş sert + ince
  80: "'den",  // seksen
  90: "'dan",  // doksan
  100: "'den", // yüz
  1000: "'den",// bin
  1000000: "'dan", // milyon
};

export function turkishAblativeSuffix(value: number): string {
  const n = Math.abs(Math.trunc(value));
  if (n === 0) return NUMBER_WORD_SUFFIX[0];

  // Son okunan sözcüğü bul: sondan başlayarak ilk SIFIR OLMAYAN basamak grubunu al.
  const ones = n % 10;
  if (ones !== 0) return NUMBER_WORD_SUFFIX[ones];

  const tens = n % 100;
  if (tens !== 0) return NUMBER_WORD_SUFFIX[tens];

  const hundreds = n % 1000;
  if (hundreds !== 0) return NUMBER_WORD_SUFFIX[100];

  // Binler ve üzeri: milyon katları "milyon", geri kalanı "bin" ile biter.
  return n % 1_000_000 === 0 ? NUMBER_WORD_SUFFIX[1_000_000] : NUMBER_WORD_SUFFIX[1000];
}

/** "₺90'dan" — kuruş cinsinden tutarı biçimlendirip doğru ayrılma ekini ekler. */
export function formatTryFrom(minor: number): string {
  const major = Math.round(minor / 100);
  return `${formatTry(minor)}${turkishAblativeSuffix(major)}`;
}

export function formatDateTime(iso: string | Date): string {
  const d = typeof iso === 'string' ? new Date(iso) : iso;
  return new Intl.DateTimeFormat('tr-TR', {
    day: '2-digit',
    month: 'short',
    weekday: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(d);
}

export function formatTime(iso: string | Date): string {
  const d = typeof iso === 'string' ? new Date(iso) : iso;
  return new Intl.DateTimeFormat('tr-TR', { hour: '2-digit', minute: '2-digit' }).format(d);
}

export function formatDuration(minutes: number): string {
  if (minutes < 60) return `${minutes} dk`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m === 0 ? `${h} sa` : `${h} sa ${m} dk`;
}

export function maskPhone(phone: string): string {
  if (!phone || phone.length < 10) return phone;
  return `${phone.slice(0, 3)} ${phone.slice(3, 6)} •• •• ${phone.slice(-2)}`;
}

/**
 * Türk mobil numarası için kullanıcı dostu maskeli formatter.
 * Input: ham rakamlar (kullanıcı yazdıkça çağrılır), input ya '5XXXXXXXXX' ya '+90...'
 * Output: '+90 555 123 45 67' (kısmi de kabul: '+90 555 12')
 */
export function formatPhoneTr(input: string): string {
  if (!input) return '+90 ';
  const digits = input.replace(/\D/g, '').replace(/^90/, '').replace(/^0/, '');
  const limited = digits.slice(0, 10);
  const [a, b, c, d] = [
    limited.slice(0, 3),
    limited.slice(3, 6),
    limited.slice(6, 8),
    limited.slice(8, 10),
  ];
  let out = '+90 ';
  if (a) out += a;
  if (b) out += ' ' + b;
  if (c) out += ' ' + c;
  if (d) out += ' ' + d;
  return out;
}

/** Display'den E.164 formatına dönüştür: '+90 555 123 45 67' → '+905551234567' */
export function phoneToE164(formatted: string): string {
  const digits = formatted.replace(/\D/g, '').replace(/^90/, '').replace(/^0/, '');
  return digits.length === 10 ? `+90${digits}` : '';
}

export function isValidTrMobile(formatted: string): boolean {
  const e164 = phoneToE164(formatted);
  return /^\+905\d{9}$/.test(e164);
}

/** Backend enum'ları int veya string olarak gönderebilir; ikisini de doğru çevir. */
export function salonGenderLabel(g: unknown): string {
  if (g === 0 || g === '0' || g === 'Unisex') return 'Unisex';
  if (g === 1 || g === '1' || g === 'FemaleOnly') return 'Sadece Kadın';
  if (g === 2 || g === '2' || g === 'MaleOnly') return 'Sadece Erkek';
  return '—';
}

export function serviceGenderLabel(g: unknown): string {
  if (g === 0 || g === '0' || g === 'Any') return 'Herkes';
  if (g === 1 || g === '1' || g === 'Female') return 'Kadın';
  if (g === 2 || g === '2' || g === 'Male') return 'Erkek';
  return '—';
}

/** Söz dizimi: "Kadın" | "Erkek" | "Unisex" → tone */
export function salonGenderTone(g: unknown): 'brand' | 'info' | 'neutral' {
  if (g === 1 || g === '1' || g === 'FemaleOnly') return 'brand';
  if (g === 2 || g === '2' || g === 'MaleOnly') return 'info';
  return 'neutral';
}

/**
 * Türkçe arama için aksan/karakter-bağımsız normalizasyon.
 * "İstanbul" ve "Istanbul" ikisi de "istanbul"a düşer.
 */
const TR_MAP: Record<string, string> = {
  'ı': 'i', 'İ': 'i', 'I': 'i', 'i': 'i',
  'ş': 's', 'Ş': 's',
  'ğ': 'g', 'Ğ': 'g',
  'ü': 'u', 'Ü': 'u',
  'ö': 'o', 'Ö': 'o',
  'ç': 'c', 'Ç': 'c',
};

export function normalizeTr(s: string): string {
  if (!s) return '';
  let out = '';
  for (const ch of s) {
    out += TR_MAP[ch] ?? ch.toLowerCase();
  }
  return out;
}

/**
 * Booking status (int veya enum string) için label + tone.
 * Backend `0`/`Pending` ikisini de gönderebiliyor.
 */
export type StatusTone = 'warning' | 'success' | 'info' | 'neutral' | 'error';

const BOOKING_STATUS: Record<string, { label: string; tone: StatusTone }> = {
  '0': { label: 'Beklemede', tone: 'warning' },
  '1': { label: 'Onaylandı', tone: 'success' },
  '2': { label: 'Devam ediyor', tone: 'info' },
  '3': { label: 'Tamamlandı', tone: 'success' },
  '4': { label: 'İptal edildi', tone: 'neutral' },
  '5': { label: 'Gelmediniz', tone: 'error' },
  Pending: { label: 'Beklemede', tone: 'warning' },
  Confirmed: { label: 'Onaylandı', tone: 'success' },
  Started: { label: 'Devam ediyor', tone: 'info' },
  InProgress: { label: 'Devam ediyor', tone: 'info' },
  Completed: { label: 'Tamamlandı', tone: 'success' },
  Cancelled: { label: 'İptal edildi', tone: 'neutral' },
  NoShow: { label: 'Gelmediniz', tone: 'error' },
};

export function bookingStatusInfo(s: unknown): { label: string; tone: StatusTone } {
  return BOOKING_STATUS[String(s)] ?? { label: 'Bilinmiyor', tone: 'neutral' };
}

/** "Pending" veya 0 ise true (yaklaşan veya beklemede). */
export function isBookingUpcoming(s: unknown): boolean {
  return s === 0 || s === '0' || s === 'Pending' || s === 1 || s === '1' || s === 'Confirmed';
}

/** Beklemede (0/Pending) — kuaför onayı için. */
export function isBookingPending(s: unknown): boolean {
  return s === 0 || s === '0' || s === 'Pending';
}

/** Tamamlandı (3/Completed) — yorum için. */
export function isBookingCompleted(s: unknown): boolean {
  return s === 3 || s === '3' || s === 'Completed';
}

/** Onaylanmış (1/Confirmed) — Başlat butonu için. */
export function isBookingConfirmed(s: unknown): boolean {
  return s === 1 || s === '1' || s === 'Confirmed';
}

/** Başlamış (2/Started/InProgress) — Tamamla butonu için. */
export function isBookingInProgress(s: unknown): boolean {
  return s === 2 || s === '2' || s === 'Started' || s === 'InProgress';
}

/** İptal edilmiş veya gelmemiş (4/Cancelled, 5/NoShow). */
export function isBookingCancelled(s: unknown): boolean {
  return (
    s === 4 || s === '4' || s === 'Cancelled' ||
    s === 5 || s === '5' || s === 'NoShow'
  );
}

/** Aktif statüleri (1, 2, 3) — ciro hesabı için. */
export function isBookingRevenueCounted(s: unknown): boolean {
  return (
    s === 1 || s === '1' || s === 'Confirmed' ||
    s === 2 || s === '2' || s === 'Started' || s === 'InProgress' ||
    s === 3 || s === '3' || s === 'Completed'
  );
}
