import { useOnboardingStore } from '@/store/onboarding-store';

const resetStore = () => useOnboardingStore.getState().reset();

describe('useOnboardingStore', () => {
  beforeEach(() => {
    resetStore();
  });

  it('başlangıç state boş form + shopId null', () => {
    const state = useOnboardingStore.getState();
    expect(state.shopName).toBe('');
    expect(state.genderType).toBe('Unisex');
    expect(state.phone).toBe('');
    expect(state.cityId).toBeNull();
    expect(state.districtId).toBeNull();
    expect(state.neighborhoodId).toBeNull();
    expect(state.addressLine).toBe('');
    expect(state.shopId).toBeNull();
  });

  it('setField tek alanı günceller, diğerlerine dokunmaz', () => {
    useOnboardingStore.getState().setField('shopName', 'Ahmet Berber');
    useOnboardingStore.getState().setField('genderType', 'MaleOnly');
    const state = useOnboardingStore.getState();
    expect(state.shopName).toBe('Ahmet Berber');
    expect(state.genderType).toBe('MaleOnly');
    expect(state.phone).toBe('');
  });

  it('setShopId shopId alanını ayarlar', () => {
    useOnboardingStore.getState().setShopId('shop-123');
    expect(useOnboardingStore.getState().shopId).toBe('shop-123');
  });

  it('reset tüm alanları başlangıç değerine döndürür', () => {
    useOnboardingStore.getState().setField('shopName', 'Ahmet Berber');
    useOnboardingStore.getState().setField('addressLine', 'Atatürk Cad. No:1');
    useOnboardingStore.getState().setShopId('shop-123');
    useOnboardingStore.getState().reset();
    const state = useOnboardingStore.getState();
    expect(state.shopName).toBe('');
    expect(state.addressLine).toBe('');
    expect(state.shopId).toBeNull();
  });
});
