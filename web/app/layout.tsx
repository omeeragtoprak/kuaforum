import type { Metadata, Viewport } from 'next';
import { Fraunces, Manrope } from 'next/font/google';
import { SITE_URL } from '@/lib/api';
import './globals.css';

// Türkçe karakter desteği (ğüşıöç) için latin-ext zorunlu; font-display: swap → CLS'siz.
const displayFont = Fraunces({
  subsets: ['latin', 'latin-ext'],
  display: 'swap',
  variable: '--font-display',
  axes: ['opsz'],
});

const bodyFont = Manrope({
  subsets: ['latin', 'latin-ext'],
  display: 'swap',
  variable: '--font-sans',
});

export const metadata: Metadata = {
  metadataBase: new URL(SITE_URL),
  title: {
    default: 'Kuaforum — Türkiye\'nin Kuaför Randevu Platformu',
    template: '%s | Kuaforum',
  },
  description:
    'Çevrendeki kuaför ve berberleri keşfet, hizmetleri ve fiyatları karşılaştır, randevunu iki dakikada al. Sıra beklemek tarihe karışıyor.',
  openGraph: {
    type: 'website',
    locale: 'tr_TR',
    siteName: 'Kuaforum',
  },
  robots: { index: true, follow: true },
};

export const viewport: Viewport = {
  themeColor: '#181310',
  width: 'device-width',
  initialScale: 1,
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="tr" className={`${displayFont.variable} ${bodyFont.variable}`}>
      <body className="flex min-h-dvh flex-col">
        <a
          href="#icerik"
          className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-50 focus:rounded-lg focus:bg-ink-900 focus:px-4 focus:py-2 focus:text-paper"
        >
          İçeriğe atla
        </a>
        {children}
      </body>
    </html>
  );
}
