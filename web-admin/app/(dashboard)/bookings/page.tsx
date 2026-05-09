'use client';

import { useCallback, useEffect, useState } from 'react';
import { adminApi, ApiClientError, type AdminBookingDto, formatDateTime, formatTry } from '@/lib/api';

const STATUS_LABEL: Record<string, { label: string; color: string }> = {
  '0': { label: 'Beklemede', color: '#F59E0B' },
  '1': { label: 'Onaylandı', color: '#10B981' },
  '2': { label: 'Devam ediyor', color: '#3B82F6' },
  '3': { label: 'Tamamlandı', color: '#10B981' },
  '4': { label: 'İptal', color: '#6B7280' },
  '5': { label: 'Gelmedi', color: '#EF4444' },
  Pending: { label: 'Beklemede', color: '#F59E0B' },
  Confirmed: { label: 'Onaylandı', color: '#10B981' },
  Started: { label: 'Devam ediyor', color: '#3B82F6' },
  Completed: { label: 'Tamamlandı', color: '#10B981' },
  Cancelled: { label: 'İptal', color: '#6B7280' },
  NoShow: { label: 'Gelmedi', color: '#EF4444' },
};

export default function BookingsPage() {
  const [items, setItems] = useState<AdminBookingDto[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<number | ''>('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [cancelModal, setCancelModal] = useState<AdminBookingDto | null>(null);
  const [successToast, setSuccessToast] = useState<string | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    adminApi
      .listBookings({
        page,
        pageSize: 25,
        search: search.trim() || undefined,
        status: statusFilter === '' ? undefined : statusFilter,
      })
      .then((p) => {
        setItems(p.items);
        setTotal(p.totalCount);
      })
      .catch((err) => {
        if (err instanceof ApiClientError) setError(err.message);
        else setError('Bilinmeyen hata');
      })
      .finally(() => setLoading(false));
  }, [page, search, statusFilter]);

  useEffect(() => { load(); }, [load]);

  // Auto-dismiss success toast
  useEffect(() => {
    if (!successToast) return;
    const t = setTimeout(() => setSuccessToast(null), 4000);
    return () => clearTimeout(t);
  }, [successToast]);

  const totalPages = Math.max(1, Math.ceil(total / 25));

  return (
    <div>
      <h1 style={styles.h1}>Randevular</h1>
      <p style={styles.subtitle}>{total} randevu · sayfa {page}/{totalPages}</p>
      <div style={styles.filters}>
        <div style={styles.searchBox}>
          <span style={styles.searchIcon}>🔍</span>
          <input
            placeholder="İsim, telefon, salon, stilist ara…"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
            style={styles.search}
          />
        </div>
        <select
          value={String(statusFilter)}
          onChange={(e) => {
            setStatusFilter(e.target.value === '' ? '' : Number(e.target.value));
            setPage(1);
          }}
          style={styles.select}
        >
          <option value="">Tüm durumlar</option>
          <option value="0">Beklemede</option>
          <option value="1">Onaylandı</option>
          <option value="2">Devam ediyor</option>
          <option value="3">Tamamlandı</option>
          <option value="4">İptal</option>
          <option value="5">Gelmedi</option>
        </select>
      </div>

      {error ? <div style={styles.error}>{error}</div> : null}
      {successToast ? <div style={styles.toast}>{successToast}</div> : null}

      {loading ? (
        <div style={styles.skeletonWrap}>
          {[0, 1, 2, 3, 4].map((i) => <div key={i} style={styles.skeletonRow} />)}
        </div>
      ) : items.length === 0 ? (
        <div style={styles.empty}>
          <div style={styles.emptyEmoji}>📅</div>
          <div style={styles.emptyTitle}>Eşleşen randevu yok</div>
          <div style={styles.emptyDesc}>
            {search || statusFilter !== ''
              ? 'Farklı bir arama/filtre dene veya filtreleri temizle.'
              : 'Henüz hiç randevu oluşturulmamış.'}
          </div>
        </div>
      ) : (
      <div style={styles.tableWrap}>
        <table style={styles.table}>
          <thead>
            <tr>
              <th style={styles.th}>Tarih</th>
              <th style={styles.th}>Müşteri</th>
              <th style={styles.th}>Salon</th>
              <th style={styles.th}>Stilist</th>
              <th style={styles.th}>Tutar</th>
              <th style={styles.th}>Durum</th>
              <th style={styles.thRight}>Aksiyon</th>
            </tr>
          </thead>
          <tbody>
            {items.map((b) => {
                const s = STATUS_LABEL[String(b.status)] ?? { label: '—', color: '#6B7280' };
                const cancellable = String(b.status) === '0' || String(b.status) === '1' ||
                  String(b.status) === 'Pending' || String(b.status) === 'Confirmed';
                return (
                  <tr key={b.id}>
                    <td style={styles.td}>{formatDateTime(b.scheduledAt)}</td>
                    <td style={styles.td}>
                      <div>{b.customerName ?? '—'}</div>
                      <div style={styles.subtext}>{b.customerPhone ?? ''}</div>
                    </td>
                    <td style={styles.td}>{b.shopName}</td>
                    <td style={styles.td}>{b.stylistName}</td>
                    <td style={styles.td}>{formatTry(b.totalAmountMinor)}</td>
                    <td style={styles.td}>
                      <span style={{ ...styles.badge, background: s.color + '22', color: s.color }}>
                        {s.label}
                      </span>
                    </td>
                    <td style={styles.td}>
                      {cancellable ? (
                        <button
                          style={styles.cancelBtn}
                          onClick={() => setCancelModal(b)}
                        >
                          Admin iptal
                        </button>
                      ) : (
                        <span style={styles.subtext}>—</span>
                      )}
                    </td>
                  </tr>
                );
              })}
          </tbody>
        </table>
      </div>
      )}

      <div style={styles.pagination}>
        <button
          onClick={() => setPage((p) => Math.max(1, p - 1))}
          disabled={page <= 1}
          style={styles.pageBtn}
        >
          ← Önceki
        </button>
        <span style={styles.pageInfo}>
          Sayfa {page} / {totalPages}
        </span>
        <button
          onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
          disabled={page >= totalPages}
          style={styles.pageBtn}
        >
          Sonraki →
        </button>
      </div>

      {cancelModal ? (
        <CancelModal
          booking={cancelModal}
          onClose={() => setCancelModal(null)}
          onDone={(refundPct) => {
            setCancelModal(null);
            setSuccessToast(`Randevu admin tarafından iptal edildi. İade: %${refundPct}`);
            load();
          }}
        />
      ) : null}
    </div>
  );
}

