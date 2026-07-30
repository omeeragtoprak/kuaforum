import { apiClient } from './client';

export type PaymentKind = 'FullOnline' | 'Deposit';

export type InitiatePaymentResponse = {
  paymentId: string;
  checkoutUrl: string;
  providerToken?: string;
  expiresAt: string;
  amountMinor: number;
  remainingAtShopMinor: number;
  kind: PaymentKind;
};

/** Her denemede benzersiz Idempotency-Key üretir — backend resume/switch semantiği
 *  aynı key'in tekrarını 409'lar; yeni key + aynı booking = güvenli reinitiate. */
function newIdempotencyKey(): string {
  return `mob-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
}

export const PaymentApi = {
  async initiate(bookingId: string, kind: PaymentKind): Promise<InitiatePaymentResponse> {
    const res = await apiClient.post<InitiatePaymentResponse>(
      '/v1/payments/initiate',
      { bookingId, kind },
      { headers: { 'Idempotency-Key': newIdempotencyKey() } },
    );
    return res.data;
  },
};
