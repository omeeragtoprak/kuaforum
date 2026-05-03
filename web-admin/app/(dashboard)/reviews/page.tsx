'use client';

import { useEffect, useState } from 'react';
import { adminApi, formatDateTime, type PendingReview, ApiClientError } from '@/lib/api';

const REJECT_REASONS = [
  { value: 0, label: 'Profanity / Küfür' },
  { value: 1, label: 'Spam' },
  { value: 2, label: 'Off-topic' },
  { value: 3, label: 'Personal Attack' },
  { value: 99, label: 'Other' },
];

export default function ReviewsPage() {
  const [items, setItems] = useState<PendingReview[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  async function load() {
    try {
      const list = await adminApi.pendingReviews();
      setItems(list);
    } catch (err) {
      if (err instanceof ApiClientError) setError(err.message);
    }
  }

  useEffect(() => { load(); }, []);

  async function approve(id: string) {
    setBusy(id);
    try {
      await adminApi.approveReview(id);
      await load();
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Hata');
    } finally {
      setBusy(null);
    }
  }

  async function reject(id: string, reason: number) {
    setBusy(id);
    try {
      await adminApi.rejectReview(id, reason);
      await load();
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Hata');
    } finally {
      setBusy(null);
    }
  }

  return (
    <div>
      <h1 style={styles.h1}>Yorum Moderasyonu</h1>
      <p style={styles.subtitle}>{items.length} bekleyen yorum</p>

      {error ? <div style={styles.error}>{error}</div> : null}
      {items.length === 0 && !error ? <div style={styles.empty}>Bekleyen yorum yok.</div> : null}

      <div style={styles.list}>
        {items.map((r) => (
          <div key={r.id} style={styles.row}>
            <div style={styles.rowHead}>
              <div style={styles.stars}>{'★'.repeat(r.rating)}{'☆'.repeat(5 - r.rating)}</div>
              <div style={styles.date}>{formatDateTime(r.createdAt)}</div>
            </div>
            <div style={styles.body}>{r.body || <em>(metin yok)</em>}</div>
            <div style={styles.meta}>Müşteri: {r.customerId.slice(0, 8)} · Stilist: {r.stylistId.slice(0, 8)}</div>
            <div style={styles.actions}>
              <button style={styles.approveBtn} onClick={() => approve(r.id)} disabled={busy === r.id}>
                Onayla
              </button>
              {REJECT_REASONS.map((rej) => (
                <button
                  key={rej.value}
                  style={styles.rejectBtn}
                  onClick={() => reject(r.id, rej.value)}
                  disabled={busy === r.id}
                  title={rej.label}
                >
                  Red: {rej.label}
                </button>
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  h1: { fontSize: 32, fontWeight: 700, marginTop: 0, marginBottom: 4, color: '#1A1A1A' },
  subtitle: { color: '#6B6B6B', marginTop: 0, marginBottom: 24 },
  list: { display: 'flex', flexDirection: 'column', gap: 12 },
  row: {
    background: '#fff', padding: 20, borderRadius: 12, border: '1px solid #E5E5E5',
    display: 'flex', flexDirection: 'column', gap: 8,
  },
  rowHead: { display: 'flex', justifyContent: 'space-between', alignItems: 'center' },
  stars: { fontSize: 20, color: '#E8B86D', letterSpacing: 2 },
  date: { fontSize: 12, color: '#9B9B9B' },
  body: { fontSize: 14, color: '#1A1A1A', lineHeight: 1.5, paddingVertical: 4 },
  meta: { fontSize: 11, color: '#9B9B9B' },
  actions: { display: 'flex', gap: 6, flexWrap: 'wrap', marginTop: 8 },
  approveBtn: {
    background: '#22C55E', color: '#fff', border: 'none', padding: '6px 14px',
    borderRadius: 6, fontSize: 12, fontWeight: 600, cursor: 'pointer',
  },
  rejectBtn: {
    background: '#fff', color: '#991B1B', border: '1px solid #FCA5A5', padding: '6px 10px',
    borderRadius: 6, fontSize: 11, fontWeight: 500, cursor: 'pointer',
  },
  empty: { padding: 32, textAlign: 'center', color: '#6B6B6B' },
  error: {
    padding: 12, background: '#FEF2F2', color: '#991B1B',
    borderRadius: 6, fontSize: 14, marginBottom: 16,
  },
};
