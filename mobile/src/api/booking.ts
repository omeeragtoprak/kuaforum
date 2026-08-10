import { apiClient } from './client';
import type { PaymentKind } from './payment';

export type OwnerDashboardStats = {
  thisWeekCompletedCount: number;
  thisWeekRevenueMinor: number;
  lastWeekCompletedCount: number;
  lastWeekRevenueMinor: number;
  revenueDeltaPercent: number;
  topServiceName: string | null;
  topServiceBookingCount: number;
};

export type BookingStatus = 'Pending' | 'Confirmed' | 'Started' | 'Completed' | 'Cancelled' | 'NoShow';

export type BookingItem = {
  id: string;
  serviceCatalogId: string;
  serviceName: string;
  durationMinutes: number;
  priceMinor: number;
};

export type Booking = {
  id: string;
  customerId: string;
  barberShopId: string;
  /** Optional — populated by admin/owner endpoints; undefined on customer GetMyBookings. */
  shopName?: string;
  stylistId: string;
  stylistName?: string;
  scheduledAt: string;
  scheduledEndsAt: string;
  status: number | string;
  totalAmountMinor: number;
  currency: string;
  refundPercentage?: number;
  items: BookingItem[];
  customerName?: string | null;
  customerPhone?: string | null;
  customerNote?: string | null;
  hasReview?: boolean;
  /**
   * RFC-015 §2 D3 — bu randevu için "kuaför gelmedi / salon kapalıydı" bildirimi zaten yapılmış
   * mı (randevu başına TEK bildirim). true ise şikayet butonu tekrar gösterilmez.
   */
  hasComplaint?: boolean;
  /** Yalnız GERÇEKTEN ödenmiş (Captured/Settled) payment'larda dolu — Initiated yok sayılır. */
  paymentKind?: PaymentKind | null;
  paidOnlineMinor?: number | null;
  remainingAtShopMinor?: number;
  /**
   * RFC-014 §2 D2 madde 6 — salonun O ANKİ (canlı) auto-confirm ayarı. Status ile birlikte
   * okunur: Status Pending iken bu true olması "kuaför onayı bekleniyor" DEĞİL, "ödeme
   * bekleniyor" anlamına gelir (auto-confirm kuaför onayını atlar, ödemeyi atlamaz — bkz.
   * lib/booking-confirm.ts resolveBookingPromise). Backend Booking.AutoConfirmSnapshot XML
   * doc'undaki aynı çıkarım kuralı.
   */
  autoConfirmEnabled: boolean;
};

/**
 * RFC-014 §2 D4 — müşterinin AYNI zaman aralığına BAŞKA bir salondan çakışan
 * (Pending/Confirmed/Started) randevusu. Backend `ConflictingBookingDto` ile birebir.
 * Founder kararı: yalnız BİLGİLENDİRME amaçlı, randevu oluşturmayı ENGELLEMEZ.
 */
export type ConflictingBooking = {
  bookingId: string;
  barberShopName: string;
  scheduledAt: string;
  scheduledEndsAt: string;
};

/** Backend InitiateBookingCommand.cs → InitiateBookingResponse record ile birebir (camelCase). */
export type InitiateBookingResponse = {
  bookingId: string;
  scheduledAt: string;
  scheduledEndsAt: string;
  totalAmountMinor: number;
  currency: string;
  /** RFC-014 §2 D2 madde 6 — bkz. Booking.autoConfirmEnabled yorumu, aynı çıkarım kuralı. */
  status: BookingStatus;
  autoConfirmEnabled: boolean;
  /**
   * RFC-014 §2 D4 — "son söz". Ön-kontrol (checkConflict) ile bu yanıt arasında geçen sürede
   * araya başka bir randevu girebilir (yarış); bu alan doluysa randevu YİNE DE oluşturulmuştur,
   * yalnız bilgilendirme amaçlıdır.
   */
  conflictingBooking: ConflictingBooking | null;
};

