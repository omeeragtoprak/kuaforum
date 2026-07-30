import { useMemo, useState } from 'react';
import { View, Text, StyleSheet, Modal, Pressable, FlatList, TextInput } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Colors, FontSize, FontWeight, Radius, Spacing, Layout } from '@/lib/theme';
import { normalizeTr } from '@/lib/format';
import { EmptyState } from '@/components/EmptyState';

export type GeoPickerItem = { id: string; name: string };

type GeoPickerModalProps = {
  visible: boolean;
  title: string;
  items: GeoPickerItem[];
  loading?: boolean;
  selectedId: string | null;
  onClose: () => void;
  onPick: (item: GeoPickerItem) => void;
  emptyLabel: string;
};

/**
 * Adres adımında il/ilçe/mahalle seçimi için tek genel modal — keşfet ekranındaki
 * CityPickerModal kalıbının (arama + liste + seçili vurgusu) genelleştirilmiş hali.
 */
export function GeoPickerModal({ visible, title, items, loading, selectedId, onClose, onPick, emptyLabel }: GeoPickerModalProps) {
  const [query, setQuery] = useState('');

  const filtered = useMemo(() => {
    const q = query.trim() ? normalizeTr(query.trim()) : null;
    const list = q ? items.filter((c) => normalizeTr(c.name).includes(q)) : items;
    return [...list].sort((a, b) => a.name.localeCompare(b.name, 'tr'));
  }, [items, query]);

  return (
    <Modal visible={visible} animationType="slide" presentationStyle="pageSheet" onRequestClose={onClose}>
      <SafeAreaView style={styles.container} edges={['top']}>
        <View style={styles.header}>
          <Pressable onPress={onClose} hitSlop={8} accessibilityRole="button" accessibilityLabel="Kapat">
            <Text style={styles.close}>Kapat</Text>
          </Pressable>
          <Text style={styles.title}>{title}</Text>
          <View style={{ width: 50 }} />
        </View>
        <View style={styles.searchBox}>
          <Text style={styles.searchIcon}>🔍</Text>
          <TextInput
            value={query}
            onChangeText={setQuery}
            placeholder="Ara…"
            placeholderTextColor={Colors.textMuted}
            style={styles.searchInput}
            autoFocus
            returnKeyType="search"
          />
          {query ? (
            <Pressable onPress={() => setQuery('')} hitSlop={8} accessibilityRole="button" accessibilityLabel="Aramayı temizle">
              <Text style={styles.clearIcon}>✕</Text>
            </Pressable>
          ) : null}
        </View>
        <FlatList
          data={filtered}
          keyExtractor={(c) => c.id}
          keyboardShouldPersistTaps="handled"
          contentContainerStyle={styles.list}
          ItemSeparatorComponent={() => <View style={{ height: 4 }} />}
          ListEmptyComponent={
            loading ? null : (
              <EmptyState emoji="🔍" title={emptyLabel} />
            )
          }
          renderItem={({ item }) => {
            const active = item.id === selectedId;
            return (
              <Pressable
                onPress={() => onPick(item)}
                style={({ pressed }) => [styles.row, active && styles.rowActive, pressed && styles.rowPressed]}
                accessibilityRole="radio"
                accessibilityState={{ checked: active, selected: active }}
                accessibilityLabel={item.name}
              >
                <Text style={[styles.rowText, active && styles.rowTextActive]}>{item.name}</Text>
                {active ? <Text style={styles.check}>✓</Text> : null}
              </Pressable>
            );
          }}
        />
      </SafeAreaView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: {
    flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between',
    paddingHorizontal: Layout.screenPaddingX, paddingVertical: Spacing.md,
    borderBottomWidth: 1, borderBottomColor: Colors.border,
  },
  close: { fontSize: FontSize.md, color: Colors.primary, fontWeight: FontWeight.semibold },
  title: { fontSize: FontSize.lg, fontWeight: FontWeight.bold, color: Colors.text },
  searchBox: {
    flexDirection: 'row', alignItems: 'center', backgroundColor: Colors.surface,
    borderRadius: Radius.full, paddingHorizontal: Spacing.lg, minHeight: 48,
    borderWidth: 1, borderColor: Colors.border,
    marginHorizontal: Layout.screenPaddingX, marginVertical: Spacing.md, gap: 8,
  },
  searchIcon: { fontSize: 16 },
  searchInput: { flex: 1, fontSize: FontSize.md, color: Colors.text, paddingVertical: 10 },
  clearIcon: { color: Colors.textMuted, fontSize: 16, paddingHorizontal: 4 },
  list: { paddingHorizontal: Layout.screenPaddingX, paddingBottom: Spacing.xl },
  row: {
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center',
    backgroundColor: Colors.surface, borderRadius: Radius.md, padding: Spacing.md,
    borderWidth: 1, borderColor: Colors.border, minHeight: Layout.tapTargetMin,
  },
  rowActive: { borderColor: Colors.primary, backgroundColor: Colors.primarySoft },
  rowPressed: { opacity: 0.85 },
  rowText: { fontSize: FontSize.md, fontWeight: FontWeight.medium, color: Colors.text },
  rowTextActive: { color: Colors.primary, fontWeight: FontWeight.semibold },
  check: { color: Colors.primary, fontWeight: FontWeight.bold },
});
