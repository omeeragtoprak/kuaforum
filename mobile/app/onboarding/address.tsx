import { useEffect, useRef, useState } from 'react';
import { StyleSheet, Alert, KeyboardAvoidingView, Platform, ScrollView } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { EmptyState } from '@/components/EmptyState';
import { OnboardingHeader } from '@/components/onboarding/OnboardingHeader';
import { GeoPickerModal, GeoPickerItem } from '@/components/onboarding/GeoPickerModal';
import { GeoPickerRow } from '@/components/onboarding/GeoPickerRow';
import { Colors, Spacing, Layout } from '@/lib/theme';
import { GeographyApi, City, District, Neighborhood } from '@/api/geography';
import { BarberApi } from '@/api/barber';
import { extractApiError } from '@/api/client';
import { Haptics } from '@/lib/haptics';
import { isValidAddressLine } from '@/lib/onboarding';
import { getCityCentroid } from '@/lib/tr-city-centroids';
import { useOnboardingStore } from '@/store/onboarding-store';

type PickerKind = 'city' | 'district' | 'neighborhood' | null;

/**
 * Adım 2 — il/ilçe/mahalle (backend SetAddressRequest NeighborhoodId'yi zorunlu
 * kılıyor, salt il/ilçe yetmiyor) + açık adres satırı. Şehir/ilçe/mahalle TAM
 * nesneleri yalnız bu ekranın local state'inde tutulur (isim + plaka kodu gerekli);
 * Zustand'a yalnız ID + addressLine yazılır. Bilinen sınır: ekran remount olursa
 * (ör. adım 3'ten geri dönülürse) il/ilçe/mahalle seçimleri sıfırlanır — isimleri
 * yeniden çözmek için ek round-trip gerekeceğinden MVP'de kapsam dışı bırakıldı.
 */
