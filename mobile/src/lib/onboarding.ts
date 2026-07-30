/**
 * RFC-007 owner onboarding sihirbazı — saf (network'süz) yardımcılar.
 * Backend `BarberShopStatus` enum'ıyla senkron: Draft=0, PendingKyc=1, Active=2,
 * Suspended=3, Closed=4 (bkz. backend/src/Kuaforum.Domain/Modules/Barber/SalonGenderType.cs).
 * Backend int veya JsonStringEnumConverter ile string de gönderebildiğinden ikisi de kabul edilir.
 */
export type OnboardingShop = {
  status: number | string;
  addressLine1?: string | null;
};

export type OnboardingStep = 1 | 2 | 3 | 'done';

const DRAFT_VALUES = new Set<number | string>([0, '0', 'Draft']);

/**
 * Kaldığı yerden devam mantığı:
 * - Hiç shop yok → adım 1 (salon bilgisi).
 * - Shop var, Draft, adres yok → adım 2 (adres).
 * - Shop var, Draft, adres var → adım 3 (belgeler) — reddedilip Draft'a dönmüş
 *   başvurular da buraya düşer, KycDocumentSection zaten yeniden gönderim akışını destekliyor.
 * - Shop var, Draft DIŞINDA bir statüde (PendingKyc/Active/Suspended/Closed) → 'done',
 *   sihirbaz yerine (barber)/kyc durum ekranına yönlendirilir.
 */
export function resolveOnboardingStep(shop: OnboardingShop | null): OnboardingStep {
  if (!shop) return 1;
  const isDraft = DRAFT_VALUES.has(shop.status);
  if (!isDraft) return 'done';
  const hasAddress = !!shop.addressLine1 && shop.addressLine1.trim().length > 0;
  return hasAddress ? 3 : 2;
}

/** Adım 1 — salon adı: 2-120 karakter (backend CreateBarberShopCommandValidator ile paralel). */
export function isValidShopName(name: string): boolean {
  const trimmed = name.trim();
  return trimmed.length >= 2 && trimmed.length <= 120;
}

/**
 * Adım 2 — açık adres: 5-200 karakter. NOT: görev tanımında "5-300" deniyordu ama
 * backend `SetBarberShopAddressCommandValidator.Line1` MaximumLength(200) — burada
 * gerçek backend sınırı esas alındı, aksi halde 200-300 aralığında kullanıcı 400 alırdı.
 */
export function isValidAddressLine(line: string): boolean {
  const trimmed = line.trim();
  return trimmed.length >= 5 && trimmed.length <= 200;
}
