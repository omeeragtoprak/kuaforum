'use client';

import { ReactNode, useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter, usePathname } from 'next/navigation';
import { adminAuth } from '@/lib/api';

const NAV = [
  { href: '/', label: 'Dashboard' },
  { href: '/users', label: 'Kullanıcılar' },
  { href: '/kyc', label: 'KYC Kuyruğu' },
  { href: '/reviews', label: 'Yorum Moderasyonu' },
];

export default function DashboardLayout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const [ready, setReady] = useState(false);

  useEffect(() => {
    if (!adminAuth.getToken()) {
      router.replace('/login');
      return;
    }
    setReady(true);
  }, [router]);

  if (!ready) return null;

  return (
    <div style={styles.layout}>
      <aside style={styles.sidebar}>
        <div style={styles.brand}>Kuaforum</div>
        <nav style={styles.nav}>
          {NAV.map((item) => (
            <Link
              key={item.href}
              href={item.href}
              style={{
                ...styles.navLink,
                ...(pathname === item.href ? styles.navLinkActive : {}),
              }}
            >
              {item.label}
            </Link>
          ))}
        </nav>
        <button
          onClick={() => {
            adminAuth.clear();
            router.replace('/login');
          }}
          style={styles.logoutBtn}
        >
          Çıkış yap
        </button>
      </aside>
      <main style={styles.main}>{children}</main>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  layout: { display: 'flex', minHeight: '100vh', background: '#FAFAFA' },
  sidebar: {
    width: 240,
    background: '#3F2E2E',
    color: '#fff',
    padding: 24,
    display: 'flex',
    flexDirection: 'column',
    gap: 24,
  },
  brand: { fontSize: 24, fontWeight: 700, letterSpacing: '-0.5px' },
  nav: { display: 'flex', flexDirection: 'column', gap: 4, flex: 1 },
  navLink: {
    padding: '10px 12px',
    color: '#E5E5E5',
    textDecoration: 'none',
    borderRadius: 6,
    fontSize: 14,
  },
  navLinkActive: { background: 'rgba(255,255,255,0.1)', color: '#fff', fontWeight: 600 },
  logoutBtn: {
    background: 'transparent',
    color: '#E5E5E5',
    border: '1px solid rgba(255,255,255,0.2)',
    padding: '8px 12px',
    borderRadius: 6,
    cursor: 'pointer',
    fontSize: 13,
  },
  main: { flex: 1, padding: 32, overflowY: 'auto' },
};
