'use client';

import { useEffect, useState } from 'react';
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

  useEffect(() => {
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

  const totalPages = Math.max(1, Math.ceil(total / 25));

  return (
    <div>
      <h1 style={styles.h1}>Randevular</h1>
      <div style={styles.filters}>
        <input
          placeholder="İsim, telefon, salon, stilist…"
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
          <option value="0">Beklemede</option>
          <option value="1">Onaylandı</option>
          <option value="2">Devam ediyor</option>
          <option value="3">Tamamlandı</option>
          <option value="4">İptal</option>
          <option value="5">Gelmedi</option>
        </select>
        <span style={styles.totalBadge}>Toplam: {total}</span>
      </div>

      {error ? <div style={styles.error}>{error}</div> : null}

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
              <th style={styles.th}>Aksiyon</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td style={styles.td} colSpan={7}>Yükleniyor…</td>
              </tr>
            ) : items.length === 0 ? (
              <tr>
                <td style={styles.td} colSpan={7}>Kayıt bulunamadı.</td>
              </tr>
            ) : (
              items.map((b) => {
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
                          onClick={async () => {
                            const reason = window.prompt('İptal sebebi (audit için):');
                            if (!reason) return;
                            try {
                              const res = await adminApi.cancelBooking(b.id, reason);
                              alert(`İptal edildi. İade %${res.refundPercentage}`);
                              setPage((p) => p);
                            } catch (err) {
                              alert(err instanceof ApiClientError ? err.message : 'Hata');
                            }
                          }}
                        >
                          İptal et
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
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  h1: { fontSize: 28, fontWeight: 700, marginTop: 0, marginBottom: 16, color: '#1A1A1A' },
  filters: { display: 'flex', gap: 12, alignItems: 'center', marginBottom: 16, flexWrap: 'wrap' },
  input: {
    padding: '8px 12px',
    border: '1px solid #D4D4D4',
    borderRadius: 6,
    fontSize: 14,
    minWidth: 200,
  },
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
    fontWeight: 500,
    cursor: 'pointer',
  },
};
