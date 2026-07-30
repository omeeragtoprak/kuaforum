import { DEPOSIT_RATE, estimateDepositMinor, remainingAtShopMinor } from '@/lib/deposit';

describe('estimateDepositMinor', () => {
  it('20000 kuruş (200 TL) için %15 kaporayı hesaplar', () => {
    expect(estimateDepositMinor(20000)).toBe(3000);
  });

  it('kesirli sonucu backend ile aynı şekilde yukarı yuvarlar (ceil)', () => {
    // 333 * 0.15 = 49.95 -> ceil -> 50
    expect(estimateDepositMinor(333)).toBe(50);
  });

  it('DEPOSIT_RATE 0.15 olarak sabitlenmiş (backend Payment:DepositRate ile senkron)', () => {
    expect(DEPOSIT_RATE).toBe(0.15);
  });
});

describe('remainingAtShopMinor', () => {
  it('toplamdan kaporayı düşerek salonda ödenecek kalanı verir', () => {
    expect(remainingAtShopMinor(20000, 3000)).toBe(17000);
  });
});
