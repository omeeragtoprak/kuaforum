import Link from 'next/link';
import { Scissors } from 'lucide-react';

// JS'siz, yapışkan, yarı saydam header. Mobilde (360px) sığması için nav
// linkleri kısaltılmış; hamburger menü bilinçli yok — 3 hedef link için gereksiz.
export function SiteHeader() {
  return (
    <header className="sticky top-0 z-40 border-b border-ink-900/8 bg-paper/85 backdrop-blur-md">
      <div className="mx-auto flex h-16 max-w-site items-center justify-between gap-3 px-4 sm:px-6">
        <Link
          href="/"
          className="flex items-center gap-2 rounded-md"
          aria-label="Kuaforum ana sayfa"
        >
          <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-ink-900 text-paper">
            <Scissors className="h-4 w-4" aria-hidden="true" />
          </span>
          <span className="font-display text-xl font-semibold tracking-tight text-ink-900">
            Kuaforum
          </span>
        </Link>

        <nav aria-label="Ana gezinme" className="flex items-center gap-1 sm:gap-2">
          <Link
            href="/kuaforler"
            className="rounded-lg px-3 py-2 text-sm font-medium text-ink-700 transition-colors hover:bg-paper-warm hover:text-ink-900"
          >
            Kuaförler
          </Link>
          <Link
            href="/#isletmeler"
            className="hidden rounded-lg px-3 py-2 text-sm font-medium text-ink-700 transition-colors hover:bg-paper-warm hover:text-ink-900 sm:block"
          >
            İşletmeler için
          </Link>
          <Link
            href="/#indir"
            className="rounded-lg bg-copper px-3.5 py-2 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-copper-700 sm:px-4"
          >
            Uygulamayı indir
          </Link>
        </nav>
      </div>
    </header>
  );
}
