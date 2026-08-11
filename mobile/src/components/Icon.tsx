import React from 'react';
import Svg, { Path, Circle, Rect, Line } from 'react-native-svg';
import { Colors } from '@/lib/theme';

/**
 * ÇİZGİ İKON SETİ — "Vitrin" tasarım sistemi.
 *
 * NEDEN VAR: uygulama sekme çubuğundan boş-durum görsellerine kadar EMOJİ
 * kullanıyordu (🏠 🔍 📅 🔔 👤 ✂️ …). Emoji ikon üç sorun üretiyor:
 *   1. Görünüm cihazdan cihaza değişir (Android/iOS/sürüm farkı) — tasarım
 *      kontrolü elden çıkar.
 *   2. Renk ve çizgi kalınlığı ayarlanamaz; seçili/seçili değil ayrımı yalnız
 *      metin rengiyle yapılabilir.
 *   3. "Yapay zekâ üretimi arayüz" izleniminin en tanınmış işaretlerinden biri.
 *
 * Tek dosyada, bağımlılıksız (yalnız react-native-svg), `currentColor` yerine
 * `color` prop'uyla. Yeni ikon eklerken: 24×24 viewBox, stroke tabanlı, dolgu yok.
 */

export type IconName =
  | 'home' | 'search' | 'calendar' | 'bell' | 'user'
  | 'scissors' | 'clock' | 'mapPin' | 'star' | 'chevronRight' | 'chevronLeft'
  | 'check' | 'x' | 'plus' | 'settings' | 'logout' | 'shield' | 'creditCard'
  | 'phone' | 'alert' | 'info' | 'refresh' | 'store' | 'users' | 'wallet'
  | 'lock' | 'document' | 'flag' | 'globe' | 'edit';

type Props = {
  name: IconName;
  size?: number;
  color?: string;
  /** Seçili sekme gibi vurgulu durumlarda 2, normalde 1.6 */
  strokeWidth?: number;
};

