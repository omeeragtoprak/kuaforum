import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';
import { getLegalDocument } from '@/lib/api';
import { MarkdownContent } from '@/lib/markdown';
import { SiteHeader } from '@/components/site-header';
import { SiteFooter } from '@/components/site-footer';

// Backend allowlist'iyle birebir (LegalEndpoints.Documents) — bilinmeyen slug 404.
const KNOWN_DOCS = ['aydinlatma', 'kullanim-kosullari'] as const;

export const revalidate = 3600;

export function generateStaticParams(): { doc: string }[] {
  return KNOWN_DOCS.map((doc) => ({ doc }));
}

export async function generateMetadata(props: {
  params: Promise<{ doc: string }>;
}): Promise<Metadata> {
  const { doc } = await props.params;
  const document = await getLegalDocument(doc);
  return {
    title: document?.title ?? 'Yasal metin',
    robots: { index: false, follow: true },
    alternates: { canonical: `/legal/${doc}` },
  };
}

export default async function LegalPage(props: { params: Promise<{ doc: string }> }) {
  const { doc } = await props.params;
  if (!KNOWN_DOCS.includes(doc as (typeof KNOWN_DOCS)[number])) notFound();

  const document = await getLegalDocument(doc);

  return (
    <>
      <SiteHeader />
      <main id="icerik" className="flex-1">
        <div className="mx-auto max-w-3xl px-4 py-10 sm:px-6 sm:py-16">
          <nav aria-label="İçerik haritası" className="text-sm text-ink-500">
            <Link href="/" className="transition-colors hover:text-copper-700">
              Ana sayfa
            </Link>
            <span aria-hidden="true" className="mx-2">
              /
            </span>
            <span className="text-ink-900">{document?.title ?? 'Yasal metin'}</span>
          </nav>

          <article className="mt-8">
            {document ? (
              <>
                <MarkdownContent markdown={document.markdown} />
                <p className="mt-10 border-t border-ink-900/8 pt-4 text-xs text-ink-500">
                  Belge sürümü: {document.version}
                </p>
              </>
            ) : (
              <div className="rounded-2xl border border-dashed border-ink-900/15 bg-white/70 p-10 text-center">
                <h1 className="font-display text-xl font-semibold text-ink-900">
                  Metin şu anda yüklenemiyor
                </h1>
                <p className="mx-auto mt-2 max-w-md text-sm leading-relaxed text-ink-600">
                  Kısa bir teknik aksaklık olabilir. Lütfen birazdan tekrar dene.
                </p>
              </div>
            )}
          </article>
        </div>
      </main>
      <SiteFooter />
    </>
  );
}
