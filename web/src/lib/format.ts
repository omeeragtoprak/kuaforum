// Türkçe yerelleştirme yardımcıları — tüm formatlama tek yerden.

const DAY_ORDER = [
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
  'Sunday',
] as const;

const DAY_TR: Record<string, string> = {
  Monday: 'Pazartesi',
  Tuesday: 'Salı',
  Wednesday: 'Çarşamba',
  Thursday: 'Perşembe',
  Friday: 'Cuma',
  Saturday: 'Cumartesi',
  Sunday: 'Pazar',
};

// .NET DayOfWeek sayısal: 0 = Sunday … 6 = Saturday
const DAY_FROM_NUMBER: Record<number, string> = {
  0: 'Sunday',
  1: 'Monday',
  2: 'Tuesday',
  3: 'Wednesday',
  4: 'Thursday',
  5: 'Friday',
  6: 'Saturday',
};

export function normalizeDay(day: string | number): string {
  return typeof day === 'number' ? (DAY_FROM_NUMBER[day] ?? 'Monday') : day;
}

export function dayLabelTr(day: string | number): string {
  const key = normalizeDay(day);
  return DAY_TR[key] ?? key;
}

export function dayOrderIndex(day: string | number): number {
  const idx = DAY_ORDER.indexOf(normalizeDay(day) as (typeof DAY_ORDER)[number]);
  return idx === -1 ? 7 : idx;
}

/** Europe/Istanbul'a göre bugünün günü ("Monday" …). */
export function todayIstanbul(): string {
  return new Intl.DateTimeFormat('en-US', {
    weekday: 'long',
    timeZone: 'Europe/Istanbul',
  }).format(new Date());
}

/** "09:00:00" → "09:00" */
export function formatTime(time: string | null): string {
  if (!time) return '—';
  return time.slice(0, 5);
}

/** priceMinor (kuruş) → "₺450" veya "₺449,50" */
export function formatPrice(priceMinor: number, currency: string): string {
  const value = priceMinor / 100;
  const fractional = !Number.isInteger(value);
  try {
    return new Intl.NumberFormat('tr-TR', {
      style: 'currency',
      currency: currency || 'TRY',
      minimumFractionDigits: fractional ? 2 : 0,
      maximumFractionDigits: fractional ? 2 : 0,
    }).format(value);
  } catch {
    // Bilinmeyen para birimi kodu Intl'de RangeError fırlatır
    return `${value.toLocaleString('tr-TR')} ${currency}`;
  }
}

/** 4.75 → "4,8" */
export function formatRating(rating: number): string {
  return new Intl.NumberFormat('tr-TR', {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  }).format(rating);
}

/** 90 → "1 sa 30 dk", 45 → "45 dk" */
export function formatDuration(minutes: number): string {
  if (minutes < 60) return `${minutes} dk`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m === 0 ? `${h} sa` : `${h} sa ${m} dk`;
}

export function genderLabelTr(gender: string | number): string {
  const key = typeof gender === 'number' ? ['Unisex', 'FemaleOnly', 'MaleOnly'][gender] : gender;
  switch (key) {
    case 'FemaleOnly':
      return 'Kadınlara özel';
    case 'MaleOnly':
      return 'Erkeklere özel';
    default:
      return 'Unisex';
  }
}
