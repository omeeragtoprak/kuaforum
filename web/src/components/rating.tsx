import { Star } from 'lucide-react';
import { formatRating } from '@/lib/format';

interface RatingProps {
  average: number | null;
  count: number;
  /** Uzun biçim "(12 değerlendirme)" — profil sayfası; kısa biçim "(12)" — kartlar */
  long?: boolean;
}

export function Rating({ average, count, long = false }: RatingProps) {
  if (average == null || count === 0) {
    return <span className="text-sm text-ink-500">Henüz değerlendirilmedi</span>;
  }

  const label = `5 üzerinden ${formatRating(average)} puan, ${count} değerlendirme`;

  return (
    <span className="inline-flex items-center gap-1.5" aria-label={label} role="img">
      <Star className="h-4 w-4 fill-gold text-gold" aria-hidden="true" />
      <span className="tabular text-sm font-semibold text-ink-900">
        {formatRating(average)}
      </span>
      <span className="tabular text-sm text-ink-500">
        {long ? `(${count} değerlendirme)` : `(${count})`}
      </span>
    </span>
  );
}
