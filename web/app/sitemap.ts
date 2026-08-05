import type { MetadataRoute } from 'next';
import { getAllShopSlugs, getCities, SITE_URL } from '@/lib/api';

// Build sırasında API kapalıysa yalnız statik rotalar döner (fetch'ler null/[]).
export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  // Legal sayfalar bilinçli dışarıda: placeholder metinle noindex'ler
  // (bkz. legal/[doc] metadata) — sitemap'e koymak çelişki olur.
  const staticRoutes: MetadataRoute.Sitemap = [
    { url: SITE_URL, changeFrequency: 'weekly', priority: 1 },
    { url: `${SITE_URL}/kuaforler`, changeFrequency: 'daily', priority: 0.9 },
  ];

  const [cities, slugs] = await Promise.all([getCities(), getAllShopSlugs()]);

  const cityRoutes: MetadataRoute.Sitemap = (cities ?? [])
    .filter((c) => c.activeShopCount > 0)
    .map((c) => ({
      url: `${SITE_URL}/kuaforler?city=${c.id}`,
      changeFrequency: 'daily' as const,
      priority: 0.7,
    }));

  const shopRoutes: MetadataRoute.Sitemap = slugs.map((slug) => ({
    url: `${SITE_URL}/salon/${slug}`,
    changeFrequency: 'weekly' as const,
    priority: 0.8,
  }));

  return [...staticRoutes, ...cityRoutes, ...shopRoutes];
}
