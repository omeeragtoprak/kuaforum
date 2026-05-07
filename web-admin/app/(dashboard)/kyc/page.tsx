'use client';

import { useEffect, useState } from 'react';
import { adminApi, formatDateTime, type KycQueueItem, ApiClientError } from '@/lib/api';

type StatusFilter = 'all' | 0 | 1 | 2 | 3;

const STATUS_LABEL: Record<number, { text: string; color: string }> = {
  0: { text: 'Bekliyor', color: '#F59E0B' },
  1: { text: 'İncelemede', color: '#3B82F6' },
  2: { text: 'Onaylı', color: '#22C55E' },
  3: { text: 'Reddedildi', color: '#EF4444' },
};

const REJECTION_REASONS = [
  'Belgeler okunamıyor',
  'Vergi levhası eksik',
  'İşyeri açma ruhsatı eksik',
  'İşletme adı belgeyle eşleşmiyor',
  'Sahtecilik şüphesi',
  'Diğer',
];

type ApproveModal = { kind: 'approve'; submission: KycQueueItem };
type RejectModal = { kind: 'reject'; submission: KycQueueItem };
type ModalState = ApproveModal | RejectModal | null;

export default function KycPage() {
  const [items, setItems] = useState<KycQueueItem[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<StatusFilter>(0);
  const [loading, setLoading] = useState(false);
  const [modal, setModal] = useState<ModalState>(null);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      const list = await adminApi.kycQueue(statusFilter === 'all' ? undefined : statusFilter);
      setItems(list);
    } catch (err) {
      if (err instanceof ApiClientError) setError(err.message);
      else setError('Sunucuya ulaşılamadı.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { load(); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [statusFilter]);

  return (
    <div>
      <h1 style={styles.h1}>KYC Kuyruğu</h1>
      <p style={styles.subtitle}>{items.length} başvuru · {STATUS_LABEL[statusFilter as number]?.text ?? 'Tümü'}</p>

      <div style={styles.filterRow}>
        <FilterChip label="Bekliyor" active={statusFilter === 0} onClick={() => setStatusFilter(0)} />
        <FilterChip label="İncelemede" active={statusFilter === 1} onClick={() => setStatusFilter(1)} />
        <FilterChip label="Onaylı" active={statusFilter === 2} onClick={() => setStatusFilter(2)} />
        <FilterChip label="Reddedildi" active={statusFilter === 3} onClick={() => setStatusFilter(3)} />
        <FilterChip label="Tümü" active={statusFilter === 'all'} onClick={() => setStatusFilter('all')} />
      </div>

      {error ? <div style={styles.error}>{error}</div> : null}
      {loading ? <div style={styles.empty}>Yükleniyor…</div> : null}
      {!loading && items.length === 0 && !error ? (
        <div style={styles.empty}>Bu filtre için başvuru yok.</div>
      ) : null}

      <div style={styles.list}>
        {items.map((item) => {
          const status = STATUS_LABEL[item.status] ?? { text: '?', color: '#9B9B9B' };
          const isActionable = item.status === 0 || item.status === 1;
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
                {isActionable ? (
                  <div style={styles.actions}>
                    <button
                      style={styles.approveBtn}
                      onClick={() => setModal({ kind: 'approve', submission: item })}
                    >
                      Onayla
                    </button>
                    <button
                      style={styles.rejectBtn}
                      onClick={() => setModal({ kind: 'reject', submission: item })}
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

      {modal?.kind === 'approve' ? (
        <ApproveModal
          submission={modal.submission}
          onClose={() => setModal(null)}
          onDone={async () => { setModal(null); await load(); }}
        />
      ) : null}
      {modal?.kind === 'reject' ? (
        <RejectModal
          submission={modal.submission}
          onClose={() => setModal(null)}
          onDone={async () => { setModal(null); await load(); }}
        />
      ) : null}
    </div>
  );
}

function FilterChip({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      style={{
        ...styles.chip,
        background: active ? '#7C3AED' : '#F1F5F9',
        color: active ? '#fff' : '#475569',
        borderColor: active ? '#7C3AED' : '#E2E8F0',
      }}
    >
      {label}
    </button>
  );
}

function ApproveModal({
  submission, onClose, onDone,
}: { submission: KycQueueItem; onClose: () => void; onDone: () => void }) {
  const [subMerchantKey, setSubMerchantKey] = useState(`MANUAL-${submission.submissionId.slice(0, 8)}`);
  const [submitting, setSubmitting] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  async function submit() {
    if (!subMerchantKey.trim()) { setErr('Sub-merchant anahtarı zorunlu.'); return; }
    setSubmitting(true);
    setErr(null);
    try {
      await adminApi.reviewKyc(submission.submissionId, true, undefined, subMerchantKey.trim());
      onDone();
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Hata');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <ModalShell title="KYC Onayla" onClose={onClose}>
      <p style={styles.modalBody}>
        <strong>{submission.barberShopName}</strong> başvurusunu onaylamak üzeresin.
        İşletme aktif olur, müşteri ödemeleri PSP üzerinden bu sub-merchant
        anahtarına yönlendirilir.
      </p>
      <label style={styles.label}>Sub-merchant anahtarı (PSP)</label>
      <input
        style={styles.input}
        value={subMerchantKey}
        onChange={(e) => setSubMerchantKey(e.target.value)}
        placeholder="PAYTR-MERCH-12345"
      />
      <p style={styles.hint}>
        PayTR/Iyzico panelinden alınan sub-merchant ID. Üretimde manuel kopyala.
        Demo için placeholder otomatik dolduruldu.
      </p>
      {err ? <div style={styles.error}>{err}</div> : null}
      <div style={styles.modalActions}>
        <button style={styles.btnGhost} onClick={onClose} disabled={submitting}>Vazgeç</button>
        <button style={styles.btnPrimary} onClick={submit} disabled={submitting}>
          {submitting ? 'Onaylanıyor…' : 'Onayla'}
        </button>
      </div>
    </ModalShell>
  );
}

function RejectModal({
  submission, onClose, onDone,
}: { submission: KycQueueItem; onClose: () => void; onDone: () => void }) {
  const [selectedReason, setSelectedReason] = useState<string | null>(null);
  const [extra, setExtra] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  async function submit() {
    if (!selectedReason) { setErr('Bir red sebebi seç.'); return; }
    const finalReason = extra.trim() ? `${selectedReason} — ${extra.trim()}` : selectedReason;
    setSubmitting(true);
    setErr(null);
    try {
      await adminApi.reviewKyc(submission.submissionId, false, finalReason);
      onDone();
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Hata');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <ModalShell title="KYC Reddet" onClose={onClose}>
      <p style={styles.modalBody}>
        <strong>{submission.barberShopName}</strong> başvurusunu reddediyorsun.
        Sebep işletme sahibine bildirimle iletilir.
      </p>
      <label style={styles.label}>Red sebebi</label>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
        {REJECTION_REASONS.map((reason) => (
          <button
            key={reason}
            onClick={() => setSelectedReason(reason)}
            style={{
              ...styles.chip,
              background: selectedReason === reason ? '#EF4444' : '#F1F5F9',
              color: selectedReason === reason ? '#fff' : '#475569',
              borderColor: selectedReason === reason ? '#EF4444' : '#E2E8F0',
              fontSize: 12,
            }}
          >
            {reason}
          </button>
        ))}
      </div>
      <label style={{ ...styles.label, marginTop: 16 }}>Ek açıklama (opsiyonel)</label>
      <textarea
        style={styles.textarea}
        value={extra}
        onChange={(e) => setExtra(e.target.value.slice(0, 300))}
        placeholder="Daha spesifik bir not ekle…"
        rows={3}
      />
      {err ? <div style={styles.error}>{err}</div> : null}
      <div style={styles.modalActions}>
        <button style={styles.btnGhost} onClick={onClose} disabled={submitting}>Vazgeç</button>
        <button style={styles.btnDanger} onClick={submit} disabled={submitting}>
          {submitting ? 'Reddediliyor…' : 'Reddet'}
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
  h1: { fontSize: 32, fontWeight: 700, marginTop: 0, marginBottom: 4, color: '#1A1A1A' },
  subtitle: { color: '#6B6B6B', marginTop: 0, marginBottom: 16 },
  filterRow: { display: 'flex', gap: 8, marginBottom: 24, flexWrap: 'wrap' },
  chip: {
    padding: '6px 14px', borderRadius: 999, border: '1px solid #E2E8F0',
    fontSize: 13, fontWeight: 600, cursor: 'pointer', whiteSpace: 'nowrap',
  },
  list: { display: 'flex', flexDirection: 'column', gap: 12 },
  row: {
    background: '#fff', padding: 20, borderRadius: 12, border: '1px solid #E5E5E5',
    display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 16,
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
  input: {
    width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: 8,
    fontSize: 14, fontFamily: 'monospace',
  },
  textarea: {
    width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: 8,
    fontSize: 14, resize: 'vertical', fontFamily: 'inherit',
  },
  hint: { fontSize: 11, color: '#94A3B8', marginTop: 6, marginBottom: 16 },
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