function CancelModal({
  booking, onClose, onDone,
}: { booking: AdminBookingDto; onClose: () => void; onDone: (refundPct: number) => void }) {
  const [reason, setReason] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  async function submit() {
    const trimmed = reason.trim();
    if (trimmed.length < 8) {
      setErr('Sebep en az 8 karakter (audit log için).');
      return;
    }
    setSubmitting(true); setErr(null);
    try {
      const res = await adminApi.cancelBooking(booking.id, trimmed);
      onDone(res.refundPercentage);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Hata');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div style={styles.backdrop} onClick={onClose}>
      <div style={styles.modal} onClick={(e) => e.stopPropagation()}>
        <div style={styles.modalHeader}>
          <h2 style={styles.modalTitle}>Admin iptali</h2>
          <button style={styles.closeX} onClick={onClose}>✕</button>
        </div>
        <p style={styles.modalBody}>
          <strong>{booking.customerName ?? 'Müşteri'} — {booking.shopName}</strong><br />
          {formatDateTime(booking.scheduledAt)} · {formatTry(booking.totalAmountMinor)}
        </p>
        <div style={styles.adminWarn}>
          ⚠️ Admin iptali müşteriye <strong>%100 iade</strong> ile sonuçlanır
          (BarberRequested kategorisi). Sebep audit log'a kalıcı yazılır.
        </div>
        <label style={styles.label}>Sebep (en az 8 karakter)</label>
        <textarea
          value={reason}
          onChange={(e) => setReason(e.target.value.slice(0, 500))}
          rows={3}
          style={styles.textarea}
          placeholder="Örn: İşletme askıya alındı, müşteri şikayeti üzerine, dolandırıcılık şüphesi…"
        />
        {err ? <div style={styles.error}>{err}</div> : null}
        <div style={styles.modalActions}>
          <button style={styles.btnGhost} onClick={onClose} disabled={submitting}>Vazgeç</button>
          <button style={styles.btnDanger} onClick={submit} disabled={submitting}>
            {submitting ? 'İptal ediliyor…' : 'İptal et'}
          </button>
        </div>
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  h1: { fontSize: 32, fontWeight: 700, marginTop: 0, marginBottom: 4, color: '#1A1A1A' },
  subtitle: { color: '#6B6B6B', marginTop: 0, marginBottom: 24 },
  filters: { display: 'flex', gap: 8, alignItems: 'center', marginBottom: 16, flexWrap: 'wrap' },
  searchBox: {
    flex: 1, minWidth: 240, display: 'flex', alignItems: 'center', gap: 8,
    border: '1px solid #E5E5E5', borderRadius: 8, padding: '0 12px', background: '#fff',
  },
  searchIcon: { fontSize: 14, color: '#9B9B9B' },
  search: { flex: 1, padding: '10px 0', border: 'none', fontSize: 14, outline: 'none', background: 'transparent' },
  select: {
    padding: '10px 12px', border: '1px solid #E5E5E5', borderRadius: 8, fontSize: 14,
    background: '#fff', cursor: 'pointer', minWidth: 160,
  },
  input: {
    padding: '8px 12px', border: '1px solid #D4D4D4', borderRadius: 6, fontSize: 14, minWidth: 200,
  },
  totalBadge: { fontSize: 13, color: '#6B6B6B' },
  error: { padding: 12, background: '#FEF2F2', color: '#991B1B', borderRadius: 6, marginBottom: 12 },
  toast: { padding: 12, background: '#F0FDF4', color: '#166534', borderRadius: 6, marginBottom: 12, fontWeight: 500 },
  skeletonWrap: { display: 'flex', flexDirection: 'column', gap: 8 },
  skeletonRow: {
    height: 56, background: 'linear-gradient(90deg, #F1F5F9 0%, #E2E8F0 50%, #F1F5F9 100%)',
    backgroundSize: '200% 100%', borderRadius: 8, animation: 'shimmer 1.6s linear infinite',
  },
  empty: {
    padding: 64, textAlign: 'center' as const, background: '#fff',
    borderRadius: 12, border: '1px solid #E5E5E5',
  },
  emptyEmoji: { fontSize: 48, marginBottom: 8 },
  emptyTitle: { fontSize: 16, fontWeight: 600, color: '#1A1A1A', marginBottom: 4 },
  emptyDesc: { fontSize: 13, color: '#6B6B6B' },
  thRight: {
    textAlign: 'right' as const, padding: '12px 16px', background: '#F5F5F5',
    fontWeight: 600, color: '#3F2E2E', borderBottom: '1px solid #E5E5E5',
    fontSize: 12, textTransform: 'uppercase' as const, letterSpacing: 0.4,
  },
  tableWrap: { background: '#fff', borderRadius: 12, border: '1px solid #E5E5E5', overflow: 'auto' },
  table: { width: '100%', borderCollapse: 'collapse', fontSize: 13 },
  th: {
    textAlign: 'left',
    padding: '12px 16px',
    background: '#F5F5F5',
    fontWeight: 600,
    color: '#3F2E2E',
    borderBottom: '1px solid #E5E5E5',
    fontSize: 12,
    textTransform: 'uppercase',
    letterSpacing: 0.4,
  },
  td: { padding: '12px 16px', borderBottom: '1px solid #F0F0F0', color: '#1A1A1A', verticalAlign: 'top' },
  subtext: { fontSize: 11, color: '#6B6B6B', marginTop: 2 },
  badge: { display: 'inline-block', padding: '4px 10px', borderRadius: 12, fontSize: 12, fontWeight: 600 },
  pagination: { display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 16, marginTop: 16 },
  pageBtn: {
    padding: '8px 16px',
    background: '#fff',
    border: '1px solid #D4D4D4',
    borderRadius: 6,
    cursor: 'pointer',
    fontSize: 13,
  },
  pageInfo: { fontSize: 13, color: '#6B6B6B' },
  cancelBtn: {
    padding: '6px 12px',
    background: '#fff',
    border: '1px solid #FCA5A5',
    color: '#991B1B',
    borderRadius: 6,
    fontSize: 12,
    fontWeight: 600,
    cursor: 'pointer',
  },

  backdrop: {
    position: 'fixed', inset: 0, background: 'rgba(15, 23, 42, 0.5)',
    display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000,
  },
  modal: {
    background: '#fff', borderRadius: 14, width: '92%', maxWidth: 500,
    padding: 24, boxShadow: '0 12px 40px rgba(0,0,0,0.18)',
  },
  modalHeader: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 },
  modalTitle: { fontSize: 20, fontWeight: 700, color: '#1A1A1A', margin: 0 },
  closeX: { background: 'none', border: 'none', fontSize: 18, cursor: 'pointer', color: '#6B6B6B' },
  modalBody: { fontSize: 14, color: '#475569', marginTop: 0, marginBottom: 16, lineHeight: 1.5 },
  adminWarn: {
    background: '#FEF3C7',
    color: '#92400E',
    padding: 12,
    borderRadius: 8,
    fontSize: 13,
    marginBottom: 16,
    lineHeight: 1.5,
  },
  label: {
    fontSize: 12, fontWeight: 600, color: '#475569', textTransform: 'uppercase',
    letterSpacing: 0.5, display: 'block', marginBottom: 6,
  },
  textarea: {
    width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: 8,
    fontSize: 14, resize: 'vertical', fontFamily: 'inherit',
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
