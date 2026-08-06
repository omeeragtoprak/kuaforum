import AsyncStorage from '@react-native-async-storage/async-storage';
import { TEST_LOGIN_FLAG_RAW } from './env-flags';

/**
 * Test build'lerde (EXPO_PUBLIC_ENABLE_TEST_LOGIN=1) API taban adresini derleme
 * SONRASI (runtime) değiştirebilmek için. Founder'ın LAN IP'si değiştikçe 75 MB'lık
 * test APK'sını yeniden derleyip göndermek yerine login ekranından adres güncellenir
 * (bkz. 2026-08-06 "sunucuya ulaşılamadı" olayı — IP 192.168.2.15 → 192.168.1.11).
 *
 * GÜVENLİK — tek kaynak burası: `isTestBuild()` App.config.js'teki AYNI bayrağı
 * (`process.env.EXPO_PUBLIC_ENABLE_TEST_LOGIN === '1'`) kullanır, `__DEV__` DAHİL
 * DEĞİL (login.tsx'teki `showQuickLogin` gibi __DEV__'i de kapsamaz — kasıtlı: bu
 * özellik yalnız derlenmiş APK sorunu için var, Expo Go'da ihtiyaç yok). Bayrak
 * kapalıyken `getPersistedBaseUrlOverride`/`setPersistedBaseUrlOverride` AsyncStorage'a
 * HİÇ dokunmaz — store build'de kalıcı ayar okunamaz/yazılamaz bile (derlenmiş kod
 * içinde kalsa da fonksiyonel olarak erişilemez).
 */

const STORAGE_KEY = 'kuaforum.dev_api_base_url_override';

export function isTestBuild(): boolean {
  return TEST_LOGIN_FLAG_RAW === '1';
}

/**
 * Kullanıcı girdisini API taban adresi olarak kullanılabilir hale normalize eder.
 * Saf fonksiyon — network/storage yok, testli.
 *
 * Geçerli: baştaki/sondaki boşluk kırpılır, `http://`/`https://` ile başlamalı,
 * sondaki `/` (tekrarlıları dahil) kırpılır, şema sonrası en az bir host karakteri
 * olmalı, adresin içinde boşluk kalmamalı.
 *
 * Geçersizse `null` döner (çağıran taraf kaydetmeyi reddetmeli).
 */
export function normalizeBaseUrl(raw: string): string | null {
  const trimmed = raw.trim();
  if (!trimmed) return null;
  if (/\s/.test(trimmed)) return null;

  // Şema + en az bir host karakteri zorunlu — "http://" veya "http:///" tek başına
  // geçersizdir (host yok). Trailing slash'lar YALNIZ şemadan SONRAKİ kısımdan
  // kırpılır — aksi halde "http://" için ".replace(/\/+$/, '')" şemanın kendi "//"
  // sini de yiyip "http:" gibi anlamsız-ama-"geçerli-görünen" bir sonuç üretiyordu.
  const match = /^(https?:\/\/)(.+)$/i.exec(trimmed);
  if (!match) return null;

  const [, scheme, rest] = match;
  const hostAndPath = rest.replace(/\/+$/, '');
  if (!hostAndPath) return null;

  return scheme + hostAndPath;
}

/** Yalnız test build'de AsyncStorage'dan okur; production'da her zaman `null`. */
export async function getPersistedBaseUrlOverride(): Promise<string | null> {
  if (!isTestBuild()) return null;
  try {
    return await AsyncStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

/** Yalnız test build'de yazar; production'da sessiz no-op (güvenlik gate'i). */
export async function setPersistedBaseUrlOverride(url: string): Promise<void> {
  if (!isTestBuild()) return;
  await AsyncStorage.setItem(STORAGE_KEY, url);
}

/** Yalnız test build'de temizler; production'da sessiz no-op. */
export async function clearPersistedBaseUrlOverride(): Promise<void> {
  if (!isTestBuild()) return;
  await AsyncStorage.removeItem(STORAGE_KEY);
}

/**
 * Öncelik zinciri: kalıcı ayar (yalnız test build) → derleme varsayılanı
 * (`buildDefault` — çağıran `api/client.ts`'teki mevcut env/Constants/Platform
 * zincirinin sonucunu geçirir).
 */
export async function resolveInitialBaseUrl(buildDefault: string): Promise<string> {
  const override = await getPersistedBaseUrlOverride();
  return override ?? buildDefault;
}
