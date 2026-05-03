import { useState } from 'react';
import { View, Text, StyleSheet, FlatList, Modal, Alert, KeyboardAvoidingView, Platform, Pressable, ScrollView } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { EmptyState } from '@/components/EmptyState';
import { Colors, FontSize, Radius, Spacing } from '@/lib/theme';
import { BarberApi } from '@/api/barber';
import { extractApiError } from '@/api/client';
import { formatDuration, formatTry } from '@/lib/format';

const GENDER_OPTIONS = [
  { value: 0, label: 'Herkes' },
  { value: 1, label: 'Sadece Kadın' },
  { value: 2, label: 'Sadece Erkek' },
];

export default function BarberServicesScreen() {
  const qc = useQueryClient();
  const shopQuery = useQuery({
    queryKey: ['my-shop'],
    queryFn: () => BarberApi.getMyShop(),
  });
  const servicesQuery = useQuery({
    queryKey: ['my-shop-services', shopQuery.data?.id],
    queryFn: () => BarberApi.getServices(shopQuery.data!.id),
    enabled: !!shopQuery.data?.id,
  });

  const [modalOpen, setModalOpen] = useState(false);
  const [name, setName] = useState('');
  const [gender, setGender] = useState(0);
  const [duration, setDuration] = useState('30');
  const [priceTl, setPriceTl] = useState('300');
  const [buffer, setBuffer] = useState('5');
  const [description, setDescription] = useState('');

  const createMut = useMutation({
    mutationFn: () => {
      const dur = parseInt(duration, 10);
      const buf = parseInt(buffer, 10);
      const priceMinor = Math.round(parseFloat(priceTl) * 100);
      return BarberApi.createService(shopQuery.data!.id, {
        name: name.trim(),
        gender,
        durationMinutes: dur,
        priceMinor,
        bufferMinutes: buf,
        description: description || undefined,
      });
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['my-shop-services'] });
      setModalOpen(false);
      setName(''); setDescription(''); setDuration('30'); setPriceTl('300'); setBuffer('5');
    },
    onError: (err) => Alert.alert('Hata', extractApiError(err)),
  });

  if (!shopQuery.data && !shopQuery.isLoading) {
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <EmptyState
          title="İşletme yok"
          description="Önce bir BarberShop oluşturmalısın. Şimdilik web'den /v1/owner/barber-shop POST ile yapabilirsin."
        />
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <View style={styles.header}>
        <View>
          <Text style={styles.title}>Hizmetler</Text>
          <Text style={styles.subtitle}>{servicesQuery.data?.length ?? 0} aktif</Text>
        </View>
        <Pressable style={styles.addBtn} onPress={() => setModalOpen(true)}>
          <Text style={styles.addBtnText}>+ Ekle</Text>
        </Pressable>
      </View>

      {servicesQuery.isLoading ? (
        <EmptyState title="Yükleniyor..." />
      ) : (servicesQuery.data?.length ?? 0) === 0 ? (
        <EmptyState title="Henüz hizmet yok" description='"+Ekle" ile başlat.' />
      ) : (
        <FlatList
          data={servicesQuery.data}
          keyExtractor={(s) => s.id}
          contentContainerStyle={styles.list}
          renderItem={({ item }) => (
            <Card style={styles.serviceCard}>
              <View style={styles.headerRow}>
                <Text style={styles.serviceName}>{item.name}</Text>
                <Text style={styles.servicePrice}>{formatTry(item.priceMinor)}</Text>
              </View>
              <Text style={styles.serviceMeta}>
                {formatDuration(item.durationMinutes)} · +{item.bufferMinutes} dk buffer ·{' '}
                {item.gender === 0 ? 'Herkes' : item.gender === 1 ? 'Kadın' : 'Erkek'}
              </Text>
            </Card>
          )}
        />
      )}

      <Modal visible={modalOpen} transparent animationType="slide" onRequestClose={() => setModalOpen(false)}>
        <KeyboardAvoidingView
          behavior={Platform.OS === 'ios' ? 'padding' : undefined}
          style={styles.modalOverlay}
        >
          <View style={styles.modalSheet}>
            <ScrollView contentContainerStyle={styles.modalContent}>
              <Text style={styles.modalTitle}>Yeni hizmet</Text>

              <Input label="Hizmet adı" value={name} onChangeText={setName} placeholder="Saç Kesim" maxLength={80} />

              <Text style={styles.label}>Cinsiyet</Text>
              <View style={styles.genderRow}>
                {GENDER_OPTIONS.map((g) => (
                  <Pressable
                    key={g.value}
                    style={[styles.pill, gender === g.value && styles.pillActive]}
                    onPress={() => setGender(g.value)}
                  >
                    <Text style={[styles.pillText, gender === g.value && styles.pillTextActive]}>{g.label}</Text>
                  </Pressable>
                ))}
              </View>

              <View style={styles.row2}>
                <View style={{ flex: 1 }}>
                  <Input label="Süre (dakika)" value={duration} onChangeText={setDuration} keyboardType="number-pad" />
                </View>
                <View style={{ flex: 1 }}>
                  <Input label="Buffer (dakika)" value={buffer} onChangeText={setBuffer} keyboardType="number-pad" />
                </View>
              </View>

              <Input label="Fiyat (TL)" value={priceTl} onChangeText={setPriceTl} keyboardType="decimal-pad" />
              <Input label="Açıklama (opsiyonel)" value={description} onChangeText={setDescription} multiline numberOfLines={3} maxLength={500} />

              <View style={styles.modalActions}>
                <Button label="Vazgeç" variant="secondary" onPress={() => setModalOpen(false)} />
                <Button label="Oluştur" onPress={() => createMut.mutate()} loading={createMut.isPending} disabled={!name.trim()} />
              </View>
            </ScrollView>
          </View>
        </KeyboardAvoidingView>
      </Modal>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: {
    padding: Spacing.lg,
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  title: { fontSize: FontSize.xxl, fontWeight: '700', color: Colors.text },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: Spacing.xs },
  addBtn: {
    backgroundColor: Colors.primary,
    paddingHorizontal: Spacing.md,
    paddingVertical: Spacing.sm,
    borderRadius: Radius.md,
  },
  addBtnText: { color: '#fff', fontSize: FontSize.sm, fontWeight: '600' },
  list: { padding: Spacing.lg, paddingTop: 0, gap: Spacing.sm },
  serviceCard: { gap: Spacing.xs },
  headerRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  serviceName: { fontSize: FontSize.md, fontWeight: '600', color: Colors.text },
  servicePrice: { fontSize: FontSize.md, fontWeight: '700', color: Colors.primary },
  serviceMeta: { fontSize: FontSize.xs, color: Colors.textMuted },
  modalOverlay: {
    flex: 1,
    backgroundColor: 'rgba(0,0,0,0.5)',
    justifyContent: 'flex-end',
  },
  modalSheet: {
    backgroundColor: Colors.background,
    borderTopLeftRadius: 20,
    borderTopRightRadius: 20,
    maxHeight: '90%',
  },
  modalContent: { padding: Spacing.lg, gap: Spacing.sm },
  modalTitle: { fontSize: FontSize.xl, fontWeight: '700', color: Colors.text, marginBottom: Spacing.md },
  label: { fontSize: FontSize.sm, color: Colors.textSecondary, fontWeight: '500', marginBottom: Spacing.xs },
  genderRow: { flexDirection: 'row', gap: Spacing.sm, marginBottom: Spacing.md },
  pill: {
    paddingVertical: Spacing.sm,
    paddingHorizontal: Spacing.md,
    borderRadius: Radius.full,
    borderWidth: 1,
    borderColor: Colors.border,
    backgroundColor: Colors.surface,
  },
  pillActive: { backgroundColor: Colors.primary, borderColor: Colors.primary },
  pillText: { fontSize: FontSize.sm, color: Colors.text },
  pillTextActive: { color: '#fff', fontWeight: '600' },
  row2: { flexDirection: 'row', gap: Spacing.sm },
  modalActions: { flexDirection: 'row', gap: Spacing.sm, marginTop: Spacing.lg },
});
