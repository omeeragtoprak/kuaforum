'use client';

import { useEffect, useState } from 'react';
import { adminApi, formatDateTime, type AdminUser, ApiClientError } from '@/lib/api';

const ROLE_LABEL: Record<number, string> = {
  0: 'Customer',
  1: 'BarberOwner',
  2: 'Stylist',
  99: 'Admin',
};
const STATUS_LABEL: Record<number, { text: string; color: string }> = {
  0: { text: 'Active', color: '#22C55E' },
  1: { text: 'Suspended', color: '#F59E0B' },
  2: { text: 'Deleted', color: '#9B9B9B' },
};

export default function UsersPage() {
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [total, setTotal] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [roleFilter, setRoleFilter] = useState<number | undefined>();
  const [statusFilter, setStatusFilter] = useState<number | undefined>();
  const [busy, setBusy] = useState<string | null>(null);

  async function load() {
    try {
      const res = await adminApi.listUsers({ search: search || undefined, role: roleFilter, status: statusFilter });
      setUsers(res.items);
      setTotal(res.totalCount);
    } catch (err) {
      if (err instanceof ApiClientError) setError(err.message);
    }
  }

  useEffect(() => { load(); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, []);

  async function suspend(u: AdminUser) {
    const reason = prompt(`${u.displayName ?? u.email ?? u.phoneE164} kullanıcısı için askıya alma sebebi:`);
    if (!reason) return;
    setBusy(u.id);
    try {
      await adminApi.suspendUser(u.id, reason);
      await load();
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Hata');
    } finally {
      setBusy(null);
    }
  }

  async function unsuspend(u: AdminUser) {
    if (!confirm(`${u.displayName ?? u.email ?? u.phoneE164} askıdan kaldırılsın mı?`)) return;
    setBusy(u.id);
    try {
      await adminApi.unsuspendUser(u.id);
      await load();
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Hata');
    } finally {
      setBusy(null);
    }
  }

  return (
    <div>
      <h1 style={styles.h1}>Kullanıcılar</h1>
      <p style={styles.subtitle}>{total} kullanıcı</p>

      <div style={styles.filters}>
        <input
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Telefon, e-posta, isim..."
          style={styles.search}
        />
        <select
          value={roleFilter ?? ''}
          onChange={(e) => setRoleFilter(e.target.value ? Number(e.target.value) : undefined)}
          style={styles.select}
        >
          <option value="">Tüm roller</option>
          <option value="0">Customer</option>
          <option value="1">BarberOwner</option>
          <option value="2">Stylist</option>
          <option value="99">Admin</option>
        </select>
        <select
          value={statusFilter ?? ''}
          onChange={(e) => setStatusFilter(e.target.value ? Number(e.target.value) : undefined)}
          style={styles.select}
        >
          <option value="">Tüm durumlar</option>
          <option value="0">Active</option>
          <option value="1">Suspended</option>
          <option value="2">Deleted</option>
        </select>
        <button onClick={load} style={styles.searchBtn}>Filtrele</button>
      </div>

      {error ? <div style={styles.error}>{error}</div> : null}

      <div style={styles.tableWrap}>
        <table style={styles.table}>
          <thead>
            <tr style={styles.thead}>
              <th style={styles.th}>İsim</th>
              <th style={styles.th}>İletişim</th>
              <th style={styles.th}>Rol</th>
              <th style={styles.th}>Durum</th>
              <th style={styles.th}>Kayıt</th>
              <th style={styles.th}>Son giriş</th>
              <th style={styles.th}>Eylem</th>
            </tr>
          </thead>
          <tbody>
            {users.map((u) => {
              const status = STATUS_LABEL[u.status] ?? { text: '?', color: '#9B9B9B' };
              return (
                <tr key={u.id} style={styles.tr}>
                  <td style={styles.td}>{u.displayName ?? '—'}</td>
                  <td style={styles.td}>
                    <div style={styles.contact}>{u.phoneE164 ?? '—'}</div>
                    <div style={styles.contactSmall}>{u.email ?? ''}</div>
                  </td>
                  <td style={styles.td}>{ROLE_LABEL[u.role]}</td>
                  <td style={styles.td}>
                    <span style={{ ...styles.badge, background: status.color }}>{status.text}</span>
                  </td>
                  <td style={styles.td}>{formatDateTime(u.createdAt)}</td>
                  <td style={styles.td}>{u.lastLoginAt ? formatDateTime(u.lastLoginAt) : '—'}</td>
                  <td style={styles.td}>
                    {u.status === 0 ? (
                      <button style={styles.suspendBtn} onClick={() => suspend(u)} disabled={busy === u.id}>
                        Askıya al
                      </button>
                    ) : u.status === 1 ? (
                      <button style={styles.unsuspendBtn} onClick={() => unsuspend(u)} disabled={busy === u.id}>
                        Aç
                      </button>
                    ) : null}
                  </td>
                </tr>
              );
            })}
            {users.length === 0 ? (
              <tr><td colSpan={7} style={styles.empty}>Eşleşen kullanıcı yok.</td></tr>
            ) : null}
          </tbody>
        </table>
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  h1: { fontSize: 32, fontWeight: 700, marginTop: 0, marginBottom: 4, color: '#1A1A1A' },
  subtitle: { color: '#6B6B6B', marginTop: 0, marginBottom: 24 },
  filters: { display: 'flex', gap: 8, marginBottom: 16, flexWrap: 'wrap' },
  search: {
    flex: 1, minWidth: 200, padding: '8px 12px', border: '1px solid #E5E5E5',
    borderRadius: 6, fontSize: 14, outline: 'none',
  },
  select: { padding: '8px 12px', border: '1px solid #E5E5E5', borderRadius: 6, fontSize: 14 },
  searchBtn: {
    padding: '8px 16px', background: '#6B4F4F', color: '#fff', border: 'none',
    borderRadius: 6, fontSize: 13, fontWeight: 600, cursor: 'pointer',
  },
  tableWrap: { background: '#fff', borderRadius: 12, border: '1px solid #E5E5E5', overflow: 'hidden' },
  table: { width: '100%', borderCollapse: 'collapse' as const },
  thead: { background: '#F8F8F8' },
  th: { padding: '12px 16px', textAlign: 'left' as const, fontSize: 12, fontWeight: 600, color: '#6B6B6B' },
  tr: { borderTop: '1px solid #E5E5E5' },
  td: { padding: '12px 16px', fontSize: 13, color: '#1A1A1A' },
  contact: { fontSize: 13 },
  contactSmall: { fontSize: 11, color: '#9B9B9B' },
  badge: { color: '#fff', padding: '3px 10px', borderRadius: 999, fontSize: 11, fontWeight: 600 },
  suspendBtn: {
    background: '#F59E0B', color: '#fff', border: 'none', padding: '6px 12px',
    borderRadius: 6, fontSize: 12, fontWeight: 600, cursor: 'pointer',
  },
  unsuspendBtn: {
    background: '#22C55E', color: '#fff', border: 'none', padding: '6px 12px',
    borderRadius: 6, fontSize: 12, fontWeight: 600, cursor: 'pointer',
  },
  empty: { padding: 32, textAlign: 'center' as const, color: '#6B6B6B' },
  error: {
    padding: 12, background: '#FEF2F2', color: '#991B1B',
    borderRadius: 6, fontSize: 14, marginBottom: 16,
  },
};
