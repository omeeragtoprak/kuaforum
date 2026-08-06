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
