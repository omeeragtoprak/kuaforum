'use client';

import { useState, FormEvent } from 'react';
import { useRouter } from 'next/navigation';
import { adminApi, adminAuth, ApiClientError } from '@/lib/api';

export default function LoginPage() {
  const router = useRouter();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [totp, setTotp] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setLoading(true);
    setError(null);
    try {
      const res = await adminApi.login(email, password, totp);
      adminAuth.setToken(res.accessToken);
      router.replace('/');
    } catch (err) {
      if (err instanceof ApiClientError) {
        setError(err.message);
      } else {
        setError('Bilinmeyen hata');
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <main style={styles.page}>
      <form onSubmit={handleSubmit} style={styles.card}>
        <h1 style={styles.title}>Kuaforum Admin</h1>
        <p style={styles.subtitle}>Yönetici hesabınla giriş yap</p>

        <label style={styles.label}>
          E-posta
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            autoComplete="email"
            style={styles.input}
          />
        </label>

        <label style={styles.label}>
          Parola
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            autoComplete="current-password"
            style={styles.input}
          />
        </label>

        <label style={styles.label}>
          TOTP Kodu (Authenticator)
          <input
            type="text"
            value={totp}
            onChange={(e) => setTotp(e.target.value.replace(/\D/g, '').slice(0, 6))}
            inputMode="numeric"
            pattern="\d{6}"
            placeholder="123456"
            style={styles.input}
          />
        </label>

        {error ? <div style={styles.error}>{error}</div> : null}

        <button type="submit" disabled={loading} style={styles.button}>
          {loading ? 'Giriş yapılıyor...' : 'Giriş yap'}
        </button>

        <div style={styles.note}>
          🚧 Admin login + TOTP backend'de henüz wired değil (M1 spec'te tanımlı, implementation bekliyor).
          Şu an bu sayfa hata gösterecek. Backend ekibinin ilk admin kullanıcı seed etmesi + TOTP endpoint
          aktive etmesi sonrası çalışacak.
        </div>
      </form>
    </main>
  );
}

const styles: Record<string, React.CSSProperties> = {
  page: {
    minHeight: '100vh',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    background: '#FAFAFA',
    padding: 24,
  },
  card: {
    background: '#fff',
    padding: 32,
    borderRadius: 12,
    width: '100%',
    maxWidth: 420,
    boxShadow: '0 4px 24px rgba(0,0,0,0.06)',
    display: 'flex',
    flexDirection: 'column',
    gap: 16,
  },
  title: { fontSize: 28, fontWeight: 700, color: '#3F2E2E', margin: 0 },
  subtitle: { fontSize: 14, color: '#6B6B6B', margin: 0, marginBottom: 8 },
  label: { display: 'flex', flexDirection: 'column', gap: 4, fontSize: 13, color: '#555', fontWeight: 500 },
  input: {
    padding: '10px 12px',
    border: '1px solid #E5E5E5',
    borderRadius: 8,
    fontSize: 14,
    outline: 'none',
  },
  button: {
    padding: '12px 24px',
    background: '#6B4F4F',
    color: '#fff',
    border: 'none',
    borderRadius: 8,
    fontSize: 14,
    fontWeight: 600,
    cursor: 'pointer',
    marginTop: 8,
  },
  error: {
    padding: '10px 12px',
    background: '#FEF2F2',
    color: '#991B1B',
    borderRadius: 6,
    fontSize: 13,
  },
  note: {
    padding: '10px 12px',
    background: '#FFFBEB',
    color: '#92400E',
    borderRadius: 6,
    fontSize: 12,
    lineHeight: 1.5,
  },
};
