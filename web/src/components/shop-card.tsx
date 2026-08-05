import Link from 'next/link';
import { ArrowUpRight, MapPin } from 'lucide-react';
import type { PublicShopListItem } from '@/lib/api';
import { Rating } from '@/components/rating';

// Monogram avatar: stok foto yok (RFC-011 — galeri V1.5). Salon adının ilk harfi,
// isme göre deterministik sıcak tonlu zemin.
const MONOGRAM_TONES = [
  'bg-copper-100 text-copper-700',
  'bg-paper-deep text-ink-700',
  'bg-copper-200 text-copper-700',
  'bg-paper-warm text-ink-600',
] as const;

function monogramTone(name: string): string {
  let hash = 0;
  for (const ch of name) hash = (hash * 31 + ch.codePointAt(0)!) % 997;
  return MONOGRAM_TONES[hash % MONOGRAM_TONES.length];
}

export function ShopCard({ shop }: { shop: PublicShopListItem }) {
  const location = [shop.districtName, shop.cityName].filter(Boolean).join(', ');

  return (
    <article className="card-lift group relative rounded-2xl border border-ink-900/8 bg-white">
      <div className="flex items-start gap-4 p-5">
        <span
          aria-hidden="true"
          className={`flex h-14 w-14 shrink-0 items-center justify-center rounded-xl font-display text-2xl font-semibold ${monogramTone(shop.name)}`}
        >
          {shop.name.charAt(0).toLocaleUpperCase('tr-TR')}
        </span>

        <div className="min-w-0 flex-1">
          <h3 className="truncate font-display text-lg font-semibold text-ink-900">
            <Link
              href={`/salon/${shop.slug}`}
              className="rounded-sm after:absolute after:inset-0 after:rounded-2xl"
            >
              {shop.name}
            </Link>
          </h3>

          {location && (
            <p className="mt-1 flex items-center gap-1 text-sm text-ink-500">
              <MapPin className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
              <span className="truncate">{location}</span>
            </p>
          )}

          <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-1">
            <Rating average={shop.ratingAverage} count={shop.ratingCount} />
            {shop.serviceCount > 0 && (
              <span className="tabular text-sm text-ink-500">
                {shop.serviceCount} hizmet
              </span>
            )}
          </div>
        </div>

        <ArrowUpRight
          className="h-5 w-5 shrink-0 text-ink-400 transition-all duration-300 group-hover:translate-x-0.5 group-hover:-translate-y-0.5 group-hover:text-copper"
          aria-hidden="true"
        />
      </div>
    </article>
  );
}
