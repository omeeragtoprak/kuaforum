import Link from 'next/link';

export default function NotFound() {
  return (
    <div style={styles.container}>
      <div style={styles.card}>
        <h1 style={styles.title}>404 — Bulunamadı</h1>
        <p style={styles.message}>Aradığın sayfa burada değil.</p>
        <Link href="/" style={styles.link}>← Ana sayfa</Link>
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
    maxWidth: 480,
    background: '#fff',
    border: '1px solid #E5E5E5',
    borderRadius: 12,
    padding: 32,
    textAlign: 'center',
  },
  title: { fontSize: 28, fontWeight: 700, marginTop: 0, marginBottom: 12, color: '#1A1A1A' },
  message: { fontSize: 14, color: '#6B6B6B', marginBottom: 20 },
  link: { color: '#3F2E2E', fontSize: 14, fontWeight: 600, textDecoration: 'none' },
};
