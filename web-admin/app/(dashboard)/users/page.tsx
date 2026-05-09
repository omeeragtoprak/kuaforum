'use client';

import { useCallback, useEffect, useState } from 'react';
import { adminApi, formatDateTime, type AdminUser, ApiClientError } from '@/lib/api';

const ROLE_LABEL: Record<number, string> = {
  0: 'Customer',
  1: 'BarberOwner',
  2: 'Stylist',
  99: 'Admin',
};
const STATUS_LABEL: Record<number, { text: string; color: string; bg: string }> = {
  0: { text: 'Aktif', color: '#166534', bg: '#DCFCE7' },
  1: { text: 'Askıda', color: '#92400E', bg: '#FEF3C7' },
  2: { text: 'Silinmiş', color: '#525252', bg: '#E5E5E5' },
};

type ToastState = { kind: 'success' | 'error'; msg: string } | null;

export default function UsersPage() {
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [total, setTotal] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [roleFilter, setRoleFilter] = useState<number | undefined>();
  const [statusFilter, setStatusFilter] = useState<number | undefined>();
  const [toast, setToast] = useState<ToastState>(null);
  const [suspendModal, setSuspendModal] = useState<AdminUser | null>(null);
  const [unsuspendModal, setUnsuspendModal] = useState<AdminUser | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await adminApi.listUsers({
        search: search || undefined,
        role: roleFilter,
        status: statusFilter,
      });
      setUsers(res.items);
      setTotal(res.totalCount);
    } catch (err) {
      if (err instanceof ApiClientError) setError(err.message);
      else setError('Sunucuya ulaşılamadı.');
    } finally {
      setLoading(false);
    }
  }, [search, roleFilter, statusFilter]);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    if (!toast) return;
    const t = setTimeout(() => setToast(null), 4000);
    return () => clearTimeout(t);
  }, [toast]);

  function nameOf(u: AdminUser) {
    return u.displayName ?? u.email ?? u.phoneE164 ?? u.id.slice(0, 8);
  }

  return (
    <div>
      <h1 style={styles.h1}>Kullanıcılar</h1>
      <p style={styles.subtitle}>{total} kullanıcı</p>

      <div style={styles.filters}>
        <div style={styles.searchBox}>
          <span style={styles.searchIcon}>🔍</span>
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Telefon, e-posta, isim ara…"
            style={styles.search}
            onKeyDown={(e) => e.key === 'Enter' && load()}
          />
        </div>
        <select
          value={roleFilter ?? ''}
          onChange={(e) => setRoleFilter(e.target.value ? Number(e.target.value) : undefined)}
          style={styles.select}
        >
          <option value="">Tüm roller</option>
          <option value="0">Müşteri</option>
          <option value="1">Salon sahibi</option>
          <option value="2">Stilist</option>
          <option value="99">Admin</option>
        </select>
        <select
          value={statusFilter ?? ''}
          onChange={(e) => setStatusFilter(e.target.value ? Number(e.target.value) : undefined)}
          style={styles.select}
        >
          <option value="">Tüm durumlar</option>
          <option value="0">Aktif</option>
          <option value="1">Askıda</option>
          <option value="2">Silinmiş</option>
        </select>
        <button onClick={load} style={styles.searchBtn}>Filtrele</button>
      </div>

      {error ? <div style={styles.error}>{error}</div> : null}
      {toast ? (
        <div style={toast.kind === 'success' ? styles.toastSuccess : styles.toastError}>{toast.msg}</div>
      ) : null}

      {loading ? (
        <div style={styles.skeletonWrap}>
          {[0, 1, 2, 3].map((i) => <div key={i} style={styles.skeletonRow} />)}
        </div>
      ) : users.length === 0 ? (
        <div style={styles.empty}>
          <div style={styles.emptyEmoji}>👤</div>
          <div style={styles.emptyTitle}>Eşleşen kullanıcı yok</div>
          <div style={styles.emptyDesc}>Farklı bir arama/filtre dene.</div>
        </div>
      ) : (
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
                <th style={styles.thRight}>Eylem</th>
              </tr>
            </thead>
            <tbody>
              {users.map((u) => {
                const status = STATUS_LABEL[u.status] ?? { text: '?', color: '#525252', bg: '#E5E5E5' };
                return (
                  <tr key={u.id} style={styles.tr}>
                    <td style={styles.td}>
                      <div style={styles.userName}>{u.displayName ?? '—'}</div>
                    </td>
                    <td style={styles.td}>
                      <div style={styles.contact}>{u.phoneE164 ?? '—'}</div>
                      {u.email ? <div style={styles.contactSmall}>{u.email}</div> : null}
                    </td>
                    <td style={styles.td}>
                      <span style={styles.roleChip}>{ROLE_LABEL[u.role]}</span>
                    </td>
                    <td style={styles.td}>
                      <span style={{ ...styles.badge, color: status.color, background: status.bg }}>
                        {status.text}
                      </span>
                    </td>
                    <td style={styles.td}>{formatDateTime(u.createdAt)}</td>
                    <td style={styles.td}>{u.lastLoginAt ? formatDateTime(u.lastLoginAt) : '—'}</td>
                    <td style={styles.tdRight}>
                      {u.status === 0 ? (
                        <button style={styles.suspendBtn} onClick={() => setSuspendModal(u)}>
                          Askıya al
                        </button>
                      ) : u.status === 1 ? (
                        <button style={styles.unsuspendBtn} onClick={() => setUnsuspendModal(u)}>
                          Aç
                        </button>
                      ) : (
                        <span style={styles.disabledLabel}>—</span>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {suspendModal ? (
        <SuspendModal
          user={suspendModal}
          onClose={() => setSuspendModal(null)}
          onDone={async () => {
            setSuspendModal(null);
            setToast({ kind: 'success', msg: `${nameOf(suspendModal)} askıya alındı.` });
            await load();
          }}
          onError={(msg) => setToast({ kind: 'error', msg })}
        />
      ) : null}

      {unsuspendModal ? (
        <UnsuspendModal
          user={unsuspendModal}
          onClose={() => setUnsuspendModal(null)}
          onDone={async () => {
            setUnsuspendModal(null);
            setToast({ kind: 'success', msg: `${nameOf(unsuspendModal)} askıdan kaldırıldı.` });
            await load();
          }}
          onError={(msg) => setToast({ kind: 'error', msg })}
        />
      ) : null}
    </div>
  );
}

function SuspendModal({
  user, onClose, onDone, onError,
}: {
  user: AdminUser;
  onClose: () => void;
  onDone: () => void;
  onError: (msg: string) => void;
}) {
  const [reason, setReason] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const reasonValid = reason.trim().length >= 3;

  async function submit() {
    if (!reasonValid) return;
    setSubmitting(true);
    try {
      await adminApi.suspendUser(user.id, reason.trim());
      onDone();
    } catch (err) {
      onError(err instanceof Error ? err.message : 'Hata');
    } finally {
      setSubmitting(false);
    }
  }

  const name = user.displayName ?? user.email ?? user.phoneE164 ?? user.id.slice(0, 8);

  return (
    <div style={styles.backdrop} onClick={onClose}>
      <div style={styles.modal} onClick={(e) => e.stopPropagation()}>
        <div style={styles.modalHeader}>
          <h2 style={styles.modalTitle}>Kullanıcıyı askıya al</h2>
          <button style={styles.closeX} onClick={onClose} aria-label="Kapat">✕</button>
        </div>
        <p style={styles.modalBody}>
          <strong>{name}</strong> hesabını askıya almak üzeresin. Kullanıcı login olamaz, mevcut
          oturumlar revoke olur. Sebep audit log'a kaydedilir.
        </p>
        <label style={styles.label}>Sebep (en az 3 karakter)</label>
        <textarea
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="örn: Yorum kuralları ihlali, sahte hesap, …"
          style={styles.textarea}
          rows={3}
          autoFocus
        />
        <div style={styles.modalActions}>
          <button style={styles.btnGhost} onClick={onClose} disabled={submitting}>Vazgeç</button>
          <button
            style={{ ...styles.btnDanger, opacity: reasonValid ? 1 : 0.5 }}
            onClick={submit}
            disabled={submitting || !reasonValid}
          >
            {submitting ? 'Askıya alınıyor…' : 'Askıya al'}
          </button>
        </div>
      </div>
    </div>
  );
}

function UnsuspendModal({
  user, onClose, onDone, onError,
}: {
  user: AdminUser;
  onClose: () => void;
  onDone: () => void;
  onError: (msg: string) => void;
}) {
  const [submitting, setSubmitting] = useState(false);

  async function submit() {
    setSubmitting(true);
    try {
      await adminApi.unsuspendUser(user.id);
      onDone();
    } catch (err) {
      onError(err instanceof Error ? err.message : 'Hata');
    } finally {
      setSubmitting(false);
    }
  }

  const name = user.displayName ?? user.email ?? user.phoneE164 ?? user.id.slice(0, 8);

  return (
    <div style={styles.backdrop} onClick={onClose}>
      <div style={styles.modal} onClick={(e) => e.stopPropagation()}>
        <div style={styles.modalHeader}>
          <h2 style={styles.modalTitle}>Askıyı kaldır</h2>
          <button style={styles.closeX} onClick={onClose} aria-label="Kapat">✕</button>
        </div>
        <p style={styles.modalBody}>
          <strong>{name}</strong> hesabını tekrar aktifleştirmek üzeresin. Kullanıcı uygulamaya
          giriş yapabilecek.
        </p>
        <div style={styles.modalActions}>
          <button style={styles.btnGhost} onClick={onClose} disabled={submitting}>Vazgeç</button>
          <button style={styles.btnSuccess} onClick={submit} disabled={submitting}>
            {submitting ? 'Açılıyor…' : 'Askıyı kaldır'}
          </button>
        </div>
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  h1: { fontSize: 32, fontWeight: 700, marginTop: 0, marginBottom: 4, color: '#1A1A1A' },
  subtitle: { color: '#6B6B6B', marginTop: 0, marginBottom: 24 },
  filters: { display: 'flex', gap: 8, marginBottom: 16, flexWrap: 'wrap', alignItems: 'center' },
  searchBox: {
    flex: 1, minWidth: 240, display: 'flex', alignItems: 'center', gap: 8,
    border: '1px solid #E5E5E5', borderRadius: 8, padding: '0 12px', background: '#fff',
  },
  searchIcon: { fontSize: 14, color: '#9B9B9B' },
  search: { flex: 1, padding: '10px 0', border: 'none', fontSize: 14, outline: 'none', background: 'transparent' },
  select: {
    padding: '10px 12px', border: '1px solid #E5E5E5', borderRadius: 8, fontSize: 14,
    background: '#fff', cursor: 'pointer', minWidth: 140,
  },
  searchBtn: {
    padding: '10px 20px', background: '#6B4F4F', color: '#fff', border: 'none',
    borderRadius: 8, fontSize: 13, fontWeight: 600, cursor: 'pointer',
  },
  tableWrap: {
    background: '#fff', borderRadius: 12, border: '1px solid #E5E5E5',
    overflow: 'auto', maxWidth: '100%',
  },
  table: { width: '100%', borderCollapse: 'collapse' as const, minWidth: 800 },
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
  tdRight: { padding: '14px 16px', fontSize: 13, color: '#1A1A1A', textAlign: 'right' as const },
  userName: { fontWeight: 600, fontSize: 14 },
  contact: { fontSize: 13, fontWeight: 500 },
  contactSmall: { fontSize: 11, color: '#9B9B9B', marginTop: 2 },
  roleChip: {
    display: 'inline-block', padding: '3px 10px', background: '#F1F5F9',
    color: '#475569', borderRadius: 999, fontSize: 11, fontWeight: 600,
  },
  badge: { padding: '4px 10px', borderRadius: 999, fontSize: 11, fontWeight: 700 },
  suspendBtn: {
    background: '#fff', color: '#92400E', border: '1px solid #FCD34D',
    padding: '7px 14px', borderRadius: 8, fontSize: 12, fontWeight: 600, cursor: 'pointer',
  },
  unsuspendBtn: {
    background: '#fff', color: '#166534', border: '1px solid #86EFAC',
    padding: '7px 14px', borderRadius: 8, fontSize: 12, fontWeight: 600, cursor: 'pointer',
  },
  disabledLabel: { color: '#9B9B9B', fontSize: 13 },

  // Loading skeleton
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

  // Modal
  backdrop: {
    position: 'fixed', inset: 0, background: 'rgba(15, 23, 42, 0.5)',
    display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000,
    padding: 16,
  },
  modal: {
    background: '#fff', borderRadius: 14, width: '100%', maxWidth: 520,
    padding: 24, boxShadow: '0 12px 40px rgba(0,0,0,0.18)',
  },
  modalHeader: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 },
  modalTitle: { fontSize: 20, fontWeight: 700, color: '#1A1A1A', margin: 0 },
  closeX: { background: 'none', border: 'none', fontSize: 18, cursor: 'pointer', color: '#6B6B6B' },
  modalBody: { fontSize: 14, color: '#475569', marginTop: 0, marginBottom: 16, lineHeight: 1.5 },
  label: {
    fontSize: 12, fontWeight: 600, color: '#475569', textTransform: 'uppercase' as const,
    letterSpacing: 0.5, display: 'block', marginBottom: 8,
  },
  textarea: {
    width: '100%', padding: 12, border: '1px solid #E2E8F0', borderRadius: 8,
    fontSize: 14, fontFamily: 'inherit', resize: 'vertical' as const, outline: 'none',
    boxSizing: 'border-box' as const,
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
  btnSuccess: {
    background: '#22C55E', color: '#fff', border: 'none',
    padding: '8px 18px', borderRadius: 8, fontSize: 14, fontWeight: 600, cursor: 'pointer',
  },
};
