// Kuaforum public API istemcisi — tüm çağrılar server-side (RSC / ISR).
// Tarayıcıdan API'ye istek atılmaz; bu yüzden CORS gerekmez (RFC-011 notu).
// API erişilemezse build/sayfa KIRILMAZ: her fetch null döner, sayfalar boş-durum gösterir.

const API_BASE =
  process.env.API_BASE_URL ??
  process.env.NEXT_PUBLIC_API_BASE_URL ??
  'http://localhost:5100';

export const SITE_URL = process.env.NEXT_PUBLIC_SITE_URL ?? 'https://kuaforum.com';

// ── Tipler (backend camelCase JSON; enum'lar JsonStringEnumConverter ile string) ──

export interface City {
  id: string;
  plateCode: number;
  name: string;
  slug: string;
  activeShopCount: number;
}

export interface PublicShopListItem {
  id: string;
  name: string;
  slug: string;
  cityName: string | null;
  districtName: string | null;
  ratingAverage: number | null;
  ratingCount: number;
  serviceCount: number;
}

export interface PublicShopListResponse {
  items: PublicShopListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface PublicService {
  name: string;
  durationMinutes: number;
  priceMinor: number;
  currency: string;
}

export interface PublicWorkingHours {
  // Backend DayOfWeek enum'u string serialize eder ("Monday"…); savunmacı olarak
  // sayı da kabul ediyoruz (0 = Sunday, .NET konvansiyonu).
  dayOfWeek: string | number;
  openTime: string | null; // "09:00:00"
  closeTime: string | null;
  isClosed: boolean;
}

export type SalonGenderType = 'Unisex' | 'FemaleOnly' | 'MaleOnly';

export interface PublicShopProfile {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  phone: string | null;
  genderType: SalonGenderType | number;
  cityName: string | null;
  districtName: string | null;
  neighborhoodName: string | null;
  addressLine1: string | null;
  latitude: number | null;
  longitude: number | null;
  ratingAverage: number | null;
  ratingCount: number;
  services: PublicService[];
  workingHours: PublicWorkingHours[];
  stylistNames: string[];
}

export interface LegalDocument {
  doc: string;
  version: string;
  title: string;
  markdown: string;
}

// ── Fetch helper ──

async function apiGet<T>(path: string, revalidateSeconds: number): Promise<T | null> {
  try {
    const res = await fetch(`${API_BASE}${path}`, {
      next: { revalidate: revalidateSeconds },
      headers: { Accept: 'application/json' },
      // Bağlantı asılı kalırsa build/ISR 60sn worker timeout'una takılmasın:
      // 5 sn'de iptal et → null → boş-durum render (build asla kırılmaz).
      signal: AbortSignal.timeout(5000),
    });
    if (!res.ok) return null;
    return (await res.json()) as T;
  } catch {
    // API kapalı / ağ hatası — sayfalar boş-durumla render olur, build kırılmaz.
    return null;
  }
}

// ── Public çağrılar ──

export function getCities(): Promise<City[] | null> {
  return apiGet<City[]>('/v1/geography/cities', 300);
}

export function getShops(params: {
  cityId?: string;
  page?: number;
  pageSize?: number;
}): Promise<PublicShopListResponse | null> {
  const qs = new URLSearchParams();
  if (params.cityId) qs.set('cityId', params.cityId);
  if (params.page) qs.set('page', String(params.page));
  if (params.pageSize) qs.set('pageSize', String(params.pageSize));
  const suffix = qs.size > 0 ? `?${qs.toString()}` : '';
  return apiGet<PublicShopListResponse>(`/v1/public/barber-shops${suffix}`, 300);
}

export function getShopBySlug(slug: string): Promise<PublicShopProfile | null> {
  return apiGet<PublicShopProfile>(
    `/v1/public/barber-shops/${encodeURIComponent(slug)}`,
    300,
  );
}

export function getLegalDocument(doc: string): Promise<LegalDocument | null> {
  return apiGet<LegalDocument>(`/v1/legal/${encodeURIComponent(doc)}`, 3600);
}

/** Sitemap için tüm aktif salon slug'larını sayfalayarak toplar (en fazla ~1000). */
export async function getAllShopSlugs(): Promise<string[]> {
  const slugs: string[] = [];
  const pageSize = 50;
  for (let page = 1; page <= 20; page += 1) {
    const res = await getShops({ page, pageSize });
    if (!res || res.items.length === 0) break;
    slugs.push(...res.items.map((s) => s.slug));
    if (page * pageSize >= res.totalCount) break;
  }
  return slugs;
}
