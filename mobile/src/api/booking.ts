import { apiClient } from './client';

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
};

export const BookingApi = {
  async initiate(stylistId: string, scheduledAt: string, serviceCatalogIds: string[], customerNote?: string, isWalkIn = false) {
    const res = await apiClient.post('/v1/bookings', {
      stylistId,
      scheduledAt,
      serviceCatalogIds,
      isWalkIn,
      customerNote,
    });
    return res.data;
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
};
