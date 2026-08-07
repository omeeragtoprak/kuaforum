'use client';

import { useCallback, useEffect, useState } from 'react';
import {
  adminApi,
  ApiClientError,
  percentTextToRate,
  rateToPercentText,
  type PlatformSettingsDto,
} from '@/lib/api';

type ToastFn = (kind: 'success' | 'error', msg: string) => void;

/**
 * RFC-014 admin faturalama ayarları.
 *
 * "Global komisyon oranı" alanı KALDIRILDI — gelir modeli ciro komisyonundan sabit aylık
 * aboneliğe geçtiği için backend PlatformSettingsDto artık commissionRateGlobal DÖNMÜYOR.
 * Eski hâlinde bu alan undefined okunup NaN'a düşüyor, doğrulama sonsuza dek false kalıyor
 * ve "Kaydet" butonu kilitlendiği için IBAN dahi kaydedilemiyordu. Bu ekranda hiçbir
 * doğrulama, ödeme bilgisi alanlarının kaydını BLOKLAMAMALI.
 *
 * Geriye kalan gerçek ayarlar (backend UpdatePlatformSettingsRequest ile birebir):
 *   - CommissionPaymentIban / BankName / AccountHolderName → abonelik ücretinin
 *     havale edileceği platform hesabı (alan adlarındaki "commission" öneki RFC-012'den
 *     kalma; backend şeması bilerek değiştirilmedi).
 *   - DepositPercentMin / Max → RFC-013 kapora oranı alt/üst sınırı.
 * PUT tüm satırı yazar; hiçbir alan kısmi güncellenmez.
 */
export function BillingSettingsCard({ onToast }: { onToast: ToastFn }) {
  const [settings, setSettings] = useState<PlatformSettingsDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const [iban, setIban] = useState('');
  const [bankName, setBankName] = useState('');
  const [accountHolder, setAccountHolder] = useState('');
  const [depositMinPercent, setDepositMinPercent] = useState('');
  const [depositMaxPercent, setDepositMaxPercent] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const s = await adminApi.platformSettings();
      setSettings(s);
      setIban(s.commissionPaymentIban ?? '');
      setBankName(s.commissionPaymentBankName ?? '');
      setAccountHolder(s.commissionPaymentAccountHolderName ?? '');
      setDepositMinPercent(rateToPercentText(s.depositPercentMin));
      setDepositMaxPercent(rateToPercentText(s.depositPercentMax));
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

  const depositMin = percentTextToRate(depositMinPercent);
  const depositMax = percentTextToRate(depositMaxPercent);
  // Backend validator: her ikisi de > 0; domain guard ayrıca min < max ve <= %100 ister.
  const depositMinValid = depositMin !== null && depositMin > 0 && depositMin <= 1;
  const depositMaxValid = depositMax !== null && depositMax > 0 && depositMax <= 1;
  const depositRangeValid =
    depositMinValid && depositMaxValid && (depositMin as number) < (depositMax as number);

  const canSave = settings !== null && depositRangeValid && !saving;
  const ibanMissing = !loading && !error && iban.trim() === '';

  async function save() {
    if (settings === null || depositMin === null || depositMax === null || !depositRangeValid) return;
    setSaving(true);
    try {
      const result = await adminApi.updatePlatformSettings({
        commissionPaymentIban: iban.trim() || null,
        commissionPaymentBankName: bankName.trim() || null,
        commissionPaymentAccountHolderName: accountHolder.trim() || null,
        depositPercentMin: depositMin,
        depositPercentMax: depositMax,
      });
      onToast(
        'success',
        result.outOfRangeShopCount > 0
          ? `Ayarlar kaydedildi. ${result.outOfRangeShopCount} salonun kapora oranı yeni aralığın dışında kaldı — otomatik düzeltilmedi, salon sahibi bir sonraki kaydında aralığa uymak zorunda.`
          : 'Faturalama ayarları kaydedildi.',
      );
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
        <span style={styles.cardHint}>
          Abonelik ücretinin havale edileceği platform hesabı ve kapora oranı sınırları. Plan
          fiyatları bu ekranda değil, abonelik planlarında tanımlıdır.
        </span>
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
              IBAN boş — kuaförlerin abonelik borcu ekranında IBAN görünmüyor, launch öncesi doldurulmalı.
            </div>
          ) : null}

          <div style={styles.grid}>
            <label style={styles.field}>
              <span style={styles.label}>IBAN</span>
              <input
                style={styles.input}
                value={iban}
                maxLength={34}
                onChange={(e) => setIban(e.target.value)}
                placeholder="TR00 0000 0000 0000 0000 0000 00"
              />
              <span style={styles.helper}>En fazla 34 karakter. Kuaförün abonelik borcu ekranında bu IBAN gösterilir.</span>
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

            <label style={styles.field}>
              <span style={styles.label}>Kapora oranı alt sınırı (%)</span>
              <input
                style={{ ...styles.input, ...(depositMinValid ? {} : styles.inputInvalid) }}
                value={depositMinPercent}
                inputMode="decimal"
                onChange={(e) => setDepositMinPercent(e.target.value)}
                placeholder="örn: 5"
                aria-invalid={!depositMinValid}
              />
              <span style={depositMinValid ? styles.helper : styles.helperError}>
                {depositMinValid ? '%0 (hariç) ile %100 arası.' : 'Alt sınır %0 (hariç) ile %100 arasında olmalı.'}
              </span>
            </label>

            <label style={styles.field}>
              <span style={styles.label}>Kapora oranı üst sınırı (%)</span>
              <input
                style={{ ...styles.input, ...(depositMaxValid ? {} : styles.inputInvalid) }}
                value={depositMaxPercent}
                inputMode="decimal"
                onChange={(e) => setDepositMaxPercent(e.target.value)}
                placeholder="örn: 50"
                aria-invalid={!depositMaxValid}
              />
              <span style={depositMaxValid ? styles.helper : styles.helperError}>
                {depositMaxValid ? '%0 (hariç) ile %100 arası.' : 'Üst sınır %0 (hariç) ile %100 arasında olmalı.'}
              </span>
            </label>
          </div>

          {depositMinValid && depositMaxValid && !depositRangeValid ? (
            <div style={styles.rangeError}>Alt sınır üst sınırdan küçük olmalı.</div>
          ) : null}

          <div style={styles.cardActions}>
            <button
              style={{ ...styles.btnPrimary, opacity: canSave ? 1 : 0.5 }}
              onClick={save}
              disabled={!canSave}
            >
              {saving ? 'Kaydediliyor…' : 'Ayarları kaydet'}
            </button>
          </div>
        </>
      )}
    </section>
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
  rangeError: { fontSize: 12, color: '#B91C1C', marginTop: 10, fontWeight: 600 },
  cardActions: { display: 'flex', justifyContent: 'flex-end', marginTop: 16 },
  btnPrimary: {
    background: '#6B4F4F', color: '#fff', border: 'none',
    padding: '9px 18px', borderRadius: 8, fontSize: 14, fontWeight: 600, cursor: 'pointer',
  },
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
