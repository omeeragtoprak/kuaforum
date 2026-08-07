import { useEffect, useRef, useState } from 'react';
import { View, Text, StyleSheet, ActivityIndicator, Alert, Linking } from 'react-native';
import * as ImagePicker from 'expo-image-picker';
import * as DocumentPicker from 'expo-document-picker';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { Colors, FontSize, FontWeight, Spacing, Layout } from '@/lib/theme';
import { BarberApi } from '@/api/barber';
import { extractApiError } from '@/api/client';
import { Haptics } from '@/lib/haptics';
import { KYC_TYPE_LABELS, KycDocumentType } from '@/lib/kyc';

export type UploadedKycDoc = {
  documentType: KycDocumentType;
  storageKey: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
};

type PickedFile = { uri: string; name: string; contentType: string };

type KycDocumentPickerProps = {
  documentType: KycDocumentType;
  required: boolean;
  value: { storageKey: string; fileName: string } | null;
  onUploaded: (doc: UploadedKycDoc) => void;
  onCleared: () => void;
  /** Yalnız opsiyonel kartlarda: kartı listeden tamamen kaldırır. */
  onRemoveCard?: () => void;
};

/** Kamera izni reddedildiğinde tekrar sorulamıyorsa Ayarlar'a yönlendiren Türkçe uyarı. */
function alertPermissionDenied(target: string, canAskAgain: boolean) {
  Alert.alert(
    'İzin gerekli',
    `Belge yüklemek için ${target} erişimine izin vermelisin.` +
      (canAskAgain ? '' : ' Ayarlar > Kuaforum üzerinden izni açabilirsin.'),
    canAskAgain
      ? [{ text: 'Tamam' }]
      : [{ text: 'Vazgeç', style: 'cancel' }, { text: 'Ayarlara git', onPress: () => Linking.openSettings() }],
  );
}

