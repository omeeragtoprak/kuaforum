'use client';

import { useEffect } from 'react';

export default function GlobalError({ error, reset }: { error: Error & { digest?: string }; reset: () => void }) {
  useEffect(() => {
    console.error('[admin error boundary]', error);
  }, [error]);

  return (
    <div style={styles.container}>
      <div style={styles.card}>
        <h1 style={styles.title}>Bir şeyler ters gitti</h1>
        <p style={styles.message}>{error.message || 'Beklenmeyen bir hata oluştu.'}</p>
        {error.digest ? <p style={styles.digest}>İz: {error.digest}</p> : null}
        <div style={styles.actions}>
          <button onClick={reset} style={styles.primaryBtn}>Tekrar dene</button>
          <button onClick={() => window.location.assign('/')} style={styles.secondaryBtn}>Ana sayfa</button>
        </div>
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  container: {
    minHeight: '100vh',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    background: '#FAFAFA',
    padding: 24,
  },
  card: {
    maxWidth: 520,
    background: '#fff',
    border: '1px solid #E5E5E5',
    borderRadius: 12,
    padding: 32,
    textAlign: 'center',
  },
  title: { fontSize: 24, fontWeight: 700, marginTop: 0, marginBottom: 12, color: '#1A1A1A' },
  message: { fontSize: 14, color: '#6B6B6B', marginBottom: 8, lineHeight: 1.6 },
  digest: { fontSize: 11, color: '#9B9B9B', fontFamily: 'monospace' },
  actions: { display: 'flex', gap: 12, justifyContent: 'center', marginTop: 20 },
  primaryBtn: {
    background: '#3F2E2E',
    color: '#fff',
    border: 'none',
    padding: '10px 18px',
    borderRadius: 6,
    fontSize: 14,
    fontWeight: 600,
    cursor: 'pointer',
  },
  secondaryBtn: {
    background: '#fff',
    color: '#3F2E2E',
    border: '1px solid #D4D4D4',
    padding: '10px 18px',
    borderRadius: 6,
    fontSize: 14,
    cursor: 'pointer',
  },
};
