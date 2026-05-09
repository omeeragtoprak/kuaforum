'use client';

import { useCallback, useEffect, useState } from 'react';
import { adminApi, formatDateTime, type PendingReview, ApiClientError } from '@/lib/api';

const REJECT_REASONS = [
  { value: 0, label: 'Küfür / Hakaret', emoji: '🤬' },
  { value: 1, label: 'Spam', emoji: '🗑️' },
  { value: 2, label: 'Konu dışı', emoji: '🤷' },
  { value: 3, label: 'Kişisel saldırı', emoji: '⚠️' },
  { value: 99, label: 'Diğer', emoji: '❓' },
];

export default function ReviewsPage() {
  const [items, setItems] = useState<PendingReview[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [toast, setToast] = useState<{ kind: 'success' | 'error'; msg: string } | null>(null);
  const [rejectModal, setRejectModal] = useState<PendingReview | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const list = await adminApi.pendingReviews();
      setItems(list);
    } catch (err) {
      if (err instanceof ApiClientError) setError(err.message);
      else setError('Sunucuya ulaşılamadı.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    if (!toast) return;
    const t = setTimeout(() => setToast(null), 4000);
    return () => clearTimeout(t);
  }, [toast]);

  async function approve(r: PendingReview) {
    setBusy(r.id);
    try {
      await adminApi.approveReview(r.id);
      setToast({ kind: 'success', msg: `${r.customerName ?? 'Müşteri'} yorumu onaylandı.` });
      await load();
    } catch (err) {
      setToast({ kind: 'error', msg: err instanceof Error ? err.message : 'Hata' });
    } finally {
      setBusy(null);
    }
  }

  return (
    <div>
      <h1 style={styles.h1}>Yorum Moderasyonu</h1>
      <p style={styles.subtitle}>{items.length} bekleyen yorum</p>

      {error ? <div style={styles.error}>{error}</div> : null}
      {toast ? (
        <div style={toast.kind === 'success' ? styles.toastSuccess : styles.toastError}>{toast.msg}</div>
      ) : null}
      {loading ? <div style={styles.empty}>Yükleniyor…</div> : null}
      {!loading && items.length === 0 && !error ? (
        <div style={styles.empty}>🎉 Bekleyen yorum yok.</div>
      ) : null}

      <div style={styles.list}>
        {items.map((r) => (
          <div key={r.id} style={styles.row}>
            <div style={styles.rowHead}>
              <div>
                <div style={styles.context}>
                  <strong>{r.customerName ?? 'Müşteri'}</strong>
                  {' → '}
                  <strong>{r.barberShopName ?? 'Salon'}</strong>
                  {r.stylistName ? <span style={styles.subtext}> · {r.stylistName}</span> : null}
                </div>
                <div style={styles.date}>{formatDateTime(r.createdAt)}</div>
              </div>
              <div style={styles.starsBox}>
                <div style={styles.stars}>{'★'.repeat(r.rating)}{'☆'.repeat(5 - r.rating)}</div>
                <div style={styles.ratingValue}>{r.rating}/5</div>
              </div>
            </div>
            <div style={styles.body}>{r.body || <em style={styles.italic}>(metin yok — sadece yıldız)</em>}</div>
            <div style={styles.actions}>
              <button
                style={styles.approveBtn}
                onClick={() => approve(r)}
                disabled={busy === r.id}
              >
                ✅ Onayla
              </button>
              <button
                style={styles.rejectBtn}
                onClick={() => setRejectModal(r)}
                disabled={busy === r.id}
              >
                ❌ Reddet
              </button>
            </div>
          </div>
        ))}
      </div>

      {rejectModal ? (
        <RejectModal
          review={rejectModal}
          onClose={() => setRejectModal(null)}
          onDone={async (label) => {
            setRejectModal(null);
            setToast({ kind: 'success', msg: `Yorum reddedildi: ${label}` });
            await load();
          }}
          onError={(msg) => setToast({ kind: 'error', msg })}
        />
      ) : null}
    </div>
  );
}

