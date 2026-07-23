'use client';

import { Fragment, useCallback, useEffect, useMemo, useState } from 'react';
import { adminApi, type AuditLogItem, ApiClientError } from '@/lib/api';

const ACTOR_TONE: Record<string, { bg: string; color: string; label: string }> = {
  User: { bg: '#EDE9FE', color: '#5B21B6', label: 'Kullanıcı' },
  Admin: { bg: '#FEF3C7', color: '#92400E', label: 'Admin' },
  System: { bg: '#F1F5F9', color: '#475569', label: 'Sistem' },
};

export default function AuditLogsPage() {
  const [items, setItems] = useState<AuditLogItem[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const pageSize = 50;

  const [actionType, setActionType] = useState('');
  const [resourceType, setResourceType] = useState('');
  const [resourceId, setResourceId] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [expandedId, setExpandedId] = useState<number | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await adminApi.listAuditLogs({
        actionType: actionType.trim() || undefined,
        resourceType: resourceType.trim() || undefined,
        resourceId: resourceId.trim() || undefined,
        page,
        pageSize,
      });
      setItems(res.items);
      setTotal(res.totalCount);
    } catch (err) {
      if (err instanceof ApiClientError) setError(err.message);
      else setError('Sunucuya ulaşılamadı.');
    } finally {
      setLoading(false);
    }
  }, [actionType, resourceType, resourceId, page]);

  useEffect(() => { load(); }, [load]);

  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const actionSummary = useMemo(() => {
    const counts = new Map<string, number>();
    items.forEach((i) => counts.set(i.actionType, (counts.get(i.actionType) ?? 0) + 1));
    return Array.from(counts.entries()).sort((a, b) => b[1] - a[1]).slice(0, 5);
  }, [items]);

  return (
    <div>
      <h1 style={styles.h1}>Audit Log</h1>
      <p style={styles.subtitle}>{total} kayıt · sayfa {page}/{totalPages}</p>

      <div style={styles.filters}>
        <input
          placeholder="Eylem tipi (örn: admin.barbershop.suspended)"
          value={actionType}
          onChange={(e) => { setActionType(e.target.value); setPage(1); }}
          style={styles.input}
          onKeyDown={(e) => e.key === 'Enter' && load()}
        />
        <input
          placeholder="Kaynak tipi (örn: BarberShop, Booking)"
          value={resourceType}
          onChange={(e) => { setResourceType(e.target.value); setPage(1); }}
          style={styles.input}
          onKeyDown={(e) => e.key === 'Enter' && load()}
        />
        <input
          placeholder="Kaynak ID (Guid)"
          value={resourceId}
          onChange={(e) => { setResourceId(e.target.value); setPage(1); }}
          style={styles.input}
          onKeyDown={(e) => e.key === 'Enter' && load()}
        />
        <button onClick={load} style={styles.searchBtn}>Filtrele</button>
      </div>

      {actionSummary.length > 0 ? (
        <div style={styles.chipsRow}>
          {actionSummary.map(([action, count]) => (
            <button
              key={action}
              onClick={() => { setActionType(action); setPage(1); }}
              style={styles.chip}
              title={`${count} kez`}
            >
              {action} <span style={styles.chipCount}>{count}</span>
            </button>
          ))}
        </div>
      ) : null}

      {error ? <div style={styles.error}>{error}</div> : null}

      {loading ? (
        <div style={styles.skeletonWrap}>
          {[0, 1, 2, 3, 4, 5].map((i) => <div key={i} style={styles.skeletonRow} />)}
        </div>
      ) : items.length === 0 ? (
        <div style={styles.empty}>
          <div style={styles.emptyEmoji}>🕵️</div>
          <div style={styles.emptyTitle}>Eşleşen audit kaydı yok</div>
          <div style={styles.emptyDesc}>Filtreleri temizle veya farklı bir eylem tipi dene.</div>
        </div>
      ) : (
      <div style={styles.tableWrap}>
        <table style={styles.table}>
          <thead>
            <tr>
              <th style={styles.th}>Zaman (UTC)</th>
              <th style={styles.th}>Aktör</th>
              <th style={styles.th}>Eylem</th>
              <th style={styles.th}>Kaynak</th>
              <th style={styles.thRight}>Payload</th>
            </tr>
          </thead>
          <tbody>
            {items.map((it) => {
              const tone = ACTOR_TONE[it.actorType] ?? ACTOR_TONE.System;
              const expanded = expandedId === it.id;
              return (
                <Fragment key={it.id}>
                  <tr>
                    <td style={styles.td}>{new Date(it.createdAt).toLocaleString('tr-TR')}</td>
                    <td style={styles.td}>
                      <span style={{ ...styles.badge, background: tone.bg, color: tone.color }}>{tone.label}</span>
                      <div style={styles.actor}>{it.actorDisplayName ?? (it.actorUserId ? it.actorUserId.slice(0, 8) : '—')}</div>
                    </td>
                    <td style={styles.td}><code style={styles.code}>{it.actionType}</code></td>
                    <td style={styles.td}>
                      <div style={styles.resourceType}>{it.resourceType}</div>
                      <div style={styles.resourceId}>{it.resourceId}</div>
                    </td>
                    <td style={styles.tdRight}>
                      {it.payloadJson ? (
                        <button
                          onClick={() => setExpandedId(expanded ? null : it.id)}
                          style={styles.expandBtn}
                        >
                          {expanded ? 'Gizle' : 'Göster'}
                        </button>
                      ) : <span style={styles.disabledLabel}>—</span>}
                    </td>
                  </tr>
                  {expanded && it.payloadJson ? (
                    <tr>
                      <td colSpan={5} style={styles.payloadRow}>
                        <pre style={styles.pre}>{formatJson(it.payloadJson)}</pre>
                        {it.ip || it.requestId ? (
                          <div style={styles.payloadMeta}>
                            {it.ip ? <span>IP: {it.ip}</span> : null}
                            {it.requestId ? <span>Request: {it.requestId}</span> : null}
                          </div>
                        ) : null}
                      </td>
                    </tr>
                  ) : null}
                </Fragment>
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
    </div>
  );
}

function formatJson(s: string): string {
  try { return JSON.stringify(JSON.parse(s), null, 2); } catch { return s; }
}

const styles: Record<string, React.CSSProperties> = {
  h1: { fontSize: 32, fontWeight: 700, marginTop: 0, marginBottom: 4, color: '#1A1A1A' },
  subtitle: { color: '#6B6B6B', marginTop: 0, marginBottom: 24 },
  filters: { display: 'flex', gap: 8, marginBottom: 12, flexWrap: 'wrap' },
  input: {
    flex: 1, minWidth: 220, padding: '10px 12px', border: '1px solid #E5E5E5',
    borderRadius: 8, fontSize: 14, outline: 'none', background: '#fff',
  },
  searchBtn: {
    padding: '10px 20px', background: '#6B4F4F', color: '#fff', border: 'none',
    borderRadius: 8, fontSize: 13, fontWeight: 600, cursor: 'pointer',
  },
  chipsRow: { display: 'flex', flexWrap: 'wrap', gap: 8, marginBottom: 16 },
  chip: {
    padding: '6px 12px', background: '#F1F5F9', color: '#475569', border: 'none',
    borderRadius: 999, fontSize: 12, fontWeight: 600, cursor: 'pointer',
  },
  chipCount: {
    marginLeft: 6, padding: '0 6px', background: '#fff', borderRadius: 999,
    fontSize: 11, color: '#475569',
  },
  tableWrap: {
    background: '#fff', borderRadius: 12, border: '1px solid #E5E5E5',
    overflow: 'auto', maxWidth: '100%',
  },
  table: { width: '100%', borderCollapse: 'collapse' as const, minWidth: 900 },
  th: {
    padding: '12px 16px', textAlign: 'left' as const, background: '#F5F5F5',
    fontSize: 11, fontWeight: 700, color: '#6B6B6B',
    textTransform: 'uppercase' as const, letterSpacing: 0.5,
    borderBottom: '1px solid #E5E5E5',
  },
  thRight: {
    padding: '12px 16px', textAlign: 'right' as const, background: '#F5F5F5',
    fontSize: 11, fontWeight: 700, color: '#6B6B6B',
    textTransform: 'uppercase' as const, letterSpacing: 0.5,
    borderBottom: '1px solid #E5E5E5',
  },
  td: { padding: '12px 16px', fontSize: 13, color: '#1A1A1A', borderBottom: '1px solid #F0F0F0', verticalAlign: 'top' },
  tdRight: { padding: '12px 16px', fontSize: 13, color: '#1A1A1A', borderBottom: '1px solid #F0F0F0', textAlign: 'right' as const },
  badge: { display: 'inline-block', padding: '3px 10px', borderRadius: 999, fontSize: 11, fontWeight: 700 },
  actor: { fontSize: 11, color: '#6B6B6B', marginTop: 4 },
  code: { fontFamily: 'Menlo, monospace', fontSize: 12, color: '#0F172A', background: '#F8FAFC', padding: '2px 6px', borderRadius: 4 },
  resourceType: { fontWeight: 600 },
  resourceId: { fontSize: 11, color: '#9B9B9B', fontFamily: 'Menlo, monospace', marginTop: 2 },
  expandBtn: {
    padding: '6px 12px', background: '#fff', border: '1px solid #E2E8F0',
    borderRadius: 6, fontSize: 12, fontWeight: 600, cursor: 'pointer', color: '#475569',
  },
  disabledLabel: { color: '#9B9B9B' },
  payloadRow: { padding: 0, background: '#0F172A', borderBottom: '1px solid #E5E5E5' },
  pre: {
    margin: 0, padding: 16, fontFamily: 'Menlo, monospace', fontSize: 12,
    color: '#E2E8F0', whiteSpace: 'pre-wrap' as const, wordBreak: 'break-all' as const,
  },
  payloadMeta: {
    padding: '8px 16px', background: '#1E293B', color: '#94A3B8',
    fontSize: 11, display: 'flex', gap: 16, borderTop: '1px solid #334155',
  },

  // Loading / empty
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
    borderRadius: 6, fontSize: 14, marginBottom: 12,
  },
  pagination: { display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 16, marginTop: 16 },
  pageBtn: {
    padding: '8px 16px', background: '#fff', border: '1px solid #D4D4D4',
    borderRadius: 6, cursor: 'pointer', fontSize: 13,
  },
  pageInfo: { fontSize: 13, color: '#6B6B6B' },
};
