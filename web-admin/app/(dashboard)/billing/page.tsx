'use client';

import { useCallback, useEffect, useState } from 'react';
import {
  adminApi,
  ApiClientError,
  formatDateOnly,
  formatTry,
  type AdminSubscriptionInvoiceDto,
  type SubscriptionInvoiceStatusName,
} from '@/lib/api';
import { BillingSettingsCard } from '@/components/billing/BillingSettingsCard';
import {
  AdjustAmountModal,
  ExtendDueModal,
  MarkPaidModal,
} from '@/components/billing/StatementActionModals';

// Backend SubscriptionInvoiceStatus JsonStringEnumConverter ile "Notified" olarak gelir;
// eski istemci/kayıt ihtimaline karşı sayısal anahtarlar da haritada (repo kalıbı).
const STATUS_LABEL: Record<string, { label: string; color: string; bg: string }> = {
  '0': { label: 'Açık', color: '#525252', bg: '#E5E5E5' },
  Open: { label: 'Açık', color: '#525252', bg: '#E5E5E5' },
  '1': { label: 'Bildirildi', color: '#1D4ED8', bg: '#DBEAFE' },
  Notified: { label: 'Bildirildi', color: '#1D4ED8', bg: '#DBEAFE' },
  '2': { label: 'Ödendi', color: '#166534', bg: '#DCFCE7' },
  Paid: { label: 'Ödendi', color: '#166534', bg: '#DCFCE7' },
  '3': { label: 'Gecikmiş', color: '#9A3412', bg: '#FFEDD5' },
  Overdue: { label: 'Gecikmiş', color: '#9A3412', bg: '#FFEDD5' },
  '4': { label: 'Askı uygulandı', color: '#991B1B', bg: '#FEE2E2' },
  SuspensionApplied: { label: 'Askı uygulandı', color: '#991B1B', bg: '#FEE2E2' },
};

const STATUS_OPTIONS: Array<{ value: SubscriptionInvoiceStatusName; label: string }> = [
  { value: 'Open', label: 'Açık' },
  { value: 'Notified', label: 'Bildirildi' },
  { value: 'Paid', label: 'Ödendi' },
  { value: 'Overdue', label: 'Gecikmiş' },
  { value: 'SuspensionApplied', label: 'Askı uygulandı' },
];

const PAGE_SIZE = 25;

// Enum karşılaştırmaları HER ZAMAN String(...) üzerinden — sayı/isim ikiliği bug'ı için.
const statusName = (s: AdminSubscriptionInvoiceDto) => String(s.status);
const isPaid = (s: AdminSubscriptionInvoiceDto) => statusName(s) === '2' || statusName(s) === 'Paid';
/** MarkPaid domain kuralı: yalnız Notified | Overdue | SuspensionApplied. */
const canMarkPaid = (s: AdminSubscriptionInvoiceDto) =>
  ['1', 'Notified', '3', 'Overdue', '4', 'SuspensionApplied'].includes(statusName(s));

type ActionModal =
  | { kind: 'mark-paid' | 'adjust' | 'extend'; invoice: AdminSubscriptionInvoiceDto }
  | null;

type ToastState = { kind: 'success' | 'error'; msg: string } | null;

/** <input type="month"> "2026-08" verir; backend DateOnly ve ayın 1'i ile TAM eşitlik arar. */
function monthInputToPeriodStart(value: string): string | undefined {
  return /^\d{4}-\d{2}$/.test(value) ? `${value}-01` : undefined;
}