function RejectModal({
  review, onClose, onDone, onError,
}: {
  review: PendingReview;
  onClose: () => void;
  onDone: (label: string) => void;
  onError: (msg: string) => void;
}) {
  const [reasonValue, setReasonValue] = useState<number | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function submit() {
    if (reasonValue === null) return;
    setSubmitting(true);
    try {
      await adminApi.rejectReview(review.id, reasonValue);
      const reason = REJECT_REASONS.find((r) => r.value === reasonValue);
      onDone(reason?.label ?? '—');
    } catch (err) {
      onError(err instanceof Error ? err.message : 'Hata');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div style={styles.backdrop} onClick={onClose}>
      <div style={styles.modal} onClick={(e) => e.stopPropagation()}>
        <div style={styles.modalHeader}>
          <h2 style={styles.modalTitle}>Yorum reddi</h2>
          <button style={styles.closeX} onClick={onClose}>✕</button>
        </div>
        <p style={styles.modalBody}>
          <strong>{review.customerName ?? 'Müşteri'}</strong> → <strong>{review.barberShopName ?? 'Salon'}</strong>
          <br />
          {review.rating}/5 · {review.body ? `"${review.body.slice(0, 100)}${review.body.length > 100 ? '…' : ''}"` : '(metin yok)'}
        </p>
        <label style={styles.label}>Red sebebi</label>
        <div style={styles.reasonGrid}>
          {REJECT_REASONS.map((r) => (
            <button
              key={r.value}
              onClick={() => setReasonValue(r.value)}
              style={{
                ...styles.reasonChip,
                background: reasonValue === r.value ? '#EF4444' : '#F1F5F9',
                color: reasonValue === r.value ? '#fff' : '#475569',
                borderColor: reasonValue === r.value ? '#EF4444' : '#E2E8F0',
              }}
            >
              <span style={{ marginRight: 6 }}>{r.emoji}</span>{r.label}
            </button>
          ))}
        </div>
        <div style={styles.modalActions}>
          <button style={styles.btnGhost} onClick={onClose} disabled={submitting}>Vazgeç</button>
          <button
            style={{ ...styles.btnDanger, opacity: reasonValue === null ? 0.5 : 1 }}
            onClick={submit}
            disabled={submitting || reasonValue === null}
          >
            {submitting ? 'Reddediliyor…' : 'Reddet'}
          </button>
        </div>
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
    display: 'flex', flexDirection: 'column', gap: 12,
  },
  rowHead: { display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 12 },
  context: { fontSize: 14, color: '#1A1A1A', marginBottom: 4 },
  subtext: { color: '#6B6B6B', fontWeight: 'normal' as const },
  date: { fontSize: 12, color: '#9B9B9B' },
  starsBox: { textAlign: 'right' },
  stars: { fontSize: 18, color: '#F59E0B', letterSpacing: 2 },
  ratingValue: { fontSize: 11, color: '#9B9B9B', marginTop: 2 },
  body: {
    fontSize: 14, color: '#1A1A1A', lineHeight: 1.5,
    background: '#F8FAFC', padding: 12, borderRadius: 8, borderLeft: '3px solid #CBD5E1',
  },
  italic: { color: '#9B9B9B', fontStyle: 'italic' as const },
  actions: { display: 'flex', gap: 8 },
  approveBtn: {
    background: '#22C55E', color: '#fff', border: 'none', padding: '8px 18px',
    borderRadius: 8, fontSize: 13, fontWeight: 600, cursor: 'pointer',
  },
  rejectBtn: {
    background: '#fff', color: '#991B1B', border: '1px solid #FCA5A5',
    padding: '8px 18px', borderRadius: 8, fontSize: 13, fontWeight: 600, cursor: 'pointer',
  },
  empty: { padding: 32, textAlign: 'center', color: '#6B6B6B' },
  error: {
    padding: 12, background: '#FEF2F2', color: '#991B1B',
    borderRadius: 6, fontSize: 14, marginBottom: 16,
  },
  toastSuccess: {
    padding: 12, background: '#F0FDF4', color: '#166534',
    borderRadius: 6, fontSize: 14, marginBottom: 16, fontWeight: 500,
  },
  toastError: {
    padding: 12, background: '#FEF2F2', color: '#991B1B',
    borderRadius: 6, fontSize: 14, marginBottom: 16, fontWeight: 500,
  },

  // Modal
  backdrop: {
    position: 'fixed', inset: 0, background: 'rgba(15, 23, 42, 0.5)',
    display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000,
  },
  modal: {
    background: '#fff', borderRadius: 14, width: '92%', maxWidth: 520,
    padding: 24, boxShadow: '0 12px 40px rgba(0,0,0,0.18)',
  },
  modalHeader: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 },
  modalTitle: { fontSize: 20, fontWeight: 700, color: '#1A1A1A', margin: 0 },
  closeX: { background: 'none', border: 'none', fontSize: 18, cursor: 'pointer', color: '#6B6B6B' },
  modalBody: { fontSize: 14, color: '#475569', marginTop: 0, marginBottom: 16, lineHeight: 1.5 },
  label: {
    fontSize: 12, fontWeight: 600, color: '#475569', textTransform: 'uppercase',
    letterSpacing: 0.5, display: 'block', marginBottom: 8,
  },
  reasonGrid: { display: 'flex', flexWrap: 'wrap', gap: 8 },
  reasonChip: {
    padding: '8px 14px', borderRadius: 999, border: '1px solid #E2E8F0',
    fontSize: 13, fontWeight: 600, cursor: 'pointer', whiteSpace: 'nowrap',
  },
  modalActions: { display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 20 },
  btnGhost: {
    background: '#F1F5F9', color: '#475569', border: 'none',
    padding: '8px 18px', borderRadius: 8, fontSize: 14, fontWeight: 600, cursor: 'pointer',
  },
  btnDanger: {
    background: '#EF4444', color: '#fff', border: 'none',
    padding: '8px 18px', borderRadius: 8, fontSize: 14, fontWeight: 600, cursor: 'pointer',
  },
};