export const BookingApi = {
  async initiate(
    stylistId: string,
    scheduledAt: string,
    serviceCatalogIds: string[],
    customerNote?: string,
    isWalkIn = false,
  ): Promise<InitiateBookingResponse> {
    const res = await apiClient.post<InitiateBookingResponse>('/v1/bookings', {
      stylistId,
      scheduledAt,
      serviceCatalogIds,
      isWalkIn,
      customerNote,
    });
    return res.data;
  },
  /**
   * RFC-014 §2 D4 madde 1 — özet ekranının oluşturmadan ÖNCE çağırdığı ön-kontrol. Tavsiye
   * niteliğinde: ağ hatasında caller randevu oluşturmayı BLOKLAMAMALI (bkz. booking-confirm.ts
   * kullanım yeri) — bu yüzden burada hata yutulmuyor, sorumluluk caller'da.
   */
  async checkConflict(
    barberShopId: string,
    scheduledAt: string,
    scheduledEndsAt: string,
  ): Promise<ConflictingBooking | null> {
    const res = await apiClient.post<{ conflictingBooking: ConflictingBooking | null }>(
      '/v1/bookings/check-conflict',
      { barberShopId, scheduledAt, scheduledEndsAt },
    );
    return res.data.conflictingBooking;
  },
  async getMine(status?: number): Promise<Booking[]> {
    const params = status !== undefined ? { status } : undefined;
    const res = await apiClient.get<Booking[]>('/v1/bookings', { params });
    return res.data;
  },
  async getById(id: string): Promise<Booking> {
    const res = await apiClient.get<Booking>(`/v1/bookings/${id}`);
    return res.data;
  },
  // Reason is derived server-side from the actor's role (customer vs. shop owner) — see
  // backend CancelBookingCommandHandler.ResolveReasonAsync. The client can no longer choose it
  // (previously an arbitrary BookingCancellationReason in the body could force a bigger refund).
  async cancel(id: string): Promise<{ refundPercentage: number }> {
    const res = await apiClient.post(`/v1/bookings/${id}/cancel`);
    return res.data;
  },

  async getOwnerStats(): Promise<OwnerDashboardStats> {
    const res = await apiClient.get<OwnerDashboardStats>('/v1/owner/barber-shop/stats');
    return res.data;
  },

  // Barber owner side
  async getOwnerBookings(from: string, to: string, status?: number): Promise<Booking[]> {
    const params: Record<string, string | number> = { from, to };
    if (status !== undefined) params.status = status;
    const res = await apiClient.get<Booking[]>('/v1/owner/barber-shop/bookings', { params });
    return res.data;
  },
  async ownerConfirm(id: string): Promise<void> {
    await apiClient.post(`/v1/bookings/${id}/confirm`);
  },
  async reschedule(id: string, newScheduledAt: string): Promise<{ bookingId: string; scheduledAt: string; scheduledEndsAt: string }> {
    const res = await apiClient.post(`/v1/bookings/${id}/reschedule`, { newScheduledAt });
    return res.data;
  },
  async start(id: string): Promise<void> {
    await apiClient.post(`/v1/bookings/${id}/start`);
  },
  async complete(id: string): Promise<void> {
    await apiClient.post(`/v1/bookings/${id}/complete`);
  },
  async markNoShow(id: string): Promise<void> {
    await apiClient.post(`/v1/bookings/${id}/no-show`);
  },

  /**
   * RFC-015 §2 D3 — müşterinin "kuaför gelmedi / salon kapalıydı" bildirimi. Yalnız GEÇMİŞ
   * Confirmed/Started/Completed randevularda kabul edilir (backend 400 döner, mobil de
   * `canFileBookingComplaint` ile aynı kuralla butonu gizler — bkz. lib/salon-changes.ts).
   * İdempotent: randevu başına tek kayıt, ikinci çağrı aynı complaintId'yi no-op döner.
   * Admin kuyruğuna düşer, OTOMATİK İADE tetiklemez.
   */
  async fileComplaint(id: string, note?: string): Promise<{ complaintId: string }> {
    const res = await apiClient.post<{ complaintId: string }>(`/v1/bookings/${id}/complaint`, { note });
    return res.data;
  },
};
