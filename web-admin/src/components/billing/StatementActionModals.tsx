'use client';

import { useState } from 'react';
import {
  adminApi,
  formatDateOnly,
  formatTry,
  parseTryToMinor,
  type AdminSubscriptionInvoiceDto,
} from '@/lib/api';

type ModalProps = {
  invoice: AdminSubscriptionInvoiceDto;
  onClose: () => void;
  onDone: (msg: string) => void;
  onError: (msg: string) => void;
};

function Shell({
  title, children, onClose, submitLabel, submitting, disabled, onSubmit, danger,
}: {
  title: string;
  children: React.ReactNode;
  onClose: () => void;
  submitLabel: string;
  submitting: boolean;
  disabled: boolean;
  onSubmit: () => void;
  danger?: boolean;
}) {
  return (
    <div style={styles.backdrop} onClick={onClose}>
      <div style={styles.modal} onClick={(e) => e.stopPropagation()} role="dialog" aria-modal="true">
        <div style={styles.modalHeader}>
          <h2 style={styles.modalTitle}>{title}</h2>
          <button style={styles.closeX} onClick={onClose} aria-label="Kapat">✕</button>
        </div>
        {children}
        <div style={styles.modalActions}>
          <button style={styles.btnGhost} onClick={onClose} disabled={submitting}>Vazgeç</button>
          <button
            style={{ ...(danger ? styles.btnDanger : styles.btnPrimary), opacity: disabled || submitting ? 0.5 : 1 }}
            onClick={onSubmit}
            disabled={disabled || submitting}
          >
            {submitting ? 'İşleniyor…' : submitLabel}
          </button>
        </div>
      </div>
    </div>
  );
}

function InvoiceSummary({ s }: { s: AdminSubscriptionInvoiceDto }) {
  return (
    <div style={styles.summary}>
      <div><strong>{s.barberShopName}</strong></div>
      <div style={styles.summaryLine}>
        Dönem: {formatDateOnly(s.periodStart)} – {formatDateOnly(s.periodEnd)}
      </div>
      <div style={styles.summaryLine}>
        Plan: <strong>{s.planCode}</strong> · Plan fiyatı: {formatTry(s.planPriceMinor)}
      </div>
      <div style={styles.summaryLine}>
        Tutar: <strong>{formatTry(s.amountMinor)}</strong> · Vade: {formatDateOnly(s.dueDate)}
      </div>
    </div>
  );
}

