'use client';

import { useEffect, useState } from 'react';
import {
  adminApi, formatDateTime, type KycQueueItem, type KycDocumentItem, ApiClientError,
} from '@/lib/api';

type StatusFilter = 'all' | 0 | 1 | 2 | 3;

// Backend enum serialization is JsonStringEnumConverter — status arrives as
// "Pending"/"UnderReview"/"Approved"/"Rejected", not 0/1/2/3. Keyed by both
// forms (same defensive pattern as bookings/barber-shops pages) so this
// doesn't silently break again if the wire format changes either way.
const STATUS_LABEL: Record<string, { text: string; color: string }> = {
  '0': { text: 'Bekliyor', color: '#F59E0B' },
  '1': { text: 'İncelemede', color: '#3B82F6' },
  '2': { text: 'Onaylı', color: '#22C55E' },
  '3': { text: 'Reddedildi', color: '#EF4444' },
  Pending: { text: 'Bekliyor', color: '#F59E0B' },
  UnderReview: { text: 'İncelemede', color: '#3B82F6' },
  Approved: { text: 'Onaylı', color: '#22C55E' },
  Rejected: { text: 'Reddedildi', color: '#EF4444' },
};

const STATUS_FILTER_LABEL: Record<string, string> = {
  '0': 'Bekliyor', Pending: 'Bekliyor',
  '1': 'İncelemede', UnderReview: 'İncelemede',
  '2': 'Onaylı', Approved: 'Onaylı',
  '3': 'Reddedildi', Rejected: 'Reddedildi',
  all: 'Tümü',
};

const DOCUMENT_TYPE_LABEL: Record<string, string> = {
  '0': 'Vergi levhası', TaxLicense: 'Vergi levhası',
  '1': 'İşyeri açma ruhsatı', SanitaryPermit: 'İşyeri açma ruhsatı',
  '2': 'MYK sertifikası', MykCertificate: 'MYK sertifikası',
  '3': 'Kimlik', IdentityCard: 'Kimlik',
  '99': 'Diğer', Other: 'Diğer',
};

function isActionable(status: number | string): boolean {
  const s = String(status);
  return s === '0' || s === '1' || s === 'Pending' || s === 'UnderReview';
}

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
type DocsModal = { kind: 'docs'; submission: KycQueueItem };
type ModalState = ApproveModal | RejectModal | DocsModal | null;

const PAGE_SIZE = 20;

