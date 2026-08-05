import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';
import {
  CalendarCheck,
  Clock,
  MapPin,
  Phone,
  Scissors,
  Users,
} from 'lucide-react';
import {
  getAllShopSlugs,
  getShopBySlug,
  SITE_URL,
  type PublicShopProfile,
} from '@/lib/api';
import {
  dayLabelTr,
  dayOrderIndex,
  formatDuration,
  formatPrice,
  formatTime,
  genderLabelTr,
  normalizeDay,
  todayIstanbul,
} from '@/lib/format';
import { SiteHeader } from '@/components/site-header';
import { SiteFooter } from '@/components/site-footer';
import { Rating } from '@/components/rating';
import { AppDownloadSection } from '@/components/app-download';

// ISR — RFC-011: salon bilgisi sık değişmez; 5 dakikalık pencere yeterli.
export const revalidate = 300;
export const dynamicParams = true;

// Build sırasında API ayaktaysa mevcut salonlar önceden üretilir;
// değilse boş döner ve sayfalar ilk istekte üretilir (build kırılmaz).
export async function generateStaticParams(): Promise<{ slug: string }[]> {
  const slugs = await getAllShopSlugs();
  return slugs.map((slug) => ({ slug }));
}

export async function generateMetadata(props: {
  params: Promise<{ slug: string }>;
}): Promise<Metadata> {
  const { slug } = await props.params;
  const shop = await getShopBySlug(slug);
  if (!shop) {
    return { title: 'Salon bulunamadı' };
  }

  const location = [shop.districtName, shop.cityName].filter(Boolean).join(', ');
  const serviceNames = shop.services.slice(0, 4).map((s) => s.name).join(', ');
  const description = [
    `${shop.name}${location ? ` — ${location}` : ''}.`,
    serviceNames ? `Hizmetler: ${serviceNames}.` : null,
    'Fiyatları ve çalışma saatlerini incele, randevunu Kuaforum ile al.',
  ]
    .filter(Boolean)
    .join(' ');

  return {
    title: `${shop.name}${shop.cityName ? ` — ${shop.cityName}` : ''}`,
    description,
    alternates: { canonical: `/salon/${shop.slug}` },
    openGraph: {
      title: `${shop.name} | Kuaforum`,
      description,
      url: `/salon/${shop.slug}`,
      type: 'website',
      locale: 'tr_TR',
    },
  };
}

function buildJsonLd(shop: PublicShopProfile): string {
  const prices = shop.services.map((s) => s.priceMinor);
  const currency = shop.services[0]?.currency ?? 'TRY';
  const priceRange =
    prices.length > 0
      ? `${formatPrice(Math.min(...prices), currency)} - ${formatPrice(Math.max(...prices), currency)}`
      : undefined;

  const openingHours = shop.workingHours
    .filter((h) => !h.isClosed && h.openTime && h.closeTime)
    .map((h) => ({
      '@type': 'OpeningHoursSpecification',
      dayOfWeek: `https://schema.org/${normalizeDay(h.dayOfWeek)}`,
      opens: formatTime(h.openTime),
      closes: formatTime(h.closeTime),
    }));

  const jsonLd = {
    '@context': 'https://schema.org',
    '@type': 'HairSalon',
    name: shop.name,
    url: `${SITE_URL}/salon/${shop.slug}`,
    description: shop.description ?? undefined,
    telephone: shop.phone ?? undefined,
    priceRange,
    address: {
      '@type': 'PostalAddress',
      streetAddress: shop.addressLine1 ?? undefined,
      addressLocality: shop.districtName ?? undefined,
      addressRegion: shop.cityName ?? undefined,
      addressCountry: 'TR',
    },
    geo:
      shop.latitude != null && shop.longitude != null
        ? {
            '@type': 'GeoCoordinates',
            latitude: shop.latitude,
            longitude: shop.longitude,
          }
        : undefined,
    aggregateRating:
      shop.ratingAverage != null && shop.ratingCount > 0
        ? {
            '@type': 'AggregateRating',
            ratingValue: Number(shop.ratingAverage.toFixed(1)),
            reviewCount: shop.ratingCount,
            bestRating: 5,
            worstRating: 1,
          }
        : undefined,
    openingHoursSpecification: openingHours.length > 0 ? openingHours : undefined,
  };

  // "</script>" kaçışı — veri DB kaynaklı olsa da savunmacı davran.
  return JSON.stringify(jsonLd).replace(/</g, '\\u003c');
}

