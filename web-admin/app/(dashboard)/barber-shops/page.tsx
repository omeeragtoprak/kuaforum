'use client';

import { useCallback, useEffect, useState } from 'react';
import { adminApi, ApiClientError, type AdminBarberShopDto, formatDateTime } from '@/lib/api';

type ActionModal =
  | { kind: 'suspend'; shop: AdminBarberShopDto }
  | { kind: 'reactivate'; shop: AdminBarberShopDto }
  | null;

const STATUS_LABEL: Record<string, { label: string; color: string }> = {
  '0': { label: 'Hazırlık', color: '#6B7280' },
  '1': { label: 'Aktif', color: '#10B981' },
  '2': { label: 'Askıda', color: '#F59E0B' },
  '3': { label: 'Kapalı', color: '#EF4444' },
  Draft: { label: 'Hazırlık', color: '#6B7280' },
  Active: { label: 'Aktif', color: '#10B981' },
  Suspended: { label: 'Askıda', color: '#F59E0B' },
  Closed: { label: 'Kapalı', color: '#EF4444' },
};

const GENDER_LABEL: Record<string, string> = {
  '0': 'Unisex',
  '1': 'Sadece kadın',
  '2': 'Sadece erkek',
  Unisex: 'Unisex',
  FemaleOnly: 'Sadece kadın',
  MaleOnly: 'Sadece erkek',
};

