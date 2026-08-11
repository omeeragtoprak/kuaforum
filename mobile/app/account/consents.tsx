import { View, Text, StyleSheet, ScrollView, Alert, Pressable, Switch, RefreshControl } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card } from '@/components/Card';
import { EmptyState } from '@/components/EmptyState';
import { SkeletonList, SkeletonBookingRow } from '@/components/Skeleton';
import { Colors, FontSize, FontWeight, Spacing, Layout } from '@/lib/theme';
import { AuthApi, Consent, ConsentPurposeKey } from '@/api/auth';
import { extractApiError } from '@/api/client';
import { Haptics } from '@/lib/haptics';

const CONSENT_META: Record<ConsentPurposeKey, { title: string; description: string; revocable: boolean }> = {
  TermsOfService: {
    title: 'Hizmet Şartları',
    description:
      'Uygulamanın temel kullanım koşulları. Hesap silinmeden geri alınamaz — KVKK m.7 gereği veri silme akışından geçmen gerekir.',
    revocable: false,
  },
  Marketing: {
    title: 'Pazarlama bildirimleri',
    description: 'Kampanya/indirim e-postası ve push bildirimi alabilmemiz için izin. İstediğin zaman kapatabilirsin.',
    revocable: true,
  },
  Location: {
    title: 'Konum verisi',
    description: 'Yakındaki kuaförleri sıralamak için konumunu kullanabilmemiz. Cihaz iznine ek bir KVKK rızasıdır.',
    revocable: true,
  },
  HealthData: {
    title: 'Saç tipi / hassasiyet bilgisi',
    description: 'Stilistlere alerjik tepki gibi hassas bilgileri iletebilmek için. Hassas veri kategorisi (KVKK m.6).',
    revocable: true,
  },
};

const PURPOSE_KEY: Record<number, ConsentPurposeKey> = {
  0: 'TermsOfService',
  1: 'Marketing',
  2: 'Location',
  3: 'HealthData',
};

function purposeKey(p: ConsentPurposeKey | number): ConsentPurposeKey {
  return typeof p === 'string' ? p : (PURPOSE_KEY[p] ?? 'Marketing');
}

export default function ConsentsScreen() {
  const router = useRouter();
  const qc = useQueryClient();
  const consentsQuery = useQuery({
    queryKey: ['my-consents'],
    queryFn: () => AuthApi.getMyConsents(),
  });

  const updateMut = useMutation({
    mutationFn: (args: { purpose: ConsentPurposeKey; grant: boolean }) =>
      AuthApi.updateConsent(args.purpose, args.grant),
    onSuccess: () => {
      Haptics.success();
      qc.invalidateQueries({ queryKey: ['my-consents'] });
    },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });

  function handleToggle(c: Consent, value: boolean) {
    const key = purposeKey(c.purpose);
    const meta = CONSENT_META[key];
    if (!value && !meta.revocable) {
      Alert.alert(
        'Geri alınamaz',
        `${meta.title} rızasını uygulama içinde kapatamazsın. Hesabını silersen veriler 30 gün sonra anonimleştirilir.`,
        [{ text: 'Tamam', style: 'cancel' }],
      );
      return;
    }
    if (!value) {
      Alert.alert(
        `${meta.title} kapat`,
        meta.description + '\n\nKapatmak istediğine emin misin?',
        [
          { text: 'Vazgeç', style: 'cancel' },
          { text: 'Kapat', style: 'destructive', onPress: () => updateMut.mutate({ purpose: key, grant: false }) },
        ],
      );
    } else {
      updateMut.mutate({ purpose: key, grant: true });
    }
  }

  // Tüm 4 amaç için satır göster — backend hiç vermemişse de "kapalı" göründüğü için.
  const consentByPurpose = new Map<ConsentPurposeKey, Consent>();
  (consentsQuery.data ?? []).forEach((c) => consentByPurpose.set(purposeKey(c.purpose), c));

  const allPurposes: ConsentPurposeKey[] = ['TermsOfService', 'Marketing', 'Location', 'HealthData'];

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <View style={styles.header}>
        <Pressable onPress={() => router.back()} style={styles.backBtn}>
          <Text style={styles.back}>← Geri</Text>
        </Pressable>
        <Text style={styles.title}>Gizlilik & Rızalar</Text>
        <Text style={styles.subtitle}>
          KVKK kapsamında veri işleme rızalarını buradan yönetebilirsin. Marketing/Location/Health
          rızalarını her an kapatabilirsin; Hizmet Şartları için hesap silmen gerekir.
        </Text>
      </View>

      <ScrollView
        contentContainerStyle={styles.scroll}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl
            refreshing={consentsQuery.isFetching && !consentsQuery.isLoading}
            onRefresh={() => consentsQuery.refetch()}
            tintColor={Colors.primary}
          />
        }
      >
        {consentsQuery.isLoading ? (
          // G11/G12 fix: eskiden yükleme sırasında bu dal hiç kontrol edilmiyordu — sunucudan
          // henüz veri gelmeden 4 rıza da "kapalı" olarak render ediliyordu. Bu bir gizlilik/rıza
          // ekranında YANLIŞ bir sinyal: kullanıcı gerçekte açık olan bir rızayı anlık da olsa
          // kapalı görebiliyordu. Artık gerçek veri gelene kadar iskelet gösteriliyor.
          <SkeletonList count={4} render={() => <SkeletonBookingRow />} />
        ) : consentsQuery.isError ? (
          <EmptyState
            emoji="⚠️"
            title="Bağlantı hatası"
            description="Rızalar yüklenemedi."
            actionLabel="Tekrar dene"
            onAction={() => consentsQuery.refetch()}
          />
        ) : (
          allPurposes.map((key) => {
            const meta = CONSENT_META[key];
            const c = consentByPurpose.get(key);
            const granted = !!(c && c.granted && !c.revokedAt);
            return (
              <Card key={key} style={{ gap: Spacing.sm }}>
                <View style={styles.row}>
                  <View style={{ flex: 1 }}>
                    <Text style={styles.consentTitle}>{meta.title}</Text>
                    <Text style={styles.consentDesc}>{meta.description}</Text>
                    {c?.grantedAt ? (
                      <Text style={styles.consentMeta}>
                        Versiyon {c.version} · {granted ? 'verildi' : 'iptal edildi'} · {new Date(c.grantedAt).toLocaleDateString('tr-TR')}
                      </Text>
                    ) : (
                      <Text style={styles.consentMeta}>Henüz kayıt yok</Text>
                    )}
                  </View>
                  <Switch
                    value={granted}
                    onValueChange={(v) => handleToggle(c ?? { purpose: key, granted: false, version: '1.0', grantedAt: '', revokedAt: null }, v)}
                    disabled={updateMut.isPending}
                    trackColor={{ true: Colors.success, false: '#E5E7EB' }}
                  />
                </View>
              </Card>
            );
          })
        )}
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: { paddingHorizontal: Layout.screenPaddingX, paddingTop: Spacing.md, paddingBottom: Spacing.sm },
  backBtn: { alignSelf: 'flex-start', paddingVertical: 4 },
  back: { color: Colors.primary, fontSize: FontSize.sm, fontWeight: FontWeight.medium },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text, marginTop: 6 },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 6, lineHeight: 20 },
  scroll: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingBottom: Spacing.xxl,
    gap: Spacing.md,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
    flexGrow: 1,
  },
  row: { flexDirection: 'row', alignItems: 'flex-start', gap: Spacing.md },
  consentTitle: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, color: Colors.text },
  consentDesc: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 4, lineHeight: 18 },
  consentMeta: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 6 },
});
