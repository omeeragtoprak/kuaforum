import Link from 'next/link';
import { SearchX } from 'lucide-react';
import { SiteHeader } from '@/components/site-header';
import { SiteFooter } from '@/components/site-footer';

export default function NotFound() {
  return (
    <>
      <SiteHeader />
      <main id="icerik" className="flex flex-1 items-center">
        <div className="mx-auto max-w-site px-4 py-24 text-center sm:px-6">
          <SearchX className="mx-auto h-10 w-10 text-ink-400" aria-hidden="true" />
          <p className="mt-6 font-display text-6xl font-semibold text-paper-deep sm:text-7xl">
            404
          </p>
          <h1 className="text-balance mt-4 font-display text-2xl font-semibold text-ink-900 sm:text-3xl">
            Aradığın sayfa bulunamadı
          </h1>
          <p className="mx-auto mt-3 max-w-md text-sm leading-relaxed text-ink-600 sm:text-base">
            Bu salon artık yayında olmayabilir ya da bağlantı hatalı yazılmış olabilir.
          </p>
          <div className="mt-8 flex flex-col items-center justify-center gap-3 sm:flex-row">
            <Link
              href="/kuaforler"
              className="inline-flex h-12 items-center justify-center rounded-xl bg-copper px-6 text-base font-semibold text-white transition-colors hover:bg-copper-700"
            >
              Kuaförleri keşfet
            </Link>
            <Link
              href="/"
              className="inline-flex h-12 items-center justify-center rounded-xl border border-ink-900/15 px-6 text-base font-semibold text-ink-700 transition-colors hover:border-ink-900/30 hover:text-ink-900"
            >
              Ana sayfaya dön
            </Link>
          </div>
        </div>
      </main>
      <SiteFooter />
    </>
  );
}
