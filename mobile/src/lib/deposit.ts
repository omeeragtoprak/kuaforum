/** RFC-006 — backend Payment:DepositRate ile senkron tutulmalı (kaynak: appsettings Payment:DepositRate).
 *  Yalnız ÖN İZLEME için; kesin tutar initiate response'undan (amountMinor) gelir. */
export const DEPOSIT_RATE = 0.15;

export function estimateDepositMinor(totalMinor: number): number {
  return Math.ceil(totalMinor * DEPOSIT_RATE);
}

export function remainingAtShopMinor(totalMinor: number, depositMinor: number): number {
  return totalMinor - depositMinor;
}
