import type { Metadata } from 'next';
import Link from 'next/link';
import { SearchX } from 'lucide-react';
import { getCities, getShops, type City } from '@/lib/api';
import { SiteHeader } from '@/components/site-header';
import { SiteFooter } from '@/components/site-footer';
import { ShopCard } from '@/components/shop-card';
import { Pagination } from '@/components/pagination';

const PAGE_SIZE = 20;

// Backend guard'ı GUID formatı bekliyor; bozuk query param'ı sessizce yok say.
const GUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

interface SearchParams {
  city?: string;
  page?: string;
}

function buildQuery(params: Record<string, string | undefined>): string {
  const parts = Object.entries(params)
    .filter(([, v]) => v !== undefined && v !== '')
    .map(([k, v]) => `${k}=${encodeURIComponent(v as string)}`);
  return parts.length ? `?${parts.join('&')}` : '';
}

function parseParams(sp: SearchParams): { cityId: string | undefined; page: number } {
  const cityId = sp.city && GUID_RE.test(sp.city) ? sp.city : undefined;
  const parsed = Number.parseInt(sp.page ?? '1', 10);
  const page = Number.isFinite(parsed) && parsed > 0 ? parsed : 1;
  return { cityId, page };
}

export async function generateMetadata(props: {
  searchParams: Promise<SearchParams>;
}): Promise<Metadata> {
  const { cityId, page } = parseParams(await props.searchParams);
  let cityName: string | undefined;
  if (cityId) {
    const cities = await getCities();
    cityName = cities?.find((c) => c.id === cityId)?.name;
  }
  const title = cityName ? `${cityName} Kuaförleri ve Berberleri` : 'Kuaförler ve Berberler';
  return {
    title,
    description: cityName
      ? `${cityName} şehrindeki kuaför ve berber salonları: hizmetler, fiyatlar, puanlar. Randevunu Kuaforum ile al.`
      : 'Türkiye genelindeki kuaför ve berber salonları: hizmetler, fiyatlar, puanlar. Randevunu Kuaforum ile al.',
    alternates: {
      // Sayfa 2+ self-canonical olmalı (Google paginated içerik önerisi) — page paramı korunur.
      canonical: `/kuaforler${buildQuery({ city: cityId, page: page > 1 ? String(page) : undefined })}`,
    },
  };
}

function CityFilter({
  cities,
  activeCityId,
}: {
  cities: City[];
  activeCityId: string | undefined;
}) {
  const visible = cities.filter((c) => c.activeShopCount > 0);
  if (visible.length === 0) return null;

  const chipBase =
    'inline-flex h-10 shrink-0 items-center gap-1.5 rounded-full border px-4 text-sm font-medium transition-colors';

  return (
    <nav aria-label="Şehir filtresi" className="-mx-4 mt-8 px-4 sm:mx-0 sm:px-0">
      <ul className="flex gap-2 overflow-x-auto pb-2">
        <li>
          <Link
            href="/kuaforler"
            aria-current={activeCityId ? undefined : 'page'}
            className={`${chipBase} ${
              activeCityId
                ? 'border-ink-900/10 bg-white text-ink-700 hover:border-copper hover:text-copper-700'
                : 'border-ink-900 bg-ink-900 text-paper'
            }`}
          >
            Tümü
          </Link>
        </li>
        {visible.map((city) => {
          const active = city.id === activeCityId;
          return (
            <li key={city.id}>
              <Link
                href={`/kuaforler?city=${city.id}`}
                aria-current={active ? 'page' : undefined}
                className={`${chipBase} ${
                  active
                    ? 'border-ink-900 bg-ink-900 text-paper'
                    : 'border-ink-900/10 bg-white text-ink-700 hover:border-copper hover:text-copper-700'
                }`}
              >
                {city.name}
                <span className={`tabular text-xs ${active ? 'text-paper/60' : 'text-ink-400'}`}>
                  {city.activeShopCount}
                </span>
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}

export default async function BarberListPage(props: {
  searchParams: Promise<SearchParams>;
}) {
  const { cityId, page } = parseParams(await props.searchParams);

  const [cities, shops] = await Promise.all([
    getCities(),
    getShops({ cityId, page, pageSize: PAGE_SIZE }),
  ]);

  const cityName = cityId ? cities?.find((c) => c.id === cityId)?.name : undefined;
  const items = shops?.items ?? [];
  const totalCount = shops?.totalCount ?? 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  const hrefFor = (p: number) => {
    const qs = new URLSearchParams();
    if (cityId) qs.set('city', cityId);
    if (p > 1) qs.set('page', String(p));
    const s = qs.toString();
    return s ? `/kuaforler?${s}` : '/kuaforler';
  };

  return (
    <>
      <SiteHeader />
      <main id="icerik" className="flex-1">
        <div className="mx-auto max-w-site px-4 py-10 sm:px-6 sm:py-14">
          <nav aria-label="İçerik haritası" className="text-sm text-ink-500">
            <Link href="/" className="transition-colors hover:text-copper-700">
              Ana sayfa
            </Link>
            <span aria-hidden="true" className="mx-2">
              /
            </span>
            <span className="text-ink-900">Kuaförler</span>
          </nav>

          <h1 className="text-balance mt-4 font-display text-3xl font-semibold text-ink-900 sm:text-4xl">
            {cityName ? `${cityName} kuaförleri` : 'Kuaförler ve berberler'}
          </h1>
          {shops && (
            <p className="tabular mt-2 text-sm text-ink-500">
              {totalCount > 0
                ? `${totalCount} salon listeleniyor`
                : 'Bu filtreyle eşleşen salon yok'}
            </p>
          )}

          {cities && cities.length > 0 && (
            <CityFilter cities={cities} activeCityId={cityId} />
          )}

          {items.length > 0 ? (
            <>
              <div className="mt-8 grid gap-4 sm:grid-cols-2 lg:grid-cols-3 lg:gap-5">
                {items.map((shop) => (
                  <ShopCard key={shop.id} shop={shop} />
                ))}
              </div>
              <Pagination page={page} totalPages={totalPages} hrefFor={hrefFor} />
            </>
          ) : (
            <div className="mt-10 rounded-2xl border border-dashed border-ink-900/15 bg-white/70 p-10 text-center sm:p-16">
              <SearchX className="mx-auto h-9 w-9 text-ink-400" aria-hidden="true" />
              <h2 className="mt-4 font-display text-xl font-semibold text-ink-900">
                {shops
                  ? cityName
                    ? `${cityName} şehrinde henüz salon yok`
                    : 'Henüz listelenecek salon yok'
                  : 'Salonlar şu anda yüklenemiyor'}
              </h2>
              <p className="mx-auto mt-2 max-w-md text-sm leading-relaxed text-ink-600">
                {shops
                  ? 'Kuaforum her hafta yeni salonlarla büyüyor. Salon sahibiysen aramıza katıl; müşteriysen çok yakında buradayız.'
                  : 'Kısa bir teknik aksaklık olabilir. Lütfen birazdan tekrar dene.'}
              </p>
              {shops && (
                <Link
                  href="/#isletmeler"
                  className="mt-6 inline-flex h-11 items-center justify-center rounded-xl bg-copper px-5 text-sm font-semibold text-white transition-colors hover:bg-copper-700"
                >
                  Salonunu ücretsiz ekle
                </Link>
              )}
            </div>
          )}
        </div>
      </main>
      <SiteFooter />
    </>
  );
}
