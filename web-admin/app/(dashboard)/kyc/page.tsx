'use client';

import { useEffect, useState } from 'react';
import { adminApi, formatDateTime, type KycQueueItem, ApiClientError } from '@/lib/api';

const STATUS_LABEL: Record<number, { text: string; color: string }> = {
  0: { text: 'Pending', color: '#F59E0B' },
  1: { text: 'Under Review', color: '#3B82F6' },
  2: { text: 'Approved', color: '#22C55E' },
  3: { text: 'Rejected', color: '#EF4444' },
};

export default function KycPage() {
  const [items, setItems] = useState<KycQueueItem[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [actionLoading, setActionLoading] = useState<string | null>(null);

  async function load() {
    try {
      const list = await adminApi.kycQueue();
      setItems(list);
    } catch (err) {
      if (err instanceof ApiClientError) setError(err.message);
    }
  }

  useEffect(() => { load(); }, []);

  async function handleApprove(id: string) {
    if (!confirm('Onayla? PSP sub-merchant key gerekli — şu an PLACEHOLDER kullanılacak.')) return;
    setActionLoading(id);
    try {
      const fakeKey = `MANUAL-${id.slice(0, 8)}`;
      await adminApi.reviewKyc(id, true, undefined, fakeKey);
      await load();
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Hata');
    } finally {
      setActionLoading(null);
    }
  }

  async function handleReject(id: string) {
    const reason = prompt('Red sebebi:');
    if (!reason) return;
    setActionLoading(id);
    try {
      await adminApi.reviewKyc(id, false, reason);
      await load();
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Hata');
    } finally {
      setActionLoading(null);
    }
  }

  return (
    <div>
      <h1 style={styles.h1}>KYC Kuyruğu</h1>
      <p style={styles.subtitle}>{items.length} başvuru</p>

      {error ? <div style={styles.error}>{error}</div> : null}
      {items.length === 0 && !error ? <div style={styles.empty}>Bekleyen KYC başvurusu yok.</div> : null}

      <div style={styles.list}>
        {items.map((item) => {
          const status = STATUS_LABEL[item.status] ?? { text: '?', color: '#9B9B9B' };
          return (
            <div key={item.submissionId} style={styles.row}>
              <div style={styles.rowLeft}>
                <div style={styles.shopName}>{item.barberShopName}</div>
                <div style={styles.meta}>
                  Sahibi: {item.submitterName ?? '—'} · {item.documentCount} belge
                </div>
                <div style={styles.metaSmall}>{formatDateTime(item.submittedAt)}</div>
              </div>
              <div style={styles.rowRight}>
                <span style={{ ...styles.badge, background: status.color }}>{status.text}</span>
                {(item.status === 0 || item.status === 1) ? (
                  <div style={styles.actions}>
                    <button
                      style={styles.approveBtn}
                      onClick={() => handleApprove(item.submissionId)}
                      disabled={actionLoading === item.submissionId}
                    >
                      Onayla
                    </button>
                    <button
                      style={styles.rejectBtn}
                      onClick={() => handleReject(item.submissionId)}
                      disabled={actionLoading === item.submissionId}
                    >
                      Reddet
                    </button>
                  </div>
                ) : null}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  h1: { fontSize: 32, fontWeight: 700, marginTop: 0, marginBottom: 4, color: '#1A1A1A' },
  subtitle: { color: '#6B6B6B', marginTop: 0, marginBottom: 24 },
  list: { display: 'flex', flexDirection: 'column', gap: 12 },
  row: {
    background: '#fff',
    padding: 20,
    borderRadius: 12,
    border: '1px solid #E5E5E5',
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    gap: 16,
  },
  rowLeft: { flex: 1 },
  rowRight: { display: 'flex', flexDirection: 'column', gap: 8, alignItems: 'flex-end' },
  shopName: { fontSize: 16, fontWeight: 600, color: '#1A1A1A' },
  meta: { fontSize: 13, color: '#6B6B6B', marginTop: 4 },
  metaSmall: { fontSize: 11, color: '#9B9B9B', marginTop: 4 },
  badge: { color: '#fff', padding: '4px 10px', borderRadius: 999, fontSize: 12, fontWeight: 600 },
  actions: { display: 'flex', gap: 8, marginTop: 8 },
  approveBtn: {
    background: '#22C55E', color: '#fff', border: 'none', padding: '6px 14px',
    borderRadius: 6, fontSize: 13, fontWeight: 600, cursor: 'pointer',
  },
  rejectBtn: {
    background: '#EF4444', color: '#fff', border: 'none', padding: '6px 14px',
    borderRadius: 6, fontSize: 13, fontWeight: 600, cursor: 'pointer',
  },
  empty: { padding: 32, textAlign: 'center', color: '#6B6B6B' },
  error: {
    padding: 12, background: '#FEF2F2', color: '#991B1B',
    borderRadius: 6, fontSize: 14, marginBottom: 16,
  },
};
