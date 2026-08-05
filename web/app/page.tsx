import Link from 'next/link';
import {
  ArrowRight,
  CalendarCheck,
  CalendarRange,
  HandCoins,
  MapPin,
  Search,
  Share2,
  Sparkles,
  Store,
} from 'lucide-react';
import { getCities } from '@/lib/api';
import { SiteHeader } from '@/components/site-header';
import { SiteFooter } from '@/components/site-footer';
import { AppDownloadSection } from '@/components/app-download';

// Landing SSG + saatlik yenileme: şehir/salon sayıları değişince kendini günceller,
// API kapalıyken build yine de geçer (getCities null → şehir bölümü boş-durum).
export const revalidate = 3600;

const STEPS = [
  {
    no: '01',
    icon: Search,
    title: 'Keşfet',
    text: 'Çevrendeki salonları incele: hizmetler, fiyatlar, çalışma saatleri ve gerçek müşteri puanları tek ekranda.',
  },
  {
    no: '02',
    icon: CalendarCheck,
    title: 'Seç ve onayla',
    text: 'Sana uyan saati ve stilisti seç. Randevun anında onaylanır, telefonuna hatırlatma gelir.',
  },
  {
    no: '03',
    icon: Sparkles,
    title: 'Sıra beklemeden otur',
    text: 'Salona geldiğinde koltuk senin. Bekleme sırası yok, sürpriz fiyat yok.',
  },
] as const;

const OWNER_BENEFITS = [
  {
    icon: CalendarRange,
    title: 'Defter yerine dijital takvim',
    text: 'Randevular, boş saatler ve ekip planı tek ekranda. Telefonla teyit trafiği biter.',
  },
  {
    icon: HandCoins,
    title: 'Gelmeyen müşteriye çözüm',
    text: 'Kapora seçeneğiyle randevusuna gelmeyen müşteri kaybını azalt.',
  },
  {
    icon: Share2,
    title: 'Paylaşılabilir salon sayfası',
    text: 'Salonuna özel bu web sayfasını Instagram bio\'na koy, yeni müşteri kazan.',
  },
] as const;

