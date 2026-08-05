import Link from 'next/link';
import { ChevronLeft, ChevronRight } from 'lucide-react';

interface PaginationProps {
  page: number;
  totalPages: number;
  /** Sayfa numarası için URL üretici — diğer query paramlarını korur */
  hrefFor: (page: number) => string;
}

const linkBase =
  'inline-flex h-11 items-center gap-1 rounded-xl border px-4 text-sm font-medium transition-colors';
const linkActive =
  'border-ink-900/10 bg-white text-ink-900 hover:border-copper hover:text-copper';
const linkDisabled = 'pointer-events-none border-ink-900/5 bg-paper-warm text-ink-400';

export function Pagination({ page, totalPages, hrefFor }: PaginationProps) {
  if (totalPages <= 1) return null;

  const prevDisabled = page <= 1;
  const nextDisabled = page >= totalPages;

  return (
    <nav aria-label="Sayfalama" className="mt-10 flex items-center justify-center gap-3">
      <Link
        href={hrefFor(page - 1)}
        aria-disabled={prevDisabled}
        tabIndex={prevDisabled ? -1 : undefined}
        className={`${linkBase} ${prevDisabled ? linkDisabled : linkActive}`}
      >
        <ChevronLeft className="h-4 w-4" aria-hidden="true" />
        Önceki
      </Link>

      <span className="tabular px-2 text-sm text-ink-500">
        Sayfa <strong className="font-semibold text-ink-900">{page}</strong> / {totalPages}
      </span>

      <Link
        href={hrefFor(page + 1)}
        aria-disabled={nextDisabled}
        tabIndex={nextDisabled ? -1 : undefined}
        className={`${linkBase} ${nextDisabled ? linkDisabled : linkActive}`}
      >
        Sonraki
        <ChevronRight className="h-4 w-4" aria-hidden="true" />
      </Link>
    </nav>
  );
}