export default function KycPage() {
  const [items, setItems] = useState<KycQueueItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<StatusFilter>(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [modal, setModal] = useState<ModalState>(null);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      const result = await adminApi.kycQueue({
        status: statusFilter === 'all' ? undefined : statusFilter,
        page,
        pageSize: PAGE_SIZE,
      });
      setItems(result.items);
      setTotalCount(result.totalCount);
    } catch (err) {
      if (err instanceof ApiClientError) setError(err.message);
      else setError('Sunucuya ulaşılamadı.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { load(); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [statusFilter, page]);

  function selectStatus(next: StatusFilter) {
    setStatusFilter(next);
    setPage(1);
  }

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  return (
    <div>
      <h1 style={styles.h1}>KYC Kuyruğu</h1>
      <p style={styles.subtitle}>
        {totalCount} başvuru · sayfa {page}/{totalPages} · {STATUS_FILTER_LABEL[String(statusFilter)] ?? 'Tümü'}
      </p>

      <div style={styles.filterRow}>
        <FilterChip label="Bekliyor" active={statusFilter === 0} onClick={() => selectStatus(0)} />
        <FilterChip label="İncelemede" active={statusFilter === 1} onClick={() => selectStatus(1)} />
        <FilterChip label="Onaylı" active={statusFilter === 2} onClick={() => selectStatus(2)} />
        <FilterChip label="Reddedildi" active={statusFilter === 3} onClick={() => selectStatus(3)} />
        <FilterChip label="Tümü" active={statusFilter === 'all'} onClick={() => selectStatus('all')} />
      </div>

      {error ? <div style={styles.error}>{error}</div> : null}
      {loading ? <div style={styles.empty}>Yükleniyor…</div> : null}
      {!loading && items.length === 0 && !error ? (
        <div style={styles.empty}>Bu filtre için başvuru yok.</div>
      ) : null}

      <div style={styles.list}>
        {items.map((item) => {
          const status = STATUS_LABEL[String(item.status)] ?? { text: '?', color: '#9B9B9B' };
          const actionable = isActionable(item.status);
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
                <div style={styles.actions}>
                  <button
                    style={styles.docsBtn}
                    onClick={() => setModal({ kind: 'docs', submission: item })}
                  >
                    Belgeler
                  </button>
                  {actionable ? (
                    <>
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
                    </>
                  ) : null}
                </div>
              </div>
            </div>
          );
        })}
      </div>

      <div style={styles.pagination}>
        <button
          onClick={() => setPage((p) => Math.max(1, p - 1))}
          disabled={page <= 1}
          style={styles.pageBtn}
        >
          ← Önceki
        </button>
        <span style={styles.pageInfo}>Sayfa {page} / {totalPages}</span>
        <button
          onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
          disabled={page >= totalPages}
          style={styles.pageBtn}
        >
          Sonraki →
        </button>
      </div>

      {modal?.kind === 'docs' ? (
        <DocumentsModal submission={modal.submission} onClose={() => setModal(null)} />
      ) : null}
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

// Shared by the standalone "Belgeler" view and the Approve/Reject modals — the
// GAP this closes is "admin kör onay": before this, the queue only showed a
// document *count*, never the documents themselves. Preview needs an
// authenticated fetch (Authorization header) since <img src> can't carry one,
// so images are lazy-loaded as blobs and object URLs; PDFs open in a new tab
// the same way.
function KycDocumentsPanel({ submissionId }: { submissionId: string }) {
  const [docs, setDocs] = useState<KycDocumentItem[] | null>(null);
  const [docsError, setDocsError] = useState<string | null>(null);
  const [previews, setPreviews] = useState<Record<string, { url: string; contentType: string }>>({});
  const [previewLoading, setPreviewLoading] = useState<string | null>(null);
  const [previewError, setPreviewError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    adminApi.kycDocuments(submissionId)
      .then((list) => { if (!cancelled) setDocs(list); })
      .catch((err) => {
        if (cancelled) return;
        setDocsError(err instanceof ApiClientError ? err.message : 'Belgeler yüklenemedi.');
      });
    return () => { cancelled = true; };
  }, [submissionId]);

  // Revoke object URLs on unmount so we don't leak blob memory across modal opens.
  useEffect(() => () => {
    Object.values(previews).forEach((p) => URL.revokeObjectURL(p.url));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function view(doc: KycDocumentItem) {
    setPreviewError(null);
    const existing = previews[doc.id];
    if (existing) {
      if (existing.contentType === 'application/pdf') {
        window.open(existing.url, '_blank', 'noopener,noreferrer');
      }
      return;
    }
    setPreviewLoading(doc.id);
    try {
      const { blob, contentType } = await adminApi.kycDocumentContent(doc.id);
      const url = URL.createObjectURL(blob);
      setPreviews((m) => ({ ...m, [doc.id]: { url, contentType } }));
      if (contentType === 'application/pdf') {
        window.open(url, '_blank', 'noopener,noreferrer');
      }
    } catch (err) {
      setPreviewError(err instanceof ApiClientError ? err.message : 'Belge açılamadı.');
    } finally {
      setPreviewLoading(null);
    }
  }

  if (docsError) return <div style={styles.error}>{docsError}</div>;
  if (docs === null) return <div style={styles.hint}>Belgeler yükleniyor…</div>;
  if (docs.length === 0) return <div style={styles.hint}>Bu başvuruya belge eklenmemiş.</div>;

  return (
    <div style={styles.docsPanel}>
      {previewError ? <div style={styles.error}>{previewError}</div> : null}
      {docs.map((doc) => {
        const preview = previews[doc.id];
        const isImage = preview?.contentType.startsWith('image/');
        return (
          <div key={doc.id} style={styles.docRow}>
            <div style={styles.docRowHeader}>
              <div>
                <div style={styles.docType}>
                  {DOCUMENT_TYPE_LABEL[String(doc.documentType)] ?? 'Belge'}
                </div>
                <div style={styles.docMeta}>
                  {doc.fileName} · {(doc.sizeBytes / 1024).toFixed(0)} KB
                </div>
              </div>
              <button
                style={styles.viewBtn}
                onClick={() => view(doc)}
                disabled={previewLoading === doc.id}
              >
                {previewLoading === doc.id
                  ? 'Yükleniyor…'
                  : preview
                    ? (isImage ? 'Yenile' : 'Yeni sekmede aç')
                    : 'Görüntüle'}
              </button>
            </div>
            {isImage ? (
              // eslint-disable-next-line @next/next/no-img-element
              <img src={preview!.url} alt={doc.fileName} style={styles.previewImg} />
            ) : null}
            {preview && !isImage && preview.contentType !== 'application/pdf' ? (
              <a href={preview.url} download={doc.fileName} style={styles.downloadLink}>
                {doc.fileName} dosyasını indir
              </a>
            ) : null}
          </div>
        );
      })}
    </div>
  );
}

function DocumentsModal({ submission, onClose }: { submission: KycQueueItem; onClose: () => void }) {
  return (
    <ModalShell title={`Belgeler — ${submission.barberShopName}`} onClose={onClose}>
      <KycDocumentsPanel submissionId={submission.submissionId} />
      <div style={styles.modalActions}>
        <button style={styles.btnGhost} onClick={onClose}>Kapat</button>
      </div>
    </ModalShell>
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
      <label style={styles.label}>Belgeler</label>
      <KycDocumentsPanel submissionId={submission.submissionId} />
      <label style={{ ...styles.label, marginTop: 16 }}>Sub-merchant anahtarı (PSP)</label>
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
      <label style={styles.label}>Belgeler</label>
      <KycDocumentsPanel submissionId={submission.submissionId} />
      <label style={{ ...styles.label, marginTop: 16 }}>Red sebebi</label>
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
  actions: { display: 'flex', gap: 8, marginTop: 8, flexWrap: 'wrap', justifyContent: 'flex-end' },
  docsBtn: {
    background: '#fff', color: '#475569', border: '1px solid #CBD5E1', padding: '6px 14px',
    borderRadius: 6, fontSize: 13, fontWeight: 600, cursor: 'pointer',
  },
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
  pagination: { display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 16, marginTop: 16 },
  pageBtn: {
    padding: '8px 16px', background: '#fff', border: '1px solid #D4D4D4',
    borderRadius: 6, cursor: 'pointer', fontSize: 13,
  },
  pageInfo: { fontSize: 13, color: '#6B6B6B' },

  // Documents panel
  docsPanel: { display: 'flex', flexDirection: 'column', gap: 10, marginBottom: 8 },
  docRow: { background: '#F8FAFC', border: '1px solid #E2E8F0', borderRadius: 8, padding: 12 },
  docRowHeader: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12 },
  docType: { fontSize: 13, fontWeight: 600, color: '#1A1A1A' },
  docMeta: { fontSize: 11, color: '#6B6B6B', marginTop: 2 },
  viewBtn: {
    background: '#EEF2FF', color: '#4338CA', border: 'none', padding: '6px 12px',
    borderRadius: 6, fontSize: 12, fontWeight: 600, cursor: 'pointer', whiteSpace: 'nowrap',
  },
  previewImg: {
    marginTop: 10, maxWidth: '100%', maxHeight: 260, borderRadius: 6,
    border: '1px solid #E2E8F0', display: 'block',
  },
  downloadLink: {
    display: 'inline-block', marginTop: 10, fontSize: 12, color: '#4338CA', fontWeight: 600,
  },

  // Modal
  backdrop: {
    position: 'fixed', inset: 0, background: 'rgba(15, 23, 42, 0.5)',
    display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000,
  },
  modal: {
    background: '#fff', borderRadius: 14, width: '92%', maxWidth: 520,
    padding: 24, boxShadow: '0 12px 40px rgba(0,0,0,0.18)',
    maxHeight: '88vh', overflowY: 'auto',
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
