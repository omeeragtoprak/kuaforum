'use client';

import { useEffect, useState } from 'react';
import { adminApi, formatTry, type DashboardStats, ApiClientError } from '@/lib/api';

export default function DashboardPage() {
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    adminApi
      .dashboard()
      .then(setStats)
      .catch((err) => {
        if (err instanceof ApiClientError) setError(err.message);
        else setError('Bilinmeyen hata');
      });
  }, []);

  return (
    <div>
      <h1 style={styles.h1}>Dashboard</h1>
      {error ? <div style={styles.error}>{error}</div> : null}
      {!stats && !error ? <div>Yükleniyor...</div> : null}

      {stats ? (
        <div style={styles.grid}>
          <Stat label="Toplam Kullanıcı" value={stats.totalUsers.toString()} />
          <Stat label="Müşteri" value={stats.customerCount.toString()} />
          <Stat label="Kuaför" value={stats.barberOwnerCount.toString()} />
          <Stat label="Aktif Salon" value={stats.activeBarberShops.toString()} accent />
          <Stat label="Bekleyen KYC" value={stats.pendingKycCount.toString()} warn={stats.pendingKycCount > 0} />
          <Stat label="Bekleyen Yorum" value={stats.pendingReviewsCount.toString()} warn={stats.pendingReviewsCount > 0} />
          <Stat label="Bugünkü Randevu" value={stats.todaysBookings.toString()} />
          <Stat label="Bugünkü GMV" value={formatTry(stats.todaysGmvMinor)} accent />
        </div>
      ) : null}
    </div>
  );
}

function Stat({
  label,
  value,
  accent,
  warn,
}: {
  label: string;
  value: string;
  accent?: boolean;
  warn?: boolean;
}) {
  return (
    <div style={{ ...styles.card, ...(warn ? styles.cardWarn : {}) }}>
      <div style={styles.statLabel}>{label}</div>
      <div style={{ ...styles.statValue, ...(accent ? styles.statValueAccent : {}) }}>{value}</div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  h1: { fontSize: 32, fontWeight: 700, marginTop: 0, marginBottom: 24, color: '#1A1A1A' },
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
  },
  cardWarn: { borderColor: '#FCA5A5', background: '#FEF2F2' },
  statLabel: { fontSize: 13, color: '#6B6B6B', marginBottom: 8 },
  statValue: { fontSize: 28, fontWeight: 700, color: '#1A1A1A' },
  statValueAccent: { color: '#6B4F4F' },
  error: {
    padding: 12,
    background: '#FEF2F2',
    color: '#991B1B',
    borderRadius: 6,
    fontSize: 14,
    marginBottom: 16,
  },
};