/** Havale/EFT onayı. Backend gerekçe için min 5 karakter ister (MarkSubscriptionInvoicePaidCommandValidator). */
export function MarkPaidModal({ invoice, onClose, onDone, onError }: ModalProps) {
  const [reason, setReason] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const valid = reason.trim().length >= 5;

  async function submit() {
    if (!valid) return;
    setSubmitting(true);
    try {
      await adminApi.markInvoicePaid(invoice.id, reason.trim());
      onDone(`${invoice.barberShopName} abonelik faturası ödendi olarak işaretlendi.`);
    } catch (err) {
      onError(err instanceof Error ? err.message : 'İşlem başarısız.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Shell
      title="Ödendi işaretle"
      onClose={onClose}
      submitLabel="Ödendi işaretle"
      submitting={submitting}
      disabled={!valid}
      onSubmit={submit}
    >
      <InvoiceSummary s={invoice} />
      <div style={styles.warn}>
        Bu işlem hesabı otomatik aktifleştirmez — salon askıya alınmışsa Salonlar sayfasından
        ayrıca aktifleştirin.
      </div>
      <label style={styles.label} htmlFor="mark-paid-reason">Dekont / açıklama (en az 5 karakter)</label>
      <textarea
        id="mark-paid-reason"
        value={reason}
        onChange={(e) => setReason(e.target.value)}
        placeholder="örn: 12.08 tarihli havale, dekont no 998877"
        style={styles.textarea}
        rows={3}
        maxLength={500}
        autoFocus
      />
    </Shell>
  );
}

/**
 * Tutar düzeltme — backend gerekçe için min 8 karakter ister, tutar kuruş cinsinden gider.
 *
 * DİKKAT (para hatası koruması): backend validator tutarı `>= 0` kabul ediyor, yani 0 TL
 * geçerli sayılıp fatura tahsil edilemez hâle geliyor ve 204 döndüğü için ekranda hiçbir
 * uyarı çıkmıyor. İstemci tarafında tutarı `> 0` şartına bağlıyoruz; sıfırlama gerekiyorsa
 * bu ekran değil, faturayı iptal eden ayrı bir akış kullanılmalı.
 */
export function AdjustAmountModal({ invoice, onClose, onDone, onError }: ModalProps) {
  const [amount, setAmount] = useState((invoice.amountMinor / 100).toFixed(2).replace('.', ','));
  const [reason, setReason] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const minor = parseTryToMinor(amount);
  const amountParsed = minor !== null;
  const amountValid = minor !== null && minor > 0;
  const reasonValid = reason.trim().length >= 8;
  const valid = amountValid && reasonValid;

  async function submit() {
    if (!valid || minor === null) return;
    setSubmitting(true);
    try {
      await adminApi.adjustInvoiceAmount(invoice.id, minor, reason.trim());
      onDone(`${invoice.barberShopName} fatura tutarı ${formatTry(minor)} olarak güncellendi.`);
    } catch (err) {
      onError(err instanceof Error ? err.message : 'İşlem başarısız.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Shell
      title="Tutar düzelt"
      onClose={onClose}
      submitLabel="Tutarı güncelle"
      submitting={submitting}
      disabled={!valid}
      onSubmit={submit}
    >
      <InvoiceSummary s={invoice} />
      <label style={styles.label} htmlFor="adjust-amount">Yeni tutar (₺)</label>
      <input
        id="adjust-amount"
        value={amount}
        inputMode="decimal"
        onChange={(e) => setAmount(e.target.value)}
        style={{ ...styles.input, ...(amountValid ? {} : styles.inputInvalid) }}
        aria-invalid={!amountValid}
        autoFocus
      />
      <span style={amountValid ? styles.helper : styles.helperError}>
        {amountValid
          ? `Backend'e ${minor} kuruş olarak gönderilir (plan fiyatı: ${formatTry(invoice.planPriceMinor)}).`
          : amountParsed
            ? 'Tutar 0 TL olamaz — sıfırlanacaksa fatura iptal edilmeli, tutarı düşürülmemeli.'
            : 'Geçerli bir tutar gir (örn: 1.250,00 veya 1250).'}
      </span>
      <label style={{ ...styles.label, marginTop: 14 }} htmlFor="adjust-reason">
        Gerekçe (zorunlu, en az 8 karakter)
      </label>
      <textarea
        id="adjust-reason"
        value={reason}
        onChange={(e) => setReason(e.target.value)}
        placeholder="örn: Ay ortasında plan düşürüldü, fark iade edildi"
        style={styles.textarea}
        rows={3}
        maxLength={500}
      />
      <span style={reasonValid ? styles.helper : styles.helperError}>
        {reason.trim().length}/8 karakter · gerekçe audit log&apos;a yazılır.
      </span>
    </Shell>
  );
}

/** Vade uzatma — yeni vade mevcut vadeden İLERİ olmalı (domain kuralı), gerekçe min 8 karakter. */
export function ExtendDueModal({ invoice, onClose, onDone, onError }: ModalProps) {
  const [dueDate, setDueDate] = useState('');
  const [reason, setReason] = useState('');
  const [submitting, setSubmitting] = useState(false);

  // DateOnly stringleri "yyyy-MM-dd" — sözlüksel karşılaştırma kronolojik sıraya eşit.
  const dateValid = dueDate !== '' && dueDate > invoice.dueDate;
  const reasonValid = reason.trim().length >= 8;
  const valid = dateValid && reasonValid;

  async function submit() {
    if (!valid) return;
    setSubmitting(true);
    try {
      await adminApi.extendInvoiceDueDate(invoice.id, dueDate, reason.trim());
      onDone(`${invoice.barberShopName} vadesi ${formatDateOnly(dueDate)} tarihine uzatıldı.`);
    } catch (err) {
      onError(err instanceof Error ? err.message : 'İşlem başarısız.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Shell
      title="Vade uzat"
      onClose={onClose}
      submitLabel="Vadeyi uzat"
      submitting={submitting}
      disabled={!valid}
      onSubmit={submit}
    >
      <InvoiceSummary s={invoice} />
      <div style={styles.info}>
        Vade uzatma, gecikme sayacını durduran tek mekanizmadır. Gecikmiş (Overdue) bir kayıt,
        yeni vade bugünden ileriyse otomatik olarak &quot;Bildirildi&quot; durumuna geri döner.
      </div>
      <label style={styles.label} htmlFor="extend-date">Yeni vade tarihi</label>
      <input
        id="extend-date"
        type="date"
        value={dueDate}
        min={invoice.dueDate}
        onChange={(e) => setDueDate(e.target.value)}
        style={{ ...styles.input, ...(dueDate === '' || dateValid ? {} : styles.inputInvalid) }}
        aria-invalid={dueDate !== '' && !dateValid}
        autoFocus
      />
      <span style={dueDate === '' || dateValid ? styles.helper : styles.helperError}>
        Mevcut vade: {formatDateOnly(invoice.dueDate)} — yeni vade bu tarihten ileri olmalı.
      </span>
      <label style={{ ...styles.label, marginTop: 14 }} htmlFor="extend-reason">
        Gerekçe (zorunlu, en az 8 karakter)
      </label>
      <textarea
        id="extend-reason"
        value={reason}
        onChange={(e) => setReason(e.target.value)}
        placeholder="örn: Kuaför itirazı incelemede, 10 gün ek süre verildi"
        style={styles.textarea}
        rows={3}
        maxLength={500}
      />
      <span style={reasonValid ? styles.helper : styles.helperError}>
        {reason.trim().length}/8 karakter · gerekçe audit log&apos;a yazılır.
      </span>
    </Shell>
  );
}

const styles: Record<string, React.CSSProperties> = {
  backdrop: {
    position: 'fixed', inset: 0, background: 'rgba(15, 23, 42, 0.5)',
    display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000, padding: 16,
  },
  modal: {
    background: '#fff', borderRadius: 14, width: '100%', maxWidth: 520,
    padding: 24, boxShadow: '0 12px 40px rgba(0,0,0,0.18)', maxHeight: '90vh', overflowY: 'auto',
  },
  modalHeader: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 },
  modalTitle: { fontSize: 20, fontWeight: 700, color: '#1A1A1A', margin: 0 },
  closeX: { background: 'none', border: 'none', fontSize: 18, cursor: 'pointer', color: '#6B6B6B' },
  summary: {
    background: '#F8FAFC', borderRadius: 10, padding: 12, fontSize: 13,
    color: '#334155', marginBottom: 14, display: 'flex', flexDirection: 'column', gap: 3,
  },
  summaryLine: { fontSize: 12, color: '#64748B' },
  warn: {
    padding: 12, background: '#FFFBEB', color: '#92400E', border: '1px solid #FDE68A',
    borderRadius: 8, fontSize: 13, marginBottom: 14, fontWeight: 500, lineHeight: 1.5,
  },
  info: {
    padding: 12, background: '#EFF6FF', color: '#1E40AF', border: '1px solid #BFDBFE',
    borderRadius: 8, fontSize: 12.5, marginBottom: 14, lineHeight: 1.5,
  },
  label: {
    fontSize: 12, fontWeight: 600, color: '#475569', textTransform: 'uppercase' as const,
    letterSpacing: 0.5, display: 'block', marginBottom: 6,
  },
  input: {
    width: '100%', padding: 12, border: '1px solid #E2E8F0', borderRadius: 8,
    fontSize: 14, fontFamily: 'inherit', outline: 'none', boxSizing: 'border-box' as const,
  },
  inputInvalid: { border: '1px solid #EF4444' },
  textarea: {
    width: '100%', padding: 12, border: '1px solid #E2E8F0', borderRadius: 8,
    fontSize: 14, fontFamily: 'inherit', resize: 'vertical' as const, outline: 'none',
    boxSizing: 'border-box' as const,
  },
  helper: { fontSize: 11, color: '#9B9B9B', display: 'block', marginTop: 4 },
  helperError: { fontSize: 11, color: '#B91C1C', display: 'block', marginTop: 4 },
  modalActions: { display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 20 },
  btnGhost: {
    background: '#F1F5F9', color: '#475569', border: 'none',
    padding: '8px 18px', borderRadius: 8, fontSize: 14, fontWeight: 600, cursor: 'pointer',
  },
  btnPrimary: {
    background: '#6B4F4F', color: '#fff', border: 'none',
    padding: '8px 18px', borderRadius: 8, fontSize: 14, fontWeight: 600, cursor: 'pointer',
  },
  btnDanger: {
    background: '#EF4444', color: '#fff', border: 'none',
    padding: '8px 18px', borderRadius: 8, fontSize: 14, fontWeight: 600, cursor: 'pointer',
  },
};