export default function BarberShopsPage() {
  const [items, setItems] = useState<AdminBarberShopDto[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<number | ''>('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [actionModal, setActionModal] = useState<ActionModal>(null);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    adminApi
      .listBarberShops({
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

  const totalPages = Math.max(1, Math.ceil(total / 25));

  return (
    <div>
      <h1 style={styles.h1}>Kuaförler</h1>
      <div style={styles.filters}>
        <input
          placeholder="Salon adı veya slug…"
          value={search}
          onChange={(e) => {
            setSearch(e.target.value);
            setPage(1);
          }}
          style={styles.input}
        />
        <select
          value={String(statusFilter)}
          onChange={(e) => {
            setStatusFilter(e.target.value === '' ? '' : Number(e.target.value));
            setPage(1);
          }}
          style={styles.input}
        >
          <option value="">Tüm durumlar</option>
          <option value="0">Hazırlık</option>
          <option value="1">Aktif</option>
          <option value="2">Askıda</option>
          <option value="3">Kapalı</option>
        </select>
        <span style={styles.totalBadge}>Toplam: {total}</span>
      </div>

      {error ? <div style={styles.error}>{error}</div> : null}

      <div style={styles.tableWrap}>
        <table style={styles.table}>
          <thead>
            <tr>
              <th style={styles.th}>Salon</th>
              <th style={styles.th}>Sahip</th>
              <th style={styles.th}>Tip</th>
              <th style={styles.th}>Stilist</th>
              <th style={styles.th}>Hizmet</th>
              <th style={styles.th}>Tamamlanmış randevu</th>
              <th style={styles.th}>Durum</th>
              <th style={styles.th}>Oluşturulma</th>
              <th style={styles.th}>Aksiyon</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td style={styles.td} colSpan={9}>Yükleniyor…</td></tr>
            ) : items.length === 0 ? (
              <tr><td style={styles.td} colSpan={9}>Kayıt bulunamadı.</td></tr>
            ) : (
              items.map((s) => {
                const status = STATUS_LABEL[String(s.status)] ?? { label: '—', color: '#6B7280' };
                return (
                  <tr key={s.id}>
                    <td style={styles.td}>
                      <div style={{ fontWeight: 600 }}>{s.name}</div>
                      <div style={styles.subtext}>{s.slug}</div>
                    </td>
                    <td style={styles.td}>
                      <div>{s.ownerDisplayName ?? '—'}</div>
                      <div style={styles.subtext}>{s.ownerPhone ?? ''}</div>
                    </td>
                    <td style={styles.td}>{GENDER_LABEL[String(s.genderType)] ?? '—'}</td>
                    <td style={styles.td}>{s.stylistCount}</td>
                    <td style={styles.td}>{s.serviceCount}</td>
                    <td style={styles.td}>{s.completedBookingsCount}</td>
                    <td style={styles.td}>
                      <span style={{ ...styles.badge, background: status.color + '22', color: status.color }}>
                        {status.label}
                      </span>
                    </td>
                    <td style={styles.td}>{formatDateTime(s.createdAt)}</td>
                    <td style={styles.td}>
                      {String(s.status) === '1' || String(s.status) === 'Active' ? (
                        <button
                          style={styles.suspendBtn}
                          onClick={() => setActionModal({ kind: 'suspend', shop: s })}
                        >
                          Askıya al
                        </button>
                      ) : String(s.status) === '2' || String(s.status) === 'Suspended' ? (
                        <button
                          style={styles.reactivateBtn}
                          onClick={() => setActionModal({ kind: 'reactivate', shop: s })}
                        >
                          Aktif et
                        </button>
                      ) : (
                        <span style={styles.subtext}>—</span>
                      )}
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>

      <div style={styles.pagination}>
        <button onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page <= 1} style={styles.pageBtn}>
          ← Önceki
        </button>
        <span style={styles.pageInfo}>Sayfa {page} / {totalPages}</span>
        <button onClick={() => setPage((p) => Math.min(totalPages, p + 1))} disabled={page >= totalPages} style={styles.pageBtn}>
          Sonraki →
        </button>
      </div>

      {actionModal?.kind === 'suspend' ? (
        <SuspendModal
          shop={actionModal.shop}
          onClose={() => setActionModal(null)}
          onDone={() => { setActionModal(null); load(); }}
        />
      ) : null}
      {actionModal?.kind === 'reactivate' ? (
        <ReactivateModal
          shop={actionModal.shop}
          onClose={() => setActionModal(null)}
          onDone={() => { setActionModal(null); load(); }}
        />
      ) : null}
    </div>
  );
}

function SuspendModal({
  shop, onClose, onDone,
}: { shop: AdminBarberShopDto; onClose: () => void; onDone: () => void }) {
  const [reason, setReason] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  async function submit() {
    const trimmed = reason.trim();
    if (trimmed.length < 8) {
      setErr('Sebep en az 8 karakter olmalı (audit log için).');
      return;
    }
    setSubmitting(true); setErr(null);
    try {
      await adminApi.suspendBarberShop(shop.id, trimmed);
      onDone();
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Hata');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <ModalShell title="İşletmeyi askıya al" onClose={onClose}>
      <p style={styles.modalBody}>
        <strong>{shop.name}</strong> askıya alınacak. Müşterilere search/keşfette
        görünmez, mevcut bookinglar etkilenmez. Sebep audit log'a kalıcı yazılır.
      </p>
      <label style={styles.label}>Sebep (en az 8 karakter)</label>
      <textarea
        value={reason}
        onChange={(e) => setReason(e.target.value.slice(0, 500))}
        rows={3}
        style={styles.textarea}
        placeholder="Örn: Müşteri şikayet sayısı eşik üstünde, KVKK ihlali, dolandırıcılık şüphesi…"
      />
      {err ? <div style={styles.error}>{err}</div> : null}
      <div style={styles.modalActions}>
        <button style={styles.btnGhost} onClick={onClose} disabled={submitting}>Vazgeç</button>
        <button style={styles.btnDanger} onClick={submit} disabled={submitting}>
          {submitting ? 'İşleniyor…' : 'Askıya al'}
        </button>
      </div>
    </ModalShell>
  );
}

function ReactivateModal({
  shop, onClose, onDone,
}: { shop: AdminBarberShopDto; onClose: () => void; onDone: () => void }) {
  const [submitting, setSubmitting] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  async function submit() {
    setSubmitting(true); setErr(null);
    try {
      await adminApi.reactivateBarberShop(shop.id);
      onDone();
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Hata');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <ModalShell title="İşletmeyi tekrar aktif et" onClose={onClose}>
      <p style={styles.modalBody}>
        <strong>{shop.name}</strong> tekrar Active duruma alınacak. Search/keşfette
        görünmeye başlar, randevu alınabilir hale gelir.
      </p>
      {err ? <div style={styles.error}>{err}</div> : null}
      <div style={styles.modalActions}>
        <button style={styles.btnGhost} onClick={onClose} disabled={submitting}>Vazgeç</button>
        <button style={styles.btnPrimary} onClick={submit} disabled={submitting}>
          {submitting ? 'İşleniyor…' : 'Aktif et'}
        </button>
      </div>
    </ModalShell>
  );
}

function ModalShell({
  title, children, onClose,
}: { title: string; children: React.ReactNode; onClose: () => void }) {
  return (
    <div style={styles.backdrop} onClick={onClose}>
      <div style={styles.modal} onClick={(e) => e.stopPropagation()}>
        <div style={styles.modalHeader}>
          <h2 style={styles.modalTitle}>{title}</h2>
          <button style={styles.closeX} onClick={onClose}>✕</button>
        </div>
        {children}
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  h1: { fontSize: 28, fontWeight: 700, marginTop: 0, marginBottom: 16, color: '#1A1A1A' },
  filters: { display: 'flex', gap: 12, alignItems: 'center', marginBottom: 16, flexWrap: 'wrap' },
  input: { padding: '8px 12px', border: '1px solid #D4D4D4', borderRadius: 6, fontSize: 14, minWidth: 200 },
  totalBadge: { fontSize: 13, color: '#6B6B6B' },
  error: { padding: 12, background: '#FEF2F2', color: '#991B1B', borderRadius: 6, marginBottom: 12 },
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
  pageBtn: { padding: '8px 16px', background: '#fff', border: '1px solid #D4D4D4', borderRadius: 6, cursor: 'pointer', fontSize: 13 },
  pageInfo: { fontSize: 13, color: '#6B6B6B' },

  suspendBtn: {
    background: '#FEF2F2', color: '#991B1B', border: '1px solid #FCA5A5',
    padding: '4px 12px', borderRadius: 6, fontSize: 12, fontWeight: 600, cursor: 'pointer',
  },
  reactivateBtn: {
    background: '#F0FDF4', color: '#166534', border: '1px solid #86EFAC',
    padding: '4px 12px', borderRadius: 6, fontSize: 12, fontWeight: 600, cursor: 'pointer',
  },

  // Modal
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
  btnPrimary: {
    background: '#22C55E', color: '#fff', border: 'none',
    padding: '8px 18px', borderRadius: 8, fontSize: 14, fontWeight: 600, cursor: 'pointer',
  },
  btnDanger: {
    background: '#EF4444', color: '#fff', border: 'none',
    padding: '8px 18px', borderRadius: 8, fontSize: 14, fontWeight: 600, cursor: 'pointer',
  },
};