export default async function LandingPage() {
  const cities = await getCities();
  const activeCities = (cities ?? [])
    .filter((c) => c.activeShopCount > 0)
    .sort((a, b) => b.activeShopCount - a.activeShopCount);

  return (
    <>
      <SiteHeader />
      <main id="icerik" className="flex-1">
        {/* ── Hero ── */}
        <section aria-labelledby="hero-baslik" className="bg-ink-950">
          <div className="pinstripe-dark">
            <div className="copper-glow">
              <div className="mx-auto max-w-site px-4 pb-20 pt-16 sm:px-6 sm:pb-28 sm:pt-24">
                <div className="max-w-2xl">
                  <p className="animate-fade-up inline-flex items-center gap-2 rounded-full border border-paper/15 bg-paper/5 px-4 py-1.5 text-sm font-medium text-paper/70">
                    Türkiye&apos;nin kuaför randevu platformu
                  </p>
                  <h1
                    id="hero-baslik"
                    className="text-balance animate-fade-up mt-6 font-display text-4xl font-semibold leading-[1.1] text-paper [animation-delay:80ms] sm:text-5xl lg:text-6xl"
                  >
                    Sıra beklemek yok.
                    <br />
                    <span className="text-copper-400">Randevun hazır,</span> koltuk senin.
                  </h1>
                  <p className="animate-fade-up mt-6 max-w-xl text-base leading-relaxed text-paper/65 [animation-delay:160ms] sm:text-lg">
                    Çevrendeki kuaför ve berberleri keşfet, hizmetleri ve fiyatları
                    karşılaştır, sana uyan saati seç. Randevun iki dakikada cebinde.
                  </p>
                  <div className="animate-fade-up mt-9 flex flex-col gap-3 [animation-delay:240ms] sm:flex-row sm:items-center">
                    <Link
                      href="/kuaforler"
                      className="inline-flex h-12 items-center justify-center gap-2 rounded-xl bg-copper px-6 text-base font-semibold text-white shadow-lg shadow-copper/25 transition-all hover:bg-copper-700 hover:shadow-copper/40"
                    >
                      Kuaförleri keşfet
                      <ArrowRight className="h-4 w-4" aria-hidden="true" />
                    </Link>
                    <Link
                      href="/#nasil-calisir"
                      className="inline-flex h-12 items-center justify-center rounded-xl border border-paper/20 px-6 text-base font-semibold text-paper/85 transition-colors hover:border-paper/40 hover:text-paper"
                    >
                      Nasıl çalışır?
                    </Link>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </section>

        {/* ── Nasıl çalışır ── */}
        <section
          id="nasil-calisir"
          aria-labelledby="nasil-baslik"
          className="scroll-mt-20"
        >
          <div className="mx-auto max-w-site px-4 py-16 sm:px-6 sm:py-24">
            <div className="max-w-xl">
              <p className="text-sm font-semibold uppercase tracking-wider text-copper">
                Nasıl çalışır?
              </p>
              <h2
                id="nasil-baslik"
                className="text-balance mt-3 font-display text-3xl font-semibold text-ink-900 sm:text-4xl"
              >
                Üç adımda, telefonla aramadan
              </h2>
            </div>

            <ol className="mt-10 grid gap-5 sm:mt-14 sm:grid-cols-3 sm:gap-6">
              {STEPS.map((step) => (
                <li
                  key={step.no}
                  className="card-lift relative overflow-hidden rounded-2xl border border-ink-900/8 bg-white p-6 sm:p-7"
                >
                  <span
                    aria-hidden="true"
                    className="absolute -right-2 -top-5 font-display text-[5.5rem] font-semibold leading-none text-paper-deep"
                  >
                    {step.no}
                  </span>
                  <span className="relative flex h-11 w-11 items-center justify-center rounded-xl bg-copper-100 text-copper-700">
                    <step.icon className="h-5 w-5" aria-hidden="true" />
                  </span>
                  <h3 className="relative mt-5 font-display text-xl font-semibold text-ink-900">
                    {step.title}
                  </h3>
                  <p className="relative mt-2.5 text-sm leading-relaxed text-ink-600">
                    {step.text}
                  </p>
                </li>
              ))}
            </ol>
          </div>
        </section>

        {/* ── Şehirler ── */}
        <section
          id="sehirler"
          aria-labelledby="sehirler-baslik"
          className="scroll-mt-20 border-y border-ink-900/5 bg-paper-warm"
        >
          <div className="dotgrid-light">
            <div className="mx-auto max-w-site px-4 py-16 sm:px-6 sm:py-24">
              <div className="flex flex-wrap items-end justify-between gap-4">
                <div className="max-w-xl">
                  <p className="text-sm font-semibold uppercase tracking-wider text-copper">
                    Şehirler
                  </p>
                  <h2
                    id="sehirler-baslik"
                    className="text-balance mt-3 font-display text-3xl font-semibold text-ink-900 sm:text-4xl"
                  >
                    Şehrini seç, salonları gör
                  </h2>
                </div>
                <Link
                  href="/kuaforler"
                  className="group inline-flex items-center gap-1.5 text-sm font-semibold text-copper transition-colors hover:text-copper-700"
                >
                  Tüm kuaförler
                  <ArrowRight
                    className="h-4 w-4 transition-transform group-hover:translate-x-0.5"
                    aria-hidden="true"
                  />
                </Link>
              </div>

              {activeCities.length > 0 ? (
                <ul className="mt-10 grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
                  {activeCities.map((city) => (
                    <li key={city.id}>
                      <Link
                        href={`/kuaforler?city=${city.id}`}
                        className="card-lift group flex h-full flex-col rounded-2xl border border-ink-900/8 bg-white p-5"
                      >
                        <span
                          aria-hidden="true"
                          className="tabular inline-flex w-fit items-center rounded-md bg-paper-deep px-2 py-0.5 font-display text-sm font-semibold text-ink-600"
                        >
                          {String(city.plateCode).padStart(2, '0')}
                        </span>
                        <span className="mt-3 font-display text-lg font-semibold text-ink-900 group-hover:text-copper-700">
                          {city.name}
                        </span>
                        <span className="tabular mt-1 text-sm text-ink-500">
                          {city.activeShopCount} salon
                        </span>
                      </Link>
                    </li>
                  ))}
                </ul>
              ) : (
                <div className="mt-10 rounded-2xl border border-dashed border-ink-900/15 bg-white/60 p-8 text-center sm:p-12">
                  <MapPin className="mx-auto h-8 w-8 text-ink-400" aria-hidden="true" />
                  <p className="mt-4 font-display text-lg font-semibold text-ink-900">
                    Şehir listesi şu anda yüklenemiyor
                  </p>
                  <p className="mx-auto mt-2 max-w-md text-sm leading-relaxed text-ink-600">
                    Erzurum&apos;dan başladık, tüm Türkiye&apos;ye açılıyoruz. Tüm salonları
                    görmek için kuaförler sayfasına göz at.
                  </p>
                  <Link
                    href="/kuaforler"
                    className="mt-5 inline-flex h-11 items-center justify-center rounded-xl bg-ink-900 px-5 text-sm font-semibold text-paper transition-colors hover:bg-ink-800"
                  >
                    Kuaförlere göz at
                  </Link>
                </div>
              )}
            </div>
          </div>
        </section>

        {/* ── Kuaförler için ── */}
        <section
          id="isletmeler"
          aria-labelledby="isletmeler-baslik"
          className="scroll-mt-20"
        >
          <div className="mx-auto max-w-site px-4 py-16 sm:px-6 sm:py-24">
            <div className="grid items-center gap-10 lg:grid-cols-[1fr_1.1fr] lg:gap-16">
              <div>
                <p className="inline-flex items-center gap-2 rounded-full bg-copper-100 px-4 py-1.5 text-sm font-semibold text-copper-700">
                  <Store className="h-4 w-4" aria-hidden="true" />
                  Salon sahibi misin?
                </p>
                <h2
                  id="isletmeler-baslik"
                  className="text-balance mt-5 font-display text-3xl font-semibold text-ink-900 sm:text-4xl"
                >
                  Koltuğun dolsun, defter kapansın
                </h2>
                <p className="mt-4 text-base leading-relaxed text-ink-600 sm:text-lg">
                  Kuaforum&apos;a katılmak ücretsiz. Uygulamayı indir, üç adımda başvurunu
                  yap; onaylandığında salonun hem uygulamada hem bu sitede yerini alır.
                </p>
                <Link
                  href="/#indir"
                  className="mt-7 inline-flex h-12 items-center justify-center gap-2 rounded-xl bg-ink-900 px-6 text-base font-semibold text-paper transition-colors hover:bg-ink-800"
                >
                  Ücretsiz kaydol
                  <ArrowRight className="h-4 w-4" aria-hidden="true" />
                </Link>
                <p className="mt-3 text-sm text-ink-500">
                  Başvuru mobil uygulama üzerinden, üç adımda tamamlanır.
                </p>
              </div>

              <ul className="grid gap-4">
                {OWNER_BENEFITS.map((benefit) => (
                  <li
                    key={benefit.title}
                    className="flex gap-4 rounded-2xl border border-ink-900/8 bg-white p-5 shadow-card sm:p-6"
                  >
                    <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-paper-warm text-copper-700">
                      <benefit.icon className="h-5 w-5" aria-hidden="true" />
                    </span>
                    <div>
                      <h3 className="font-display text-lg font-semibold text-ink-900">
                        {benefit.title}
                      </h3>
                      <p className="mt-1.5 text-sm leading-relaxed text-ink-600">
                        {benefit.text}
                      </p>
                    </div>
                  </li>
                ))}
              </ul>
            </div>
          </div>
        </section>

        <AppDownloadSection />
      </main>
      <SiteFooter />
    </>
  );
}
