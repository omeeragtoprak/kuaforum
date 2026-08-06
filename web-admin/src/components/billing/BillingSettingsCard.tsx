'use client';

import { useCallback, useEffect, useState } from 'react';
import {
  adminApi,
  ApiClientError,
  percentTextToRate,
  rateToPercentText,
  type AdminBarberShopDto,
  type PlatformSettingsDto,
} from '@/lib/api';

type ToastFn = (kind: 'success' | 'error', msg: string) => void;

/**
 * RFC-012 admin ayarları: global komisyon oranı + havale bilgisi.
 * Backend oranı 0-1 arası decimal tutar (>0, <=0.5) — UI yüzde gösterir/alır,
 * dönüşüm percentTextToRate/rateToPercentText üzerinden tek noktadan yapılır.
 */
export function BillingSettingsCard({ onToast }: { onToast: ToastFn }) {
  const [settings, setSettings] = useState<PlatformSettingsDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const [percent, setPercent] = useState('');
  const [iban, setIban] = useState('');
  const [bankName, setBankName] = useState('');
  const [accountHolder, setAccountHolder] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const s = await adminApi.platformSettings();
      setSettings(s);
      setPercent(rateToPercentText(s.commissionRateGlobal));
      setIban(s.commissionPaymentIban ?? '');
      setBankName(s.commissionPaymentBankName ?? '');
      setAccountHolder(s.commissionPaymentAccountHolderName ?? '');
    } catch (err) {
      if (err instanceof ApiClientError) setError(err.message);
      else setError('Ayarlar yüklenemedi.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const rate = percentTextToRate(percent);
  const rateValid = rate !== null && rate > 0 && rate <= 0.5;
  const ibanMissing = !loading && !error && iban.trim() === '';

  async function save() {
    if (!rateValid || rate === null) return;
    setSaving(true);
    try {
      await adminApi.updatePlatformSettings({
        commissionRateGlobal: rate,
        commissionPaymentIban: iban.trim() || null,
        commissionPaymentBankName: bankName.trim() || null,
        commissionPaymentAccountHolderName: accountHolder.trim() || null,
        // RFC-013 kapora sınırları bu ekranda düzenlenmiyor; PUT tüm satırı yazdığı için
        // GET'ten okunanlar aynen geri gönderilir (undefined ise alan hiç gönderilmez —
        // sınırların backend'e eklenmediği sürümle de uyumlu kalır).
        depositPercentMin: settings?.depositPercentMin,
        depositPercentMax: settings?.depositPercentMax,
      });
      onToast('success', 'Faturalama ayarları kaydedildi.');
      await load();
    } catch (err) {
      onToast('error', err instanceof Error ? err.message : 'Ayarlar kaydedilemedi.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <section style={styles.card}>
      <div style={styles.cardHead}>
        <h2 style={styles.cardTitle}>Faturalama ayarları</h2>
        <span style={styles.cardHint}>Oran değişikliği yalnız yeni dönemleri etkiler — kapanmış statement&apos;lar snapshot&apos;lı orana bağlı kalır.</span>
      </div>

      {loading ? (
        <div style={styles.skeletonWrap}>
          {[0, 1].map((i) => <div key={i} style={styles.skeletonRow} />)}
        </div>
      ) : error ? (
        <div style={styles.error}>
          {error}
          <button style={styles.retryBtn} onClick={load}>Tekrar dene</button>
        </div>
      ) : (
        <>
          {ibanMissing ? (
            <div style={styles.warn}>
              IBAN boş — kuaförlerin borç ekranında IBAN görünmüyor, launch öncesi doldurulmalı.
            </div>
          ) : null}

          <div style={styles.grid}>
            <label style={styles.field}>
              <span style={styles.label}>Global komisyon oranı (%)</span>
              <input
                style={{ ...styles.input, ...(rateValid ? {} : styles.inputInvalid) }}
                value={percent}
                inputMode="decimal"
                onChange={(e) => setPercent(e.target.value)}
                placeholder="örn: 20"
                aria-invalid={!rateValid}
              />
              <span style={rateValid ? styles.helper : styles.helperError}>
                {rateValid
                  ? `Backend'e ${rate} olarak gönderilir (0-1 decimal).`
                  : 'Oran %0 (hariç) ile %50 arasında olmalı.'}
              </span>
            </label>

            <label style={styles.field}>
              <span style={styles.label}>IBAN</span>
              <input
                style={styles.input}
                value={iban}
                maxLength={34}
                onChange={(e) => setIban(e.target.value)}
                placeholder="TR00 0000 0000 0000 0000 0000 00"
              />
              <span style={styles.helper}>En fazla 34 karakter. Kuaförün borç ekranında bu IBAN gösterilir.</span>
            </label>

            <label style={styles.field}>
              <span style={styles.label}>Banka</span>
              <input
                style={styles.input}
                value={bankName}
                maxLength={120}
                onChange={(e) => setBankName(e.target.value)}
                placeholder="örn: Ziraat Bankası"
              />
            </label>

            <label style={styles.field}>
              <span style={styles.label}>Hesap sahibi</span>
              <input
                style={styles.input}
                value={accountHolder}
                maxLength={120}
                onChange={(e) => setAccountHolder(e.target.value)}
                placeholder="örn: Kuaforum Teknoloji A.Ş."
              />
            </label>
          </div>

          <div style={styles.cardActions}>
            <button
              style={{ ...styles.btnPrimary, opacity: rateValid && !saving ? 1 : 0.5 }}
              onClick={save}
              disabled={!rateValid || saving}
            >
              {saving ? 'Kaydediliyor…' : 'Ayarları kaydet'}
            </button>
          </div>

          <ShopRateOverride onToast={onToast} globalPercentText={settings ? rateToPercentText(settings.commissionRateGlobal) : '—'} />
        </>
      )}
    </section>
  );
}

/**
 * Salon bazlı oran override'ı. Admin salon listesi DTO'su override değerini DÖNMÜYOR
 * (AdminBarberShopDto'da alan yok) — bu yüzden mevcut değer gösterilemiyor, form
 * yalnız yazma yapar. Backend DTO'ya alan eklenince buraya "mevcut: %X" konmalı.
 */
function ShopRateOverride({ onToast, globalPercentText }: { onToast: ToastFn; globalPercentText: string }) {
  const [search, setSearch] = useState('');
  const [results, setResults] = useState<AdminBarberShopDto[]>([]);
  const [searching, setSearching] = useState(false);
  const [selectedId, setSelectedId] = useState('');
  const [percent, setPercent] = useState('');
  const [saving, setSaving] = useState(false);

  async function runSearch() {
    setSearching(true);
    try {
      const p = await adminApi.listBarberShops({ search: search.trim() || undefined, page: 1, pageSize: 25 });
      setResults(p.items);
      setSelectedId(p.items[0]?.id ?? '');
    } catch (err) {
      onToast('error', err instanceof Error ? err.message : 'Salonlar yüklenemedi.');
    } finally {
      setSearching(false);
    }
  }

  const rate = percentTextToRate(percent);
  const rateValid = rate !== null && rate > 0 && rate <= 0.5;

  async function apply(next: number | null) {
    if (!selectedId) return;
    setSaving(true);
    try {
      await adminApi.setBarberShopCommissionRate(selectedId, next);
      onToast(
        'success',
        next === null ? 'Salon oranı global orana döndürüldü.' : `Salon oranı %${percent} olarak ayarlandı.`,
      );
      setPercent('');
    } catch (err) {
      onToast('error', err instanceof Error ? err.message : 'Oran güncellenemedi.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div style={styles.subCard}>
      <div style={styles.subCardHead}>
        <h3 style={styles.subCardTitle}>Salon bazlı oran (override)</h3>
        <span style={styles.cardHint}>Override yoksa global oran (%{globalPercentText}) uygulanır.</span>
      </div>

      <div style={styles.overrideRow}>
        <input
          style={{ ...styles.input, flex: 1, minWidth: 200 }}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          onKeyDown={(e) => { if (e.key === 'Enter') runSearch(); }}
          placeholder="Salon adı ara…"
        />
        <button style={styles.btnGhost} onClick={runSearch} disabled={searching}>
          {searching ? 'Aranıyor…' : 'Ara'}
        </button>
        <select
          style={styles.select}
          value={selectedId}
          onChange={(e) => setSelectedId(e.target.value)}
          disabled={results.length === 0}
          aria-label="Salon seç"
        >
          {results.length === 0 ? <option value="">Önce salon ara</option> : null}
          {results.map((s) => (
            <option key={s.id} value={s.id}>{s.name}</option>
          ))}
        </select>
        <input
          style={{ ...styles.input, width: 110, ...(percent === '' || rateValid ? {} : styles.inputInvalid) }}
          value={percent}
          inputMode="decimal"
          onChange={(e) => setPercent(e.target.value)}
          placeholder="oran %"
          aria-label="Salon komisyon oranı yüzde"
        />
        <button
          style={{ ...styles.btnPrimary, opacity: selectedId && rateValid && !saving ? 1 : 0.5 }}
          onClick={() => apply(rate)}
          disabled={!selectedId || !rateValid || saving}
        >
          Kaydet
        </button>
        <button
          style={{ ...styles.btnGhost, opacity: selectedId && !saving ? 1 : 0.5 }}
          onClick={() => apply(null)}
          disabled={!selectedId || saving}
        >
          Override&apos;ı kaldır
        </button>
      </div>
      <span style={styles.helper}>
        Mevcut override değeri admin salon listesi API&apos;sinde dönmüyor — bu form yalnız yazar,
        değişiklik audit log&apos;a düşer.
      </span>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  card: {
    background: '#fff', borderRadius: 12, border: '1px solid #E5E5E5',
    padding: 20, marginBottom: 24,
  },
  cardHead: { display: 'flex', flexDirection: 'column', gap: 4, marginBottom: 16 },
  cardTitle: { fontSize: 18, fontWeight: 700, margin: 0, color: '#1A1A1A' },
  cardHint: { fontSize: 12, color: '#6B6B6B' },
  grid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 16 },
  field: { display: 'flex', flexDirection: 'column', gap: 6 },
  label: {
    fontSize: 12, fontWeight: 600, color: '#475569',
    textTransform: 'uppercase' as const, letterSpacing: 0.5,
  },
  input: {
    padding: '10px 12px', border: '1px solid #E2E8F0', borderRadius: 8,
    fontSize: 14, fontFamily: 'inherit', outline: 'none', boxSizing: 'border-box' as const,
    width: '100%',
  },
  inputInvalid: { border: '1px solid #EF4444' },
  helper: { fontSize: 11, color: '#9B9B9B' },
  helperError: { fontSize: 11, color: '#B91C1C' },
  cardActions: { display: 'flex', justifyContent: 'flex-end', marginTop: 16 },
  btnPrimary: {
    background: '#6B4F4F', color: '#fff', border: 'none',
    padding: '9px 18px', borderRadius: 8, fontSize: 14, fontWeight: 600, cursor: 'pointer',
  },
  btnGhost: {
    background: '#F1F5F9', color: '#475569', border: 'none',
    padding: '9px 14px', borderRadius: 8, fontSize: 13, fontWeight: 600, cursor: 'pointer',
    whiteSpace: 'nowrap' as const,
  },
  select: {
    padding: '10px 12px', border: '1px solid #E2E8F0', borderRadius: 8, fontSize: 14,
    background: '#fff', cursor: 'pointer', minWidth: 180,
  },
  subCard: {
    marginTop: 20, paddingTop: 16, borderTop: '1px solid #F1F5F9',
    display: 'flex', flexDirection: 'column', gap: 10,
  },
  subCardHead: { display: 'flex', flexDirection: 'column', gap: 2 },
  subCardTitle: { fontSize: 14, fontWeight: 700, margin: 0, color: '#1A1A1A' },
  overrideRow: { display: 'flex', gap: 8, flexWrap: 'wrap' as const, alignItems: 'center' },
  warn: {
    padding: 12, background: '#FFFBEB', color: '#92400E', border: '1px solid #FDE68A',
    borderRadius: 8, fontSize: 13, marginBottom: 16, fontWeight: 500,
  },
  error: {
    padding: 12, background: '#FEF2F2', color: '#991B1B', borderRadius: 8,
    fontSize: 14, display: 'flex', alignItems: 'center', gap: 12,
  },
  retryBtn: {
    background: '#fff', border: '1px solid #FCA5A5', color: '#991B1B',
    padding: '6px 12px', borderRadius: 6, fontSize: 12, fontWeight: 600, cursor: 'pointer',
  },
  skeletonWrap: { display: 'flex', flexDirection: 'column', gap: 8 },
  skeletonRow: {
    height: 48, background: 'linear-gradient(90deg, #F1F5F9 0%, #E2E8F0 50%, #F1F5F9 100%)',
    backgroundSize: '200% 100%', borderRadius: 8, animation: 'shimmer 1.6s linear infinite',
  },
};