export default function OnboardingAddressScreen() {
  const router = useRouter();
  const shopId = useOnboardingStore((s) => s.shopId);
  const addressLine = useOnboardingStore((s) => s.addressLine);
  const setField = useOnboardingStore((s) => s.setField);

  const mountedRef = useRef(true);
  useEffect(() => () => { mountedRef.current = false; }, []);

  const [cities, setCities] = useState<City[]>([]);
  const [citiesStatus, setCitiesStatus] = useState<'loading' | 'ready' | 'error'>('loading');
  const [selectedCity, setSelectedCity] = useState<City | null>(null);

  const [districts, setDistricts] = useState<District[]>([]);
  const [districtsStatus, setDistrictsStatus] = useState<'idle' | 'loading' | 'ready' | 'error'>('idle');
  const [selectedDistrict, setSelectedDistrict] = useState<District | null>(null);

  const [neighborhoods, setNeighborhoods] = useState<Neighborhood[]>([]);
  const [neighborhoodsStatus, setNeighborhoodsStatus] = useState<'idle' | 'loading' | 'ready' | 'error'>('idle');
  const [selectedNeighborhood, setSelectedNeighborhood] = useState<Neighborhood | null>(null);

  const [openPicker, setOpenPicker] = useState<PickerKind>(null);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => { loadCities(); }, []);

  async function loadCities() {
    setCitiesStatus('loading');
    try {
      const data = await GeographyApi.getCities();
      if (!mountedRef.current) return;
      setCities(data);
      setCitiesStatus('ready');
    } catch {
      if (!mountedRef.current) return;
      setCitiesStatus('error');
    }
  }

  async function loadDistricts(cityId: string) {
    setDistrictsStatus('loading');
    try {
      const data = await GeographyApi.getDistricts(cityId);
      if (!mountedRef.current) return;
      setDistricts(data);
      setDistrictsStatus('ready');
    } catch {
      if (!mountedRef.current) return;
      setDistrictsStatus('error');
    }
  }

  async function loadNeighborhoods(districtId: string) {
    setNeighborhoodsStatus('loading');
    try {
      const data = await GeographyApi.getNeighborhoods(districtId);
      if (!mountedRef.current) return;
      setNeighborhoods(data);
      setNeighborhoodsStatus('ready');
    } catch {
      if (!mountedRef.current) return;
      setNeighborhoodsStatus('error');
    }
  }

  function pickCity(item: GeoPickerItem) {
    const city = cities.find((c) => c.id === item.id) ?? null;
    setSelectedCity(city);
    setField('cityId', item.id);
    setSelectedDistrict(null);
    setField('districtId', null);
    setDistricts([]);
    setDistrictsStatus('idle');
    setSelectedNeighborhood(null);
    setField('neighborhoodId', null);
    setNeighborhoods([]);
    setNeighborhoodsStatus('idle');
    setOpenPicker(null);
    if (city) loadDistricts(city.id);
  }

  function pickDistrict(item: GeoPickerItem) {
    const district = districts.find((d) => d.id === item.id) ?? null;
    setSelectedDistrict(district);
    setField('districtId', item.id);
    setSelectedNeighborhood(null);
    setField('neighborhoodId', null);
    setNeighborhoods([]);
    setNeighborhoodsStatus('idle');
    setOpenPicker(null);
    if (district) loadNeighborhoods(district.id);
  }

  function pickNeighborhood(item: GeoPickerItem) {
    const neighborhood = neighborhoods.find((n) => n.id === item.id) ?? null;
    setSelectedNeighborhood(neighborhood);
    setField('neighborhoodId', item.id);
    setOpenPicker(null);
  }

  const addressValid = isValidAddressLine(addressLine);
  const formValid = !!selectedCity && !!selectedDistrict && !!selectedNeighborhood && addressValid;

  async function handleContinue() {
    if (!formValid || submitting || !shopId || !selectedCity || !selectedDistrict || !selectedNeighborhood) return;
    setSubmitting(true);
    try {
      const centroid = getCityCentroid(selectedCity.plateCode);
      await BarberApi.setAddress(shopId, {
        cityId: selectedCity.id,
        districtId: selectedDistrict.id,
        neighborhoodId: selectedNeighborhood.id,
        line1: addressLine.trim(),
        latitude: centroid.latitude,
        longitude: centroid.longitude,
      });
      if (!mountedRef.current) return;
      Haptics.success();
      router.push('/onboarding/documents' as never);
    } catch (err) {
      if (!mountedRef.current) return;
      Haptics.error();
      Alert.alert('Hata', extractApiError(err));
    } finally {
      if (mountedRef.current) setSubmitting(false);
    }
  }

  if (!shopId) {
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <EmptyState
          emoji="🏪"
          title="Önce salon bilgisi gerekli"
          description="Adres girmeden önce salon adını ve hizmet segmentini tamamlaman gerekiyor."
          actionLabel="Adım 1'e git"
          onAction={() => router.replace('/onboarding/shop' as never)}
        />
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} style={styles.flex}>
        <ScrollView contentContainerStyle={styles.scroll} showsVerticalScrollIndicator={false} keyboardShouldPersistTaps="handled">
          <OnboardingHeader
            step={2}
            onBack={() => router.back()}
            title="Salonun nerede?"
            subtitle="Müşteriler yol tarifi için bu adresi kullanacak."
          />

          <GeoPickerRow
            label="İl *"
            value={selectedCity?.name ?? null}
            placeholder="İl seç"
            status={citiesStatus}
            onPress={() => citiesStatus === 'ready' && setOpenPicker('city')}
            onRetry={loadCities}
            disabled={citiesStatus === 'loading'}
          />

          <GeoPickerRow
            label="İlçe *"
            value={selectedDistrict?.name ?? null}
            placeholder={selectedCity ? 'İlçe seç' : 'Önce il seç'}
            status={selectedCity ? districtsStatus : 'idle'}
            onPress={() => districtsStatus === 'ready' && setOpenPicker('district')}
            onRetry={() => selectedCity && loadDistricts(selectedCity.id)}
            disabled={!selectedCity || districtsStatus === 'loading'}
          />

          <GeoPickerRow
            label="Mahalle *"
            value={selectedNeighborhood?.name ?? null}
            placeholder={selectedDistrict ? 'Mahalle seç' : 'Önce ilçe seç'}
            status={selectedDistrict ? neighborhoodsStatus : 'idle'}
            onPress={() => neighborhoodsStatus === 'ready' && setOpenPicker('neighborhood')}
            onRetry={() => selectedDistrict && loadNeighborhoods(selectedDistrict.id)}
            disabled={!selectedDistrict || neighborhoodsStatus === 'loading'}
          />

          <Input
            label="Açık adres"
            required
            value={addressLine}
            onChangeText={(v) => setField('addressLine', v)}
            placeholder="Mahalle, cadde, kapı no…"
            multiline
            maxLength={200}
            hint={addressValid || addressLine.length === 0 ? `${addressLine.length}/200` : 'En az 5 karakter olmalı'}
            error={addressLine.length > 0 && !addressValid ? 'En az 5, en fazla 200 karakter olmalı' : undefined}
            style={styles.multiline}
          />

          <Button label="Devam" onPress={handleContinue} loading={submitting} disabled={!formValid} size="lg" />
        </ScrollView>
      </KeyboardAvoidingView>

      <GeoPickerModal
        visible={openPicker === 'city'}
        title="İl seç"
        items={cities}
        selectedId={selectedCity?.id ?? null}
        onClose={() => setOpenPicker(null)}
        onPick={pickCity}
        emptyLabel="İl bulunamadı"
      />
      <GeoPickerModal
        visible={openPicker === 'district'}
        title="İlçe seç"
        items={districts}
        selectedId={selectedDistrict?.id ?? null}
        onClose={() => setOpenPicker(null)}
        onPick={pickDistrict}
        emptyLabel="İlçe bulunamadı"
      />
      <GeoPickerModal
        visible={openPicker === 'neighborhood'}
        title="Mahalle seç"
        items={neighborhoods}
        selectedId={selectedNeighborhood?.id ?? null}
        onClose={() => setOpenPicker(null)}
        onPick={pickNeighborhood}
        emptyLabel="Mahalle bulunamadı"
      />
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  flex: { flex: 1 },
  scroll: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingTop: Spacing.lg,
    paddingBottom: Spacing.xxl,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  multiline: { minHeight: 72, textAlignVertical: 'top', paddingTop: 12 },
});
