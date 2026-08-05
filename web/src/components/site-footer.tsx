import Link from 'next/link';
import { Scissors } from 'lucide-react';

const YEAR = new Date().getFullYear();

export function SiteFooter() {
  return (
    <footer className="border-t border-paper/10 bg-ink-950 text-paper/70">
      <div className="pinstripe-dark">
        <div className="mx-auto max-w-site px-4 py-12 sm:px-6 sm:py-16">
          <div className="grid gap-10 sm:grid-cols-2 lg:grid-cols-4">
            <div className="max-w-xs">
              <div className="flex items-center gap-2">
                <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-paper text-ink-900">
                  <Scissors className="h-4 w-4" aria-hidden="true" />
                </span>
                <span className="font-display text-xl font-semibold text-paper">Kuaforum</span>
              </div>
              <p className="mt-4 text-sm leading-relaxed">
                Türkiye&apos;nin kuaför randevu platformu. Keşfet, karşılaştır, randevunu
                anında al.
              </p>
            </div>

            <nav aria-label="Keşfet">
              <h2 className="text-sm font-semibold uppercase tracking-wider text-paper/50">
                Keşfet
              </h2>
              <ul className="mt-4 space-y-2.5 text-sm">
                <li>
                  <Link href="/kuaforler" className="transition-colors hover:text-paper">
                    Kuaförler
                  </Link>
                </li>
                <li>
                  <Link href="/#nasil-calisir" className="transition-colors hover:text-paper">
                    Nasıl çalışır?
                  </Link>
                </li>
                <li>
                  <Link href="/#indir" className="transition-colors hover:text-paper">
                    Uygulamayı indir
                  </Link>
                </li>
              </ul>
            </nav>

            <nav aria-label="İşletmeler">
              <h2 className="text-sm font-semibold uppercase tracking-wider text-paper/50">
                İşletmeler
              </h2>
              <ul className="mt-4 space-y-2.5 text-sm">
                <li>
                  <Link href="/#isletmeler" className="transition-colors hover:text-paper">
                    Salonunu ekle
                  </Link>
                </li>
              </ul>
            </nav>

            <nav aria-label="Yasal">
              <h2 className="text-sm font-semibold uppercase tracking-wider text-paper/50">
                Yasal
              </h2>
              <ul className="mt-4 space-y-2.5 text-sm">
                <li>
                  <Link href="/legal/aydinlatma" className="transition-colors hover:text-paper">
                    KVKK Aydınlatma Metni
                  </Link>
                </li>
                <li>
                  <Link
                    href="/legal/kullanim-kosullari"
                    className="transition-colors hover:text-paper"
                  >
                    Kullanım Koşulları
                  </Link>
                </li>
              </ul>
            </nav>
          </div>

          <div className="mt-12 flex flex-col gap-2 border-t border-paper/10 pt-6 text-xs text-paper/40 sm:flex-row sm:items-center sm:justify-between">
            <p>© {YEAR} Kuaforum. Tüm hakları saklıdır.</p>
            <p>Erzurum&apos;da tasarlandı, Türkiye için geliştiriliyor.</p>
          </div>
        </div>
      </div>
    </footer>
  );
}
