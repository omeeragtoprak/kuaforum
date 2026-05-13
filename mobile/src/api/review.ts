import { apiClient } from './client';

export type Review = {
  id: string;
  customerId: string;
  stylistId: string;
  rating: number;
  body?: string;
  createdAt: string;
  replyBody?: string;
  replyAt?: string;
};

export type ShopRatingSummary = { average: number; count: number };

export type ShopReviewsResponse = {
  items: Review[];
  summary: ShopRatingSummary;
  totalCount: number;
};

export const ReviewApi = {
  async getShopReviews(shopId: string, page = 1, pageSize = 20): Promise<ShopReviewsResponse> {
    const res = await apiClient.get<ShopReviewsResponse>(`/v1/barber-shops/${shopId}/reviews`, {
      params: { page, pageSize },
    });
    return res.data;
  },
  async submit(bookingId: string, rating: number, body?: string): Promise<{ id: string }> {
    const res = await apiClient.post<{ id: string }>('/v1/reviews', { bookingId, rating, body });
    return res.data;
  },
  async reply(reviewId: string, body: string): Promise<void> {
    await apiClient.post(`/v1/reviews/${reviewId}/reply`, { body });
  },
};
