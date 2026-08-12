import { turkishAblativeSuffix, formatTryFrom } from '../format';

/**
 * Türkçe ayrılma hâli ekinin sayı okunuşuna göre değişmesi.
 *
 * NEDEN BU TEST VAR: keşfet/arama kartlarındaki "en düşük fiyat" etiketi sabit `'den`
 * ile yazılıyordu ve emülatörde "₺90'den" olarak göründü — doğrusu "₺90'dan".
 * Sabit ek yazmak kolay ve sessizce yanlış olduğu için, doğru davranışı burada
 * kilitliyoruz.
 */
describe('turkishAblativeSuffix', () => {
  it('birler basamağına göre ek seçer', () => {
    expect(turkishAblativeSuffix(1)).toBe("'den");   // bir
    expect(turkishAblativeSuffix(3)).toBe("'ten");   // üç — sert ünsüz
    expect(turkishAblativeSuffix(5)).toBe("'ten");   // beş — sert ünsüz
    expect(turkishAblativeSuffix(6)).toBe("'dan");   // altı — kalın ünlü
    expect(turkishAblativeSuffix(9)).toBe("'dan");   // dokuz — kalın ünlü
  });

  it('onlar basamağında ünlü uyumu VE ünsüz benzeşmesini birlikte uygular', () => {
    expect(turkishAblativeSuffix(40)).toBe("'tan");  // kırk — sert + kalın
    expect(turkishAblativeSuffix(60)).toBe("'tan");  // altmış — sert + kalın
    expect(turkishAblativeSuffix(70)).toBe("'ten");  // yetmiş — sert + ince
    expect(turkishAblativeSuffix(80)).toBe("'den");  // seksen — yumuşak + ince
    expect(turkishAblativeSuffix(90)).toBe("'dan");  // doksan — yumuşak + kalın
  });

  it('sondaki sıfırlar okunan sözcüğü yukarı taşır', () => {
    expect(turkishAblativeSuffix(100)).toBe("'den");   // yüz
    expect(turkishAblativeSuffix(1200)).toBe("'den");  // ...iki yüz
    expect(turkishAblativeSuffix(1000)).toBe("'den");  // bin
    expect(turkishAblativeSuffix(1_000_000)).toBe("'dan"); // milyon
  });

  it('son okunan sözcük belirleyicidir, ilk basamak değil', () => {
    // 1.250 → "bin iki yüz elli" → "elli" → 'den
    expect(turkishAblativeSuffix(1250)).toBe("'den");
    // 1.290 → "...doksan" → 'dan
    expect(turkishAblativeSuffix(1290)).toBe("'dan");
  });

  it('formatTryFrom biçimlendirme ile eki birleştirir', () => {
    // Intl çıktısındaki boşluk karakteri platforma göre değişebildiği için
    // tam eşitlik yerine ekin doğruluğu kontrol ediliyor.
    expect(formatTryFrom(9000)).toMatch(/90'dan$/);   // ₺90
    expect(formatTryFrom(8000)).toMatch(/80'den$/);   // ₺80
    expect(formatTryFrom(17000)).toMatch(/170'ten$/); // ₺170
  });
});
