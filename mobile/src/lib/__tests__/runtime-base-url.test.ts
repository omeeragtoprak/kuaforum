import { normalizeBaseUrl } from '@/lib/runtime-base-url';

jest.mock('@react-native-async-storage/async-storage', () =>
  // eslint-disable-next-line @typescript-eslint/no-var-requires
  require('@react-native-async-storage/async-storage/jest/async-storage-mock'),
);

const STORAGE_KEY = 'kuaforum.dev_api_base_url_override';

describe('normalizeBaseUrl', () => {
  it.each([
    ['http://192.168.1.11:5100', 'http://192.168.1.11:5100'],
    ['https://api.kuaforum.com', 'https://api.kuaforum.com'],
    ['http://192.168.1.11:5100/', 'http://192.168.1.11:5100'],
    ['http://192.168.1.11:5100///', 'http://192.168.1.11:5100'],
    ['  http://192.168.1.11:5100  ', 'http://192.168.1.11:5100'],
    ['http://192.168.1.11:5100/api/', 'http://192.168.1.11:5100/api'],
  ])('geçerli girdi normalize edilir: %s -> %s', (input, expected) => {
    expect(normalizeBaseUrl(input)).toBe(expected);
  });

  it.each([
    [''],
    ['   '],
    ['192.168.1.11:5100'], // şema yok
    ['ftp://foo.com'], // yanlış şema
    ['http://'], // host yok
    ['https://'],
    ['http:///'], // şema + sadece slash (host yok)
    ['http://192 .168.1.11:5100'], // içinde boşluk
    ['not a url at all'],
  ])('geçersiz girdi null döner: %s', (input) => {
    expect(normalizeBaseUrl(input)).toBeNull();
  });
});

/**
 * `EXPO_PUBLIC_ENABLE_TEST_LOGIN` derleme anında `src/lib/env-flags.ts` içinde literal'e
 * inline edildiği için (bkz. o dosyadaki açıklama — jest-expo altında da aynı inlining
 * tetikleniyor) düz `process.env.X = ...` mutasyonunun `runtime-base-url.ts`'in davranışına
 * hiçbir etkisi YOK. Bunun yerine `env-flags` modülünü test başına `jest.doMock` +
 * `jest.resetModules` ile değiştirip `runtime-base-url`'i dinamik `require` ediyoruz — bu,
 * flag'in gerçek build'de neden runtime'da değiştirilemeyen sağlam bir güvenlik sınırı
 * olduğunu da kanıtlıyor.
 */
function loadRuntimeBaseUrl(flagValue: string | undefined) {
  jest.resetModules();
  jest.doMock('@/lib/env-flags', () => ({ TEST_LOGIN_FLAG_RAW: flagValue }));
  return {
    // eslint-disable-next-line @typescript-eslint/no-var-requires
    mod: require('@/lib/runtime-base-url') as typeof import('@/lib/runtime-base-url'),
    // Aynı resetModules turunda taze bir mock storage instance'ı — `mod`'un içeride
    // require ettiği AsyncStorage ile AYNI instance (top-level'da bir kez import edip
    // resetModules SONRASI kullanmak farklı/eski bir instance'a "sızdırırdı").
    // eslint-disable-next-line @typescript-eslint/no-var-requires
    storage: require('@react-native-async-storage/async-storage') as typeof import('@react-native-async-storage/async-storage').default,
  };
}

afterEach(() => {
  jest.dontMock('@/lib/env-flags');
  jest.resetModules();
});

describe('isTestBuild', () => {
  it('flag "1" ise true döner', () => {
    const { mod } = loadRuntimeBaseUrl('1');
    expect(mod.isTestBuild()).toBe(true);
  });

  it('flag yoksa/başka bir değerse false döner', () => {
    expect(loadRuntimeBaseUrl(undefined).mod.isTestBuild()).toBe(false);
    expect(loadRuntimeBaseUrl('true').mod.isTestBuild()).toBe(false);
  });
});

describe('kalıcı ayar — production güvenlik gate', () => {
  it('flag KAPALIYKEN kalıcı ayar OKUNMAZ — storage\'da eski/kötü niyetli bir değer olsa bile yok sayılır', async () => {
    const { mod, storage } = loadRuntimeBaseUrl(undefined);
    // Eski bir test build'den kalmış (ya da manipüle edilmiş) bir değeri simüle et.
    await storage.setItem(STORAGE_KEY, 'http://should-never-be-read:9999');

    expect(await mod.getPersistedBaseUrlOverride()).toBeNull();
  });

  it('flag KAPALIYKEN kalıcı ayara YAZILMAZ — set çağrısı sessiz no-op', async () => {
    const { mod, storage } = loadRuntimeBaseUrl(undefined);
    await storage.setItem(STORAGE_KEY, 'http://untouched:1111');

    await mod.setPersistedBaseUrlOverride('http://should-not-be-written:9999');

    expect(await storage.getItem(STORAGE_KEY)).toBe('http://untouched:1111');
  });

  it('flag AÇIKKEN yazar / okur / temizler', async () => {
    const { mod } = loadRuntimeBaseUrl('1');
    expect(await mod.getPersistedBaseUrlOverride()).toBeNull();

    await mod.setPersistedBaseUrlOverride('http://192.168.1.11:5100');
    expect(await mod.getPersistedBaseUrlOverride()).toBe('http://192.168.1.11:5100');

    await mod.clearPersistedBaseUrlOverride();
    expect(await mod.getPersistedBaseUrlOverride()).toBeNull();
  });
});

describe('resolveInitialBaseUrl — öncelik zinciri (kalıcı ayar → derleme varsayılanı/env)', () => {
  it('kalıcı ayar varsa (test build) onu kullanır — build default/env yok sayılır', async () => {
    const { mod } = loadRuntimeBaseUrl('1');
    await mod.setPersistedBaseUrlOverride('http://192.168.1.11:5100');

    expect(await mod.resolveInitialBaseUrl('http://10.0.2.2:5100')).toBe('http://192.168.1.11:5100');
  });

  it('kalıcı ayar yoksa build default/env kullanılır', async () => {
    const { mod } = loadRuntimeBaseUrl('1');
    expect(await mod.resolveInitialBaseUrl('http://10.0.2.2:5100')).toBe('http://10.0.2.2:5100');
  });

  it('production build (flag kapalı) kalıcı ayarda ne olursa olsun HER ZAMAN build default/env kullanır', async () => {
    const { mod, storage } = loadRuntimeBaseUrl(undefined);
    await storage.setItem(STORAGE_KEY, 'http://leftover-override:1234');

    expect(await mod.resolveInitialBaseUrl('http://10.0.2.2:5100')).toBe('http://10.0.2.2:5100');
  });
});