export default function BillingPage() {
  const [items, setItems] = useState<AdminSubscriptionInvoiceDto[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [statusFilter, setStatusFilter] = useState<SubscriptionInvoiceStatusName | ''>('');
  const [disputedOnly, setDisputedOnly] = useState(false);
  const [periodMonth, setPeriodMonth] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);
  const [modal, setModal] = useState<ActionModal>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await adminApi.listSubscriptionInvoices({
        status: statusFilter === '' ? undefined : statusFilter,
        disputedOnly: disputedOnly || undefined,
        periodStart: monthInputToPeriodStart(periodMonth),
        page,
        pageSize: PAGE_SIZE,
      });
      setItems(res.items);
      setTotal(res.totalCount);
    } catch (err) {
      if (err instanceof ApiClientError) setError(err.message);
      else setError('Sunucuya ulaşılamadı.');
    } finally {
      setLoading(false);
    }
  }, [statusFilter, disputedOnly, periodMonth, page]);

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    if (!toast) return;
    const t = setTimeout(() => setToast(null), 4000);
    return () => clearTimeout(t);
  }, [toast]);

  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  async function afterAction(msg: string) {
    setModal(null);
    setToast({ kind: 'success', msg });
    await load();
  }

  return (
    <div>
      <h1 style={styles.h1}>Faturalama</h1>
      <p style={styles.subtitle}>Aylık abonelik faturaları ve tahsilat kuyruğu (RFC-014).</p>

      <BillingSettingsCard onToast={(kind, msg) => setToast({ kind, msg })} />

      <h2 style={styles.h2}>Tahsilat kuyruğu</h2>
      <p style={styles.subtitle}>{total} kayıt · sayfa {page}/{totalPages} · vade tarihine göre sıralı</p>

      <div style={styles.filters}>
        <select
          value={statusFilter}
          onChange={(e) => {
            setStatusFilter(e.target.value as SubscriptionInvoiceStatusName | '');
            setPage(1);
          }}
          style={styles.select}
          aria-label="Durum filtresi"
        >
          <option value="">Tüm durumlar</option>
          {STATUS_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>{o.label}</option>
          ))}
        </select>
        <label style={styles.checkboxLabel}>
          <input
            type="checkbox"
            checked={disputedOnly}
            onChange={(e) => { setDisputedOnly(e.target.checked); setPage(1); }}
          />
          Yalnız itirazlılar
        </label>
        <label style={styles.checkboxLabel}>
          Dönem
          <input
            type="month"
            value={periodMonth}
            onChange={(e) => { setPeriodMonth(e.target.value); setPage(1); }}
            style={styles.monthInput}
            aria-label="Dönem filtresi (ay)"
          />
        </label>
        {periodMonth ? (
          <button style={styles.clearBtn} onClick={() => { setPeriodMonth(''); setPage(1); }}>
            Dönem filtresini temizle
          </button>
        ) : null}
      </div>

      {error ? <div style={styles.error}>{error}</div> : null}
      {toast ? (
        <div style={toast.kind === 'success' ? styles.toastSuccess : styles.toastError}>{toast.msg}</div>
      ) : null}

      {loading ? (
        <div style={styles.skeletonWrap}>
          {[0, 1, 2, 3, 4].map((i) => <div key={i} style={styles.skeletonRow} />)}
        </div>
      ) : items.length === 0 ? (
        <div style={styles.empty}>
          <div style={styles.emptyEmoji}>🧾</div>
          <div style={styles.emptyTitle}>Kayıt yok</div>
          <div style={styles.emptyDesc}>
            {statusFilter || disputedOnly || periodMonth
              ? 'Bu filtreyle eşleşen abonelik faturası yok — filtreyi değiştir.'
              : 'Henüz abonelik faturası kapanmamış (aylık kapanış job’u ayın sonunda üretir; ücretsiz planlar için fatura üretilmez).'}
          </div>
        </div>
      ) : (
        <div style={styles.tableWrap}>
          <table style={styles.table}>
            <thead>
              <tr style={styles.thead}>
                <th style={styles.th}>Salon</th>
                <th style={styles.th}>Dönem</th>
                <th style={styles.th}>Plan</th>
                <th style={styles.th}>Plan fiyatı</th>
                <th style={styles.th}>Tutar</th>
                <th style={styles.th}>Vade</th>
                <th style={styles.th}>Durum</th>
                <th style={styles.thRight}>Aksiyon</th>
              </tr>
            </thead>
            <tbody>
              {items.map((s) => {
                const badge = STATUS_LABEL[statusName(s)] ?? { label: statusName(s), color: '#525252', bg: '#E5E5E5' };
                const paid = isPaid(s);
                return (
                  <tr key={s.id} style={styles.tr}>
                    <td style={styles.td}>
                      <div style={styles.shopName}>{s.barberShopName}</div>
                      {s.isDisputed ? (
                        <span style={styles.disputeChip} title="Kuaför bu faturaya itiraz etti">
                          ⚑ İtirazlı
                        </span>
                      ) : null}
                    </td>
                    <td style={styles.td}>
                      {formatDateOnly(s.periodStart)}
                      <div style={styles.subtext}>– {formatDateOnly(s.periodEnd)}</div>
                    </td>
                    <td style={styles.td}>
                      <span style={styles.planCode}>{s.planCode}</span>
                    </td>
                    <td style={styles.td}>
                      {formatTry(s.planPriceMinor)}
                      {s.amountMinor !== s.planPriceMinor ? (
                        <div style={styles.subtext} title="Admin tutarı düzeltmiş">düzeltildi</div>
                      ) : null}
                    </td>
                    <td style={styles.tdAmount}>{formatTry(s.amountMinor)}</td>
                    <td style={styles.td}>{formatDateOnly(s.dueDate)}</td>
                    <td style={styles.td}>
                      <span style={{ ...styles.badge, color: badge.color, background: badge.bg }}>
                        {badge.label}
                      </span>
                    </td>
                    <td style={styles.tdRight}>
                      <div style={styles.actions}>
                        <button
                          style={{ ...styles.btnPaid, opacity: canMarkPaid(s) ? 1 : 0.4 }}
                          disabled={!canMarkPaid(s)}
                          title={canMarkPaid(s) ? 'Havale onayı' : 'Bu durumda ödendi işaretlenemez'}
                          onClick={() => setModal({ kind: 'mark-paid', invoice: s })}
                        >
                          Ödendi işaretle
                        </button>
                        <button
                          style={{ ...styles.btnSecondary, opacity: paid ? 0.4 : 1 }}
                          disabled={paid}
                          title={paid ? 'Ödenmiş fatura tutarı düzeltilemez' : 'Tutar düzelt'}
                          onClick={() => setModal({ kind: 'adjust', invoice: s })}
                        >
                          Tutar düzelt
                        </button>
                        <button
                          style={{ ...styles.btnSecondary, opacity: paid ? 0.4 : 1 }}
                          disabled={paid}
                          title={paid ? 'Ödenmiş fatura vadesi uzatılamaz' : 'Vade uzat'}
                          onClick={() => setModal({ kind: 'extend', invoice: s })}
                        >
                          Vade uzat
                        </button>
                      </div>
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
        <span style={styles.pageInfo}>Sayfa {page} / {totalPages}</span>
        <button
          onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
          disabled={page >= totalPages}
          style={styles.pageBtn}
        >
          Sonraki →
        </button>
      </div>

      {modal?.kind === 'mark-paid' ? (
        <MarkPaidModal
          invoice={modal.invoice}
          onClose={() => setModal(null)}
          onDone={afterAction}
          onError={(msg) => setToast({ kind: 'error', msg })}
        />
      ) : null}
      {modal?.kind === 'adjust' ? (
        <AdjustAmountModal
          invoice={modal.invoice}
          onClose={() => setModal(null)}
          onDone={afterAction}
          onError={(msg) => setToast({ kind: 'error', msg })}
        />
      ) : null}
      {modal?.kind === 'extend' ? (
        <ExtendDueModal
          invoice={modal.invoice}
          onClose={() => setModal(null)}
          onDone={afterAction}
          onError={(msg) => setToast({ kind: 'error', msg })}
        />
      ) : null}
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  h1: { fontSize: 32, fontWeight: 700, marginTop: 0, marginBottom: 4, color: '#1A1A1A' },
  h2: { fontSize: 20, fontWeight: 700, margin: '0 0 4px', color: '#1A1A1A' },
  subtitle: { color: '#6B6B6B', marginTop: 0, marginBottom: 16, fontSize: 13 },
  filters: { display: 'flex', gap: 12, marginBottom: 16, flexWrap: 'wrap', alignItems: 'center' },
  select: {
    padding: '10px 12px', border: '1px solid #E5E5E5', borderRadius: 8, fontSize: 14,
    background: '#fff', cursor: 'pointer', minWidth: 180,
  },
  checkboxLabel: { display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, color: '#475569', cursor: 'pointer' },
  monthInput: {
    padding: '9px 10px', border: '1px solid #E5E5E5', borderRadius: 8, fontSize: 13,
    background: '#fff', fontFamily: 'inherit',
  },
  clearBtn: {
    background: '#F1F5F9', color: '#475569', border: 'none',
    padding: '8px 12px', borderRadius: 8, fontSize: 12, fontWeight: 600, cursor: 'pointer',
  },
  tableWrap: {
    background: '#fff', borderRadius: 12, border: '1px solid #E5E5E5',
    overflow: 'auto', maxWidth: '100%',
  },
  table: { width: '100%', borderCollapse: 'collapse' as const, minWidth: 980 },
  thead: { background: '#F8F8F8' },
  th: {
    padding: '12px 16px', textAlign: 'left' as const, fontSize: 11, fontWeight: 700,
    color: '#6B6B6B', textTransform: 'uppercase' as const, letterSpacing: 0.5,
  },
  thRight: {
    padding: '12px 16px', textAlign: 'right' as const, fontSize: 11, fontWeight: 700,
    color: '#6B6B6B', textTransform: 'uppercase' as const, letterSpacing: 0.5,
  },
  tr: { borderTop: '1px solid #E5E5E5' },
  td: { padding: '14px 16px', fontSize: 13, color: '#1A1A1A', verticalAlign: 'top' },
  tdAmount: { padding: '14px 16px', fontSize: 14, fontWeight: 700, color: '#1A1A1A', verticalAlign: 'top' },
  tdRight: { padding: '14px 16px', fontSize: 13, textAlign: 'right' as const },
  shopName: { fontWeight: 600, fontSize: 14 },
  subtext: { fontSize: 11, color: '#9B9B9B', marginTop: 2 },
  planCode: {
    display: 'inline-block', padding: '3px 8px', background: '#F1F5F9', color: '#334155',
    borderRadius: 6, fontSize: 11, fontWeight: 700, letterSpacing: 0.3,
    fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
  },
  disputeChip: {
    display: 'inline-block', marginTop: 4, padding: '2px 8px', background: '#FEF3C7',
    color: '#92400E', borderRadius: 999, fontSize: 11, fontWeight: 700,
  },
  badge: { padding: '4px 10px', borderRadius: 999, fontSize: 11, fontWeight: 700, whiteSpace: 'nowrap' as const },
  actions: { display: 'flex', gap: 6, justifyContent: 'flex-end', flexWrap: 'wrap' as const },
  btnPaid: {
    background: '#fff', color: '#166534', border: '1px solid #86EFAC',
    padding: '7px 12px', borderRadius: 8, fontSize: 12, fontWeight: 600, cursor: 'pointer',
    whiteSpace: 'nowrap' as const,
  },
  btnSecondary: {
    background: '#fff', color: '#475569', border: '1px solid #E2E8F0',
    padding: '7px 12px', borderRadius: 8, fontSize: 12, fontWeight: 600, cursor: 'pointer',
    whiteSpace: 'nowrap' as const,
  },
  pagination: { display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 16, marginTop: 16 },
  pageBtn: {
    padding: '8px 16px', background: '#fff', border: '1px solid #D4D4D4',
    borderRadius: 6, cursor: 'pointer', fontSize: 13,
  },
  pageInfo: { fontSize: 13, color: '#6B6B6B' },
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
};
