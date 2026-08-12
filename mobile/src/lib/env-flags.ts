/**
 * `EXPO_PUBLIC_*` env değişkenlerinin TEK statik okuma noktası.
 *
 * NEDEN AYRI DOSYA: Metro/babel-preset-expo (`expo-inline-production-environment-variables`
 * plugin'i) `process.env.EXPO_PUBLIC_X` gibi DOT-NOTATION erişimleri DERLEME ANINDA literal
 * değere çevirir — hem gerçek Metro/EAS bundle'da (bu YÜZDEN bayrak APK'da çalışıyor; RN
 * bundle'ında gerçek bir `process.env` yok, her şey build-time string replacement) HEM DE
 * jest-expo altında (jest'in babel `caller`'ı `isDev=false` ürettiği için — `NODE_ENV=test`,
 * `development` değil — aynı inlining jest'te de tetikleniyor). Sonucu: bu dosyanın DIŞINDA
 * `process.env.EXPO_PUBLIC_X = ...` ile runtime'da mutate etmenin okuma tarafındaki modülün
 * davranışına HİÇBİR etkisi yoktur — değer, dosya ilk import edildiği anda donar.
 *
 * Bunu test edilebilir kılmak için: `runtime-base-url.test.ts` bu modülü `jest.doMock` +
 * `jest.resetModules` + dinamik `require` ile bayrak başına yeniden yükler. Gerçek build'de
 * bu dosya normal şekilde inline edilir, ekstra bir şey gerekmez.
 */
export const TEST_LOGIN_FLAG_RAW: string | undefined = process.env.EXPO_PUBLIC_ENABLE_TEST_LOGIN;

/**
 * Hızlı-giriş (dev-login) için paylaşılan secret. `X-Dev-Access-Key` header'ı olarak
 * YALNIZ `/v1/auth/dev-login` isteğinde gönderilir.
 *
 * NEDEN GEREKLİ: dev-login uç noktası AllowAnonymous'tur ve gövdesinde yalnız bir rol adı
 * ister — Admin dahil. İnternete açık bir ortamda (Render) korumasız bırakılamaz, bu yüzden
 * sunucu `DevTools:AccessKey` ile bir paylaşılan secret ister (bkz.
 * backend/src/Kuaforum.Api/Configuration/DevToolsGate.cs). Header gönderilmediğinde sunucu
 * 403 döner — APK'da hızlı girişin "Geçersiz veya eksik X-Dev-Access-Key" ile düşmesinin
 * sebebi tam olarak buydu: sunucu tarafı doğru kuruluydu, istemci header'ı hiç göndermiyordu.
 *
 * GÜVENLİK SINIRI: değer derleme anında bundle'a GÖMÜLÜR (EXPO_PUBLIC_* inline edilir), yani
 * APK'yı eline geçiren biri okuyabilir. Bu bilinçli bir kabul — anahtar yalnız test
 * build'lerine verilir ve koruduğu ortamda gerçek kullanıcı verisi yoktur. Store build'inde
 * EXPO_PUBLIC_ENABLE_TEST_LOGIN kapalı olduğu için hızlı giriş ekranı hiç render edilmez ve
 * bu anahtar da build'e verilmez.
 */
export const DEV_ACCESS_KEY_RAW: string | undefined = process.env.EXPO_PUBLIC_DEV_ACCESS_KEY;