export function KycDocumentPicker({ documentType, required, value, onUploaded, onCleared, onRemoveCard }: KycDocumentPickerProps) {
  const [status, setStatus] = useState<'idle' | 'uploading' | 'error'>('idle');
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const label = KYC_TYPE_LABELS[documentType];

  // Upload async'i unmount sonrası dönerse (kullanıcı ekrandan hızlı çıkarsa)
  // setState çağırmayı engelleyen guard — "Can't perform a React state update
  // on an unmounted component" uyarısı ve olası bellek sızıntısı önlenir.
  const mountedRef = useRef(true);
  useEffect(() => () => { mountedRef.current = false; }, []);

  async function upload(picked: PickedFile) {
    setStatus('uploading');
    setErrorMsg(null);
    try {
      const res = await BarberApi.uploadKycDocument(picked);
      if (!mountedRef.current) return;
      setStatus('idle');
      Haptics.success();
      onUploaded({
        documentType,
        storageKey: res.storageKey,
        fileName: picked.name,
        contentType: res.contentType,
        sizeBytes: res.sizeBytes,
      });
    } catch (err) {
      if (!mountedRef.current) return;
      Haptics.error();
      setStatus('error');
      setErrorMsg(extractApiError(err));
    }
  }

  async function pickFromCamera() {
    try {
      const perm = await ImagePicker.requestCameraPermissionsAsync();
      if (perm.status !== 'granted') return alertPermissionDenied('kamera', perm.canAskAgain);
      const result = await ImagePicker.launchCameraAsync({ mediaTypes: ['images'], quality: 0.7 });
      if (result.canceled || !result.assets?.[0]) return;
      const asset = result.assets[0];
      await upload({
        uri: asset.uri,
        name: asset.fileName ?? `${documentType.toLowerCase()}-${Date.now()}.jpg`,
        contentType: asset.mimeType ?? 'image/jpeg',
      });
    } catch {
      if (!mountedRef.current) return;
      Haptics.error();
      Alert.alert('Kamera açılamadı', 'Kamera şu anda kullanılamıyor. Lütfen tekrar dene.');
    }
  }

  async function pickFromGallery() {
    try {
      const perm = await ImagePicker.requestMediaLibraryPermissionsAsync();
      if (perm.status !== 'granted') return alertPermissionDenied('galeri', perm.canAskAgain);
      const result = await ImagePicker.launchImageLibraryAsync({ mediaTypes: ['images'], quality: 0.7 });
      if (result.canceled || !result.assets?.[0]) return;
      const asset = result.assets[0];
      await upload({
        uri: asset.uri,
        name: asset.fileName ?? `${documentType.toLowerCase()}-${Date.now()}.jpg`,
        contentType: asset.mimeType ?? 'image/jpeg',
      });
    } catch {
      if (!mountedRef.current) return;
      Haptics.error();
      Alert.alert('Galeri açılamadı', 'Galeriye şu anda erişilemiyor. Lütfen tekrar dene.');
    }
  }

  async function pickPdf() {
    try {
      const result = await DocumentPicker.getDocumentAsync({ type: 'application/pdf', copyToCacheDirectory: true });
      if (result.canceled || !result.assets?.[0]) return;
      const asset = result.assets[0];
      await upload({
        uri: asset.uri,
        name: asset.name ?? `${documentType.toLowerCase()}.pdf`,
        contentType: asset.mimeType ?? 'application/pdf',
      });
    } catch {
      if (!mountedRef.current) return;
      Haptics.error();
      Alert.alert('Dosya seçilemedi', 'PDF seçici açılamadı. Lütfen tekrar dene.');
    }
  }

  const statusText = status === 'uploading' ? 'yükleniyor' : value ? 'yüklendi' : status === 'error' ? 'hata' : 'boş';

  return (
    <Card style={styles.card} testID={`kyc-picker-${documentType}`}>
      <View
        style={styles.header}
        accessible
        accessibilityLabel={`${label} belgesi, durum: ${statusText}${required ? ', zorunlu' : ', opsiyonel'}`}
      >
        <Text style={styles.title}>
          {label}
          {required ? <Text style={styles.required}> *</Text> : null}
        </Text>
        {onRemoveCard && !value && status !== 'uploading' ? (
          <Button label="Kaldır" variant="ghost" size="sm" fullWidth={false} onPress={onRemoveCard} />
        ) : null}
      </View>

      {status === 'uploading' ? (
        // G15: bu kart tek bir belge yükleme aksiyonunu temsil ediyor (sabit, küçük
        // boyut) — bir skeleton placeholder burada abartı olur, gösterilecek hedef
        // "şekil" zaten sabit. ActivityIndicator + metin zaten tutarlı bir yükleme
        // göstergesi (düz metin DEĞİL); bu yüzden aynen korunuyor.
        <View style={styles.centerRow}>
          <ActivityIndicator color={Colors.primary} />
          <Text style={styles.hint}>Yükleniyor…</Text>
        </View>
      ) : value ? (
        <View style={styles.row}>
          <Text style={styles.fileName} numberOfLines={1}>📎 {value.fileName}</Text>
          <View style={styles.actions}>
            <Button label="Değiştir" variant="outline" size="sm" fullWidth={false} onPress={onCleared} style={styles.actionBtn} />
            {onRemoveCard ? (
              <Button label="Kaldır" variant="ghost" size="sm" fullWidth={false} onPress={onRemoveCard} style={styles.actionBtn} />
            ) : null}
          </View>
        </View>
      ) : (
        <>
          {errorMsg ? <Text style={styles.error}>⚠️ {errorMsg} — tekrar dene.</Text> : null}
          <View style={styles.row}>
            <Button label="📷 Kamera" variant="outline" size="sm" fullWidth={false} onPress={pickFromCamera} style={styles.actionBtn} />
            <Button label="🖼️ Galeri" variant="outline" size="sm" fullWidth={false} onPress={pickFromGallery} style={styles.actionBtn} />
            <Button label="📄 PDF" variant="outline" size="sm" fullWidth={false} onPress={pickPdf} style={styles.actionBtn} />
          </View>
        </>
      )}
    </Card>
  );
}

const styles = StyleSheet.create({
  card: { gap: Spacing.sm },
  header: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  title: { fontSize: FontSize.sm, fontWeight: FontWeight.semibold, color: Colors.text },
  required: { color: Colors.error },
  row: { flexDirection: 'row', alignItems: 'center', gap: Spacing.sm, flexWrap: 'wrap' },
  centerRow: { flexDirection: 'row', alignItems: 'center', gap: Spacing.sm, minHeight: Layout.tapTargetMin },
  actions: { flexDirection: 'row', gap: Spacing.sm },
  actionBtn: { flex: 1, minHeight: Layout.tapTargetMin },
  fileName: { flex: 1, fontSize: FontSize.sm, color: Colors.text },
  hint: { fontSize: FontSize.sm, color: Colors.textSecondary },
  error: { fontSize: FontSize.xs, color: Colors.error, lineHeight: 18 },
});
