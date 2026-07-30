import { useState } from 'react';
import { View, Text, StyleSheet, Modal, Pressable } from 'react-native';
import { Colors, FontSize, FontWeight, Spacing } from '@/lib/theme';
import { KYC_TYPE_LABELS, OPTIONAL_KYC_TYPES, REQUIRED_KYC_TYPES, KycDocumentType } from '@/lib/kyc';
import { KycDocumentPicker, UploadedKycDoc } from './KycDocumentPicker';

type KycDocumentSectionProps = {
  docs: UploadedKycDoc[];
  onChange: (docs: UploadedKycDoc[]) => void;
};

/**
 * Zorunlu (TaxLicense + IdentityCard) + opsiyonel (SanitaryPermit/MykCertificate/Other) KYC
 * belge kartlarının toplandığı bölüm. Tek kaynak `docs` state'i kyc.tsx'te tutulur;
 * bu bileşen yalnız kart açma/kapama + upload event'lerini yukarı iletir.
 */
export function KycDocumentSection({ docs, onChange }: KycDocumentSectionProps) {
  const [optionalSlots, setOptionalSlots] = useState<KycDocumentType[]>(
    () => docs.map((d) => d.documentType).filter((t) => OPTIONAL_KYC_TYPES.includes(t)),
  );
  const [pickerOpen, setPickerOpen] = useState(false);

  const availableToAdd = OPTIONAL_KYC_TYPES.filter((t) => !optionalSlots.includes(t));

  function findDoc(type: KycDocumentType) {
    return docs.find((d) => d.documentType === type) ?? null;
  }

  function handleUploaded(doc: UploadedKycDoc) {
    onChange([...docs.filter((d) => d.documentType !== doc.documentType), doc]);
  }

  function handleCleared(type: KycDocumentType) {
    onChange(docs.filter((d) => d.documentType !== type));
  }

  function handleAddSlot(type: KycDocumentType) {
    setOptionalSlots((prev) => [...prev, type]);
    setPickerOpen(false);
  }

  function handleRemoveSlot(type: KycDocumentType) {
    setOptionalSlots((prev) => prev.filter((t) => t !== type));
    onChange(docs.filter((d) => d.documentType !== type));
  }

  return (
    <View style={styles.container}>
      {REQUIRED_KYC_TYPES.map((type) => (
        <KycDocumentPicker
          key={type}
          documentType={type}
          required
          value={findDoc(type)}
          onUploaded={handleUploaded}
          onCleared={() => handleCleared(type)}
        />
      ))}

      {optionalSlots.map((type) => (
        <KycDocumentPicker
          key={type}
          documentType={type}
          required={false}
          value={findDoc(type)}
          onUploaded={handleUploaded}
          onCleared={() => handleCleared(type)}
          onRemoveCard={() => handleRemoveSlot(type)}
        />
      ))}

      {availableToAdd.length > 0 ? (
        <Pressable
          onPress={() => setPickerOpen(true)}
          style={styles.addRow}
          accessibilityRole="button"
          accessibilityLabel="Ek belge ekle"
        >
          <Text style={styles.addText}>＋ Ek belge</Text>
        </Pressable>
      ) : null}

      <Modal visible={pickerOpen} transparent animationType="fade" onRequestClose={() => setPickerOpen(false)}>
        <Pressable style={styles.overlay} onPress={() => setPickerOpen(false)}>
          <Pressable style={styles.sheet} onPress={(e) => e.stopPropagation()}>
            <Text style={styles.sheetTitle}>Belge tipi seç</Text>
            {availableToAdd.map((type) => (
              <Pressable
                key={type}
                style={styles.option}
                onPress={() => handleAddSlot(type)}
                accessibilityRole="button"
                accessibilityLabel={KYC_TYPE_LABELS[type]}
              >
                <Text style={styles.optionText}>{KYC_TYPE_LABELS[type]}</Text>
              </Pressable>
            ))}
          </Pressable>
        </Pressable>
      </Modal>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { gap: Spacing.md },
  addRow: {
    paddingVertical: Spacing.md,
    alignItems: 'center',
    borderWidth: 1,
    borderStyle: 'dashed',
    borderColor: Colors.borderStrong,
    borderRadius: 14,
    minHeight: 44,
    justifyContent: 'center',
  },
  addText: { color: Colors.primary, fontWeight: FontWeight.semibold, fontSize: FontSize.sm },
  overlay: { flex: 1, backgroundColor: Colors.overlay, justifyContent: 'center', padding: Spacing.xl },
  sheet: { backgroundColor: Colors.surface, borderRadius: 20, padding: Spacing.lg, gap: Spacing.xs },
  sheetTitle: { fontSize: FontSize.lg, fontWeight: FontWeight.bold, color: Colors.text, marginBottom: Spacing.sm },
  option: { paddingVertical: Spacing.md, minHeight: 44, justifyContent: 'center' },
  optionText: { fontSize: FontSize.md, color: Colors.text },
});