export function Icon({ name, size = 22, color = Colors.textSecondary, strokeWidth = 1.6 }: Props) {
  const p = { stroke: color, strokeWidth, strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const, fill: 'none' };

  return (
    <Svg width={size} height={size} viewBox="0 0 24 24">
      {name === 'home' && <Path {...p} d="M3 10.5 12 4l9 6.5V20a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z" />}
      {name === 'search' && (<><Circle {...p} cx="11" cy="11" r="7" /><Path {...p} d="m20 20-4-4" /></>)}
      {name === 'calendar' && (<><Rect {...p} x="3" y="5" width="18" height="16" rx="2" /><Path {...p} d="M8 3v4M16 3v4M3 11h18" /></>)}
      {name === 'bell' && <Path {...p} d="M18 8a6 6 0 1 0-12 0c0 6-2 7-2 7h16s-2-1-2-7M13.7 21a2 2 0 0 1-3.4 0" />}
      {name === 'user' && (<><Circle {...p} cx="12" cy="8" r="4" /><Path {...p} d="M4 21c0-4 3.6-6 8-6s8 2 8 6" /></>)}
      {name === 'scissors' && (<><Circle {...p} cx="6" cy="6" r="2.6" /><Circle {...p} cx="6" cy="18" r="2.6" /><Path {...p} d="M8.1 7.6 20 18M20 6 8.1 16.4" /></>)}
      {name === 'clock' && (<><Circle {...p} cx="12" cy="12" r="8.5" /><Path {...p} d="M12 7.5V12l3 2" /></>)}
      {name === 'mapPin' && (<><Path {...p} d="M20 10.5c0 5.2-8 11-8 11s-8-5.8-8-11a8 8 0 1 1 16 0Z" /><Circle {...p} cx="12" cy="10.5" r="2.8" /></>)}
      {name === 'star' && <Path {...p} d="m12 3.6 2.6 5.3 5.9.9-4.3 4.1 1 5.8-5.2-2.7-5.2 2.7 1-5.8L3.5 9.8l5.9-.9z" />}
      {name === 'chevronRight' && <Path {...p} d="m9 5 7 7-7 7" />}
      {name === 'chevronLeft' && <Path {...p} d="m15 5-7 7 7 7" />}
      {name === 'check' && <Path {...p} d="m4.5 12.5 5 5 10-11" />}
      {name === 'x' && <Path {...p} d="M6 6l12 12M18 6 6 18" />}
      {name === 'plus' && <Path {...p} d="M12 5v14M5 12h14" />}
      {name === 'settings' && (<><Circle {...p} cx="12" cy="12" r="3" /><Path {...p} d="M19.4 15a1.6 1.6 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.6 1.6 0 0 0-2.7 1.1V21a2 2 0 1 1-4 0v-.1A1.6 1.6 0 0 0 7.9 19.4l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1A1.6 1.6 0 0 0 4 13.9H4a2 2 0 1 1 0-4h.1A1.6 1.6 0 0 0 5.1 7.3L5 7.2a2 2 0 1 1 2.8-2.8l.1.1a1.6 1.6 0 0 0 1.8.3H10a1.6 1.6 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.6 1.6 0 0 0 2.7 1.1l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.6 1.6 0 0 0-.3 1.8V10a1.6 1.6 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.6 1.6 0 0 0-1.5 1z" /></>)}
      {name === 'logout' && <Path {...p} d="M15 17l5-5-5-5M20 12H9M13 4H6a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h7" />}
      {name === 'shield' && <Path {...p} d="M12 3.5 5 6.2V11c0 4.6 3 8.2 7 9.5 4-1.3 7-4.9 7-9.5V6.2z" />}
      {name === 'creditCard' && (<><Rect {...p} x="2.5" y="5.5" width="19" height="13" rx="2.5" /><Path {...p} d="M2.5 10h19" /></>)}
      {name === 'phone' && <Path {...p} d="M7 3.5h10a1.5 1.5 0 0 1 1.5 1.5v14a1.5 1.5 0 0 1-1.5 1.5H7A1.5 1.5 0 0 1 5.5 19V5A1.5 1.5 0 0 1 7 3.5ZM11 17.5h2" />}
      {name === 'alert' && (<><Path {...p} d="M12 4.5 2.8 20h18.4z" /><Line {...p} x1="12" y1="10" x2="12" y2="14" /><Circle cx="12" cy="17" r="1" fill={color} /></>)}
      {name === 'info' && (<><Circle {...p} cx="12" cy="12" r="8.5" /><Path {...p} d="M12 11.5V16" /><Circle cx="12" cy="8.4" r="1" fill={color} /></>)}
      {name === 'refresh' && <Path {...p} d="M20 11a8 8 0 1 0-.7 4.5M20 5v6h-6" />}
      {name === 'store' && (<><Path {...p} d="M4 9.5V20a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1V9.5" /><Path {...p} d="M3 9.5 5 4h14l2 5.5a3 3 0 0 1-6 0 3 3 0 0 1-6 0 3 3 0 0 1-6 0Z" /></>)}
      {name === 'users' && (<><Circle {...p} cx="9" cy="8" r="3.4" /><Path {...p} d="M2.5 20c0-3.4 2.9-5.2 6.5-5.2s6.5 1.8 6.5 5.2" /><Path {...p} d="M16.5 5.2a3.4 3.4 0 0 1 0 6.6M18 14.9c2.1.5 3.5 1.9 3.5 4.1" /></>)}
      {name === 'wallet' && (<><Rect {...p} x="3" y="6" width="18" height="13" rx="2.5" /><Path {...p} d="M3 10h18" /><Circle cx="17" cy="14.5" r="1.2" fill={color} /></>)}
      {name === 'lock' && (<><Rect {...p} x="4.5" y="10.5" width="15" height="10" rx="2" /><Path {...p} d="M8 10.5V7.5a4 4 0 0 1 8 0v3" /></>)}
      {name === 'document' && (<><Path {...p} d="M14 3.5H7a1.5 1.5 0 0 0-1.5 1.5v14A1.5 1.5 0 0 0 7 20.5h10a1.5 1.5 0 0 0 1.5-1.5V8z" /><Path {...p} d="M14 3.5V8h4.5M8.5 13h7M8.5 16.5h4" /></>)}
      {name === 'flag' && <Path {...p} d="M5 21V4.5h9l-1 3h6l-1.5 4.5L19 16h-9v5" />}
      {name === 'globe' && (<><Circle {...p} cx="12" cy="12" r="8.5" /><Path {...p} d="M3.5 12h17M12 3.5c2.2 2.4 3.3 5.4 3.3 8.5s-1.1 6.1-3.3 8.5c-2.2-2.4-3.3-5.4-3.3-8.5S9.8 5.9 12 3.5" /></>)}
      {name === 'edit' && <Path {...p} d="M4 20h4L19 9a2.1 2.1 0 0 0-3-3L5 17zM14.5 6.5l3 3" />}
    </Svg>
  );
}
