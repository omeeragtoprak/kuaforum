'use client';

import { useEffect, useState } from 'react';
import { adminApi, ApiClientError, type AdminBarberShopDto, formatDateTime } from '@/lib/api';

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

  useEffect(() => {
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
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td style={styles.td} colSpan={8}>Yükleniyor…</td></tr>
            ) : items.length === 0 ? (
              <tr><td style={styles.td} colSpan={8}>Kayıt bulunamadı.</td></tr>
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
};
