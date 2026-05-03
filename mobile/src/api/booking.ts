import { apiClient } from './client';

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
  stylistId: string;
  scheduledAt: string;
  scheduledEndsAt: string;
  status: number; // BookingStatus enum int
  totalAmountMinor: number;
  currency: string;
  refundPercentage?: number;
  items: BookingItem[];
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
  async cancel(id: string, reason: number): Promise<{ refundPercentage: number }> {
    const res = await apiClient.post(`/v1/bookings/${id}/cancel`, { reason });
    return res.data;
  },
};
