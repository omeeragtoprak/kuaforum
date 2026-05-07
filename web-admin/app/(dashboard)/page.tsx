'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { adminApi, formatTry, type DashboardStats, ApiClientError } from '@/lib/api';

export default function DashboardPage() {
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
  const router = useRouter();

  async function load() {
    setLoading(true);
    setError(null);
    try {
      const s = await adminApi.dashboard();
      setStats(s);
      setLastUpdated(new Date());
    } catch (err) {
      if (err instanceof ApiClientError) setError(err.message);
      else setError('Sunucuya ulaşılamadı.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { load(); }, []);

  return (
    <div>
      <div style={styles.headerRow}>
        <div>
          <h1 style={styles.h1}>Dashboard</h1>
          {lastUpdated ? (
            <p style={styles.subtitle}>
              Son güncelleme: {lastUpdated.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })}
            </p>
          ) : null}
        </div>
        <button onClick={load} disabled={loading} style={styles.refreshBtn}>
          {loading ? '⟳ Yükleniyor…' : '⟳ Yenile'}
        </button>
      </div>

      {error ? <div style={styles.error}>{error}</div> : null}

      {/* Bekleyen KYC > 0 → her şeyin üstüne çağrı bandı */}
      {stats && stats.pendingKycCount > 0 ? (
        <div style={styles.kycBanner}>
          <div>
            <strong style={styles.kycBannerTitle}>{stats.pendingKycCount} KYC başvurusu bekliyor</strong>
            <div style={styles.kycBannerSub}>İşletme onayları admin incelemesini bekliyor — geciken her gün gelir kaybı.</div>
          </div>
          <button style={styles.kycBannerBtn} onClick={() => router.push('/kyc')}>
            Şimdi incele →
          </button>
        </div>
      ) : null}

      {loading && !stats ? (
        <div style={styles.grid}>
          {Array.from({ length: 8 }).map((_, i) => (
            <div key={i} style={{ ...styles.card, ...styles.skeleton }} />
          ))}
        </div>
      ) : null}

      {stats ? (
        <>
          <h2 style={styles.sectionTitle}>Pazaryeri</h2>
          <div style={styles.grid}>
            <Stat label="Aktif Salon" value={stats.activeBarberShops.toString()} accent />
            <Stat label="Toplam Kullanıcı" value={stats.totalUsers.toString()} />
            <Stat label="Müşteri" value={stats.customerCount.toString()} />
            <Stat label="Kuaför" value={stats.barberOwnerCount.toString()} />
          </div>

          <h2 style={styles.sectionTitle}>Bugün</h2>
          <div style={styles.grid}>
            <Stat label="Bugünkü Randevu" value={stats.todaysBookings.toString()} />
            <Stat label="Bugünkü GMV" value={formatTry(stats.todaysGmvMinor)} accent />
          </div>

          <h2 style={styles.sectionTitle}>Moderasyon kuyruğu</h2>
          <div style={styles.grid}>
            <Stat
              label="Bekleyen KYC"
              value={stats.pendingKycCount.toString()}
              warn={stats.pendingKycCount > 0}
              onClick={() => router.push('/kyc')}
            />
            <Stat
              label="Bekleyen Yorum"
              value={stats.pendingReviewsCount.toString()}
              warn={stats.pendingReviewsCount > 0}
              onClick={() => router.push('/reviews')}
            />
          </div>
        </>
      ) : null}
    </div>
  );
}

function Stat({
  label,
  value,
  accent,
  warn,
  onClick,
}: {
  label: string;
  value: string;
  accent?: boolean;
  warn?: boolean;
  onClick?: () => void;
}) {
  const cardStyle: React.CSSProperties = {
    ...styles.card,
    ...(warn ? styles.cardWarn : {}),
    ...(onClick ? { cursor: 'pointer' } : {}),
  };
  return (
    <div style={cardStyle} onClick={onClick} role={onClick ? 'button' : undefined}>
      <div style={styles.statLabel}>{label}</div>
      <div style={{ ...styles.statValue, ...(accent ? styles.statValueAccent : {}) }}>{value}</div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  headerRow: { display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 24 },
  h1: { fontSize: 32, fontWeight: 700, marginTop: 0, marginBottom: 4, color: '#1A1A1A' },
  subtitle: { color: '#6B6B6B', marginTop: 0, marginBottom: 0, fontSize: 13 },
  refreshBtn: {
    background: '#F1F5F9',
    color: '#475569',
    border: '1px solid #E2E8F0',
    padding: '8px 16px',
    borderRadius: 8,
    fontSize: 13,
    fontWeight: 600,
    cursor: 'pointer',
  },
  sectionTitle: {
    fontSize: 14,
    fontWeight: 700,
    color: '#475569',
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    marginTop: 32,
    marginBottom: 12,
  },
  grid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))',
    gap: 16,
  },
  card: {
    background: '#fff',
    padding: 20,
    borderRadius: 12,
    border: '1px solid #E5E5E5',
    transition: 'transform 0.1s, box-shadow 0.1s',
  },
  cardWarn: { borderColor: '#FCA5A5', background: '#FEF2F2' },
  skeleton: {
    background: 'linear-gradient(90deg, #F1F5F9 0%, #E2E8F0 50%, #F1F5F9 100%)',
    backgroundSize: '200% 100%',
    animation: 'shimmer 1.4s infinite',
    height: 84,
  },
  statLabel: { fontSize: 13, color: '#6B6B6B', marginBottom: 8 },
  statValue: { fontSize: 28, fontWeight: 700, color: '#1A1A1A' },
  statValueAccent: { color: '#7C3AED' },
  error: {
    padding: 12,
    background: '#FEF2F2',
    color: '#991B1B',
    borderRadius: 6,
    fontSize: 14,
    marginBottom: 16,
  },
  kycBanner: {
    background: 'linear-gradient(135deg, #F59E0B, #EF4444)',
    color: '#fff',
    padding: 20,
    borderRadius: 12,
    marginBottom: 24,
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: 16,
    boxShadow: '0 4px 12px rgba(245, 158, 11, 0.25)',
  },
  kycBannerTitle: { fontSize: 18, fontWeight: 700 },
  kycBannerSub: { fontSize: 13, opacity: 0.9, marginTop: 4 },
  kycBannerBtn: {
    background: '#fff',
    color: '#EF4444',
    border: 'none',
    padding: '10px 20px',
    borderRadius: 8,
    fontSize: 14,
    fontWeight: 700,
    cursor: 'pointer',
    whiteSpace: 'nowrap',
  },
};