export default async function SalonPage(props: { params: Promise<{ slug: string }> }) {
  const { slug } = await props.params;
  const shop = await getShopBySlug(slug);
  if (!shop) notFound();

  const location = [shop.neighborhoodName, shop.districtName, shop.cityName]
    .filter(Boolean)
    .join(', ');
  const today = todayIstanbul();
  const hours = [...shop.workingHours].sort(
    (a, b) => dayOrderIndex(a.dayOfWeek) - dayOrderIndex(b.dayOfWeek),
  );
  const mapsUrl =
    shop.latitude != null && shop.longitude != null
      ? `https://www.google.com/maps/search/?api=1&query=${shop.latitude},${shop.longitude}`
      : null;

  return (
    <>
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: buildJsonLd(shop) }}
      />
      <SiteHeader />
      <main id="icerik" className="flex-1 pb-24 sm:pb-0">
        {/* ── Profil başlığı ── */}
        <header className="border-b border-ink-900/5 bg-paper-warm">
          <div className="dotgrid-light">
            <div className="mx-auto max-w-site px-4 py-10 sm:px-6 sm:py-14">
              <nav aria-label="İçerik haritası" className="text-sm text-ink-500">
                <Link href="/" className="transition-colors hover:text-copper-700">
                  Ana sayfa
                </Link>
                <span aria-hidden="true" className="mx-2">
                  /
                </span>
                <Link href="/kuaforler" className="transition-colors hover:text-copper-700">
                  Kuaförler
                </Link>
                <span aria-hidden="true" className="mx-2">
                  /
                </span>
                <span className="text-ink-900">{shop.name}</span>
              </nav>

              <div className="mt-6 flex items-start gap-5">
                <span
                  aria-hidden="true"
                  className="hidden h-20 w-20 shrink-0 items-center justify-center rounded-2xl bg-ink-900 font-display text-4xl font-semibold text-paper sm:flex"
                >
                  {shop.name.charAt(0).toLocaleUpperCase('tr-TR')}
                </span>
                <div className="min-w-0">
                  <h1 className="text-balance font-display text-3xl font-semibold text-ink-900 sm:text-4xl">
                    {shop.name}
                  </h1>
                  <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-2">
                    <Rating average={shop.ratingAverage} count={shop.ratingCount} long />
                    <span className="rounded-full bg-copper-100 px-3 py-1 text-xs font-semibold text-copper-700">
                      {genderLabelTr(shop.genderType)}
                    </span>
                    {location && (
                      <span className="flex items-center gap-1 text-sm text-ink-500">
                        <MapPin className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                        {location}
                      </span>
                    )}
                  </div>
                </div>
              </div>
            </div>
          </div>
        </header>

        <div className="mx-auto max-w-site px-4 py-10 sm:px-6 sm:py-14">
          <div className="grid gap-10 lg:grid-cols-[1fr_20rem] lg:gap-14">
            {/* ── Ana içerik ── */}
            <div className="min-w-0 space-y-12">
              {shop.description && (
                <section aria-labelledby="hakkinda-baslik">
                  <h2 id="hakkinda-baslik" className="sr-only">
                    Hakkında
                  </h2>
                  <p className="max-w-2xl text-base leading-relaxed text-ink-600">
                    {shop.description}
                  </p>
                </section>
              )}

              <section aria-labelledby="hizmetler-baslik">
                <div className="flex items-center gap-2.5">
                  <Scissors className="h-5 w-5 text-copper" aria-hidden="true" />
                  <h2
                    id="hizmetler-baslik"
                    className="font-display text-2xl font-semibold text-ink-900"
                  >
                    Hizmetler ve fiyatlar
                  </h2>
                </div>
                {shop.services.length > 0 ? (
                  <ul className="mt-5 divide-y divide-ink-900/6 rounded-2xl border border-ink-900/8 bg-white shadow-card">
                    {shop.services.map((service, i) => (
                      <li
                        key={`${service.name}-${i}`}
                        className="flex items-baseline justify-between gap-4 px-5 py-4"
                      >
                        <div className="min-w-0">
                          <span className="block font-medium text-ink-900">
                            {service.name}
                          </span>
                          <span className="tabular mt-0.5 block text-sm text-ink-500">
                            {formatDuration(service.durationMinutes)}
                          </span>
                        </div>
                        <span className="tabular shrink-0 font-display text-lg font-semibold text-ink-900">
                          {formatPrice(service.priceMinor, service.currency)}
                        </span>
                      </li>
                    ))}
                  </ul>
                ) : (
                  <p className="mt-4 text-sm text-ink-500">
                    Hizmet listesi henüz eklenmemiş.
                  </p>
                )}
              </section>

              <section aria-labelledby="saatler-baslik">
                <div className="flex items-center gap-2.5">
                  <Clock className="h-5 w-5 text-copper" aria-hidden="true" />
                  <h2
                    id="saatler-baslik"
                    className="font-display text-2xl font-semibold text-ink-900"
                  >
                    Çalışma saatleri
                  </h2>
                </div>
                {hours.length > 0 ? (
                  <ul className="mt-5 divide-y divide-ink-900/6 rounded-2xl border border-ink-900/8 bg-white shadow-card">
                    {hours.map((h) => {
                      const isToday = normalizeDay(h.dayOfWeek) === today;
                      return (
                        <li
                          key={normalizeDay(h.dayOfWeek)}
                          className={`flex items-center justify-between gap-4 px-5 py-3.5 ${
                            isToday ? 'bg-copper-100/40' : ''
                          }`}
                        >
                          <span className="flex items-center gap-2.5 font-medium text-ink-900">
                            {dayLabelTr(h.dayOfWeek)}
                            {isToday && (
                              <span className="rounded-full bg-copper px-2 py-0.5 text-[0.7rem] font-bold uppercase tracking-wide text-white">
                                Bugün
                              </span>
                            )}
                          </span>
                          <span
                            className={`tabular text-sm font-medium ${
                              h.isClosed ? 'text-ink-400' : 'text-ink-700'
                            }`}
                          >
                            {h.isClosed
                              ? 'Kapalı'
                              : `${formatTime(h.openTime)} – ${formatTime(h.closeTime)}`}
                          </span>
                        </li>
                      );
                    })}
                  </ul>
                ) : (
                  <p className="mt-4 text-sm text-ink-500">
                    Çalışma saatleri henüz eklenmemiş.
                  </p>
                )}
              </section>

              {shop.stylistNames.length > 0 && (
                <section aria-labelledby="ekip-baslik">
                  <div className="flex items-center gap-2.5">
                    <Users className="h-5 w-5 text-copper" aria-hidden="true" />
                    <h2
                      id="ekip-baslik"
                      className="font-display text-2xl font-semibold text-ink-900"
                    >
                      Ekip
                    </h2>
                  </div>
                  <ul className="mt-5 flex flex-wrap gap-2.5">
                    {shop.stylistNames.map((name, i) => (
                      <li
                        key={`${name}-${i}`}
                        className="flex items-center gap-2 rounded-full border border-ink-900/8 bg-white py-1.5 pl-1.5 pr-4 shadow-card"
                      >
                        <span
                          aria-hidden="true"
                          className="flex h-8 w-8 items-center justify-center rounded-full bg-paper-warm font-display text-sm font-semibold text-copper-700"
                        >
                          {name.charAt(0).toLocaleUpperCase('tr-TR')}
                        </span>
                        <span className="text-sm font-medium text-ink-900">{name}</span>
                      </li>
                    ))}
                  </ul>
                </section>
              )}
            </div>

            {/* ── Yan panel ── */}
            <aside className="lg:sticky lg:top-24 lg:self-start">
              <div className="rounded-2xl border border-ink-900/8 bg-white p-6 shadow-card">
                <h2 className="font-display text-lg font-semibold text-ink-900">
                  Randevu al
                </h2>
                <p className="mt-2 text-sm leading-relaxed text-ink-600">
                  Boş saatleri görmek ve randevu almak için Kuaforum uygulamasını kullan.
                </p>
                <a
                  href="#indir"
                  className="mt-5 inline-flex h-12 w-full items-center justify-center gap-2 rounded-xl bg-copper text-base font-semibold text-white shadow-lg shadow-copper/25 transition-colors hover:bg-copper-700"
                >
                  <CalendarCheck className="h-5 w-5" aria-hidden="true" />
                  Randevu al
                </a>
                <p className="mt-2.5 text-center text-xs text-ink-500">
                  Uygulama çok yakında yayında
                </p>

                <dl className="mt-6 space-y-4 border-t border-ink-900/8 pt-6">
                  {(shop.addressLine1 ?? location) && (
                    <div className="flex gap-3">
                      <dt className="shrink-0">
                        <MapPin className="mt-0.5 h-4 w-4 text-copper" aria-hidden="true" />
                        <span className="sr-only">Adres</span>
                      </dt>
                      <dd className="text-sm leading-relaxed text-ink-700">
                        {shop.addressLine1 && <span className="block">{shop.addressLine1}</span>}
                        {location && <span className="block text-ink-500">{location}</span>}
                        {mapsUrl && (
                          <a
                            href={mapsUrl}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="mt-1 inline-block font-semibold text-copper transition-colors hover:text-copper-700"
                          >
                            Haritada aç
                          </a>
                        )}
                      </dd>
                    </div>
                  )}
                  {shop.phone && (
                    <div className="flex gap-3">
                      <dt className="shrink-0">
                        <Phone className="mt-0.5 h-4 w-4 text-copper" aria-hidden="true" />
                        <span className="sr-only">Telefon</span>
                      </dt>
                      <dd className="text-sm">
                        <a
                          href={`tel:${shop.phone}`}
                          className="tabular font-medium text-ink-900 transition-colors hover:text-copper-700"
                        >
                          {shop.phone}
                        </a>
                      </dd>
                    </div>
                  )}
                </dl>
              </div>
            </aside>
          </div>
        </div>

        <AppDownloadSection />

        {/* Mobil yapışkan CTA — masaüstünde yan panel görünür olduğundan gizli */}
        <div className="fixed inset-x-0 bottom-0 z-30 border-t border-ink-900/8 bg-paper/95 p-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] backdrop-blur-md sm:hidden">
          <a
            href="#indir"
            className="flex h-12 w-full items-center justify-center gap-2 rounded-xl bg-copper text-base font-semibold text-white shadow-lg shadow-copper/25 transition-colors hover:bg-copper-700"
          >
            <CalendarCheck className="h-5 w-5" aria-hidden="true" />
            Randevu al
          </a>
        </div>
      </main>
      <SiteFooter />
    </>
  );
}
