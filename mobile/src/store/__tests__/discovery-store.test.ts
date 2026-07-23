import { useDiscoveryStore, DEFAULT_CITY_PLATE, DEFAULT_CITY_NAME } from '@/store/discovery-store';

// Zustand store singleton test'ler arası state sızdırır — her testten önce sıfırla.
const resetStore = () => useDiscoveryStore.setState({ cityId: null, cityName: null });

describe('useDiscoveryStore', () => {
  beforeEach(() => {
    resetStore();
  });

  it('başlangıçta seçili şehir yoktur (default null — Erzurum otomatik seçimi ekran katmanında yapılır)', () => {
    const state = useDiscoveryStore.getState();
    expect(state.cityId).toBeNull();
    expect(state.cityName).toBeNull();
  });

  it('setCity ile şehir seçimini günceller', () => {
    useDiscoveryStore.getState().setCity('25', 'Erzurum');
    const state = useDiscoveryStore.getState();
    expect(state.cityId).toBe('25');
    expect(state.cityName).toBe('Erzurum');
  });

  it('clear ile seçimi sıfırlar', () => {
    useDiscoveryStore.getState().setCity('34', 'İstanbul');
    useDiscoveryStore.getState().clear();
    const state = useDiscoveryStore.getState();
    expect(state.cityId).toBeNull();
    expect(state.cityName).toBeNull();
  });

  it('demo default şehir sabitleri Erzurum (plate 25) olarak tanımlı', () => {
    expect(DEFAULT_CITY_PLATE).toBe(25);
    expect(DEFAULT_CITY_NAME).toBe('Erzurum');
  });
});
