import { useMemo } from 'react';
import { View, Text, StyleSheet, ScrollView, Pressable } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useQuery } from '@tanstack/react-query';
import { Colors, FontSize, FontWeight, LetterSpacing, Spacing, Radius, Layout } from '@/lib/theme';
import { LegalApi, LegalDocSlug } from '@/api/legal';
import { extractApiError } from '@/api/client';
import { EmptyState } from '@/components/EmptyState';
import { Icon } from '@/components/Icon';
import { Skeleton } from '@/components/Skeleton';

const VALID_SLUGS: LegalDocSlug[] = ['aydinlatma', 'kullanim-kosullari'];

type MarkdownBlock =
  | { type: 'h1'; text: string }
  | { type: 'h2'; text: string }
  | { type: 'warning'; text: string }
  | { type: 'p'; text: string };

/**
 * RFC-008 — kütüphane EKLEME kararı: markdown satır bazlı işlenir (tam parser değil).
 * `#`/`##` başlık, `> ` uyarı kutusu (placeholder uyarıları GÖRÜNÜR olmalı — hukukçu
 * metni gelene kadar founder'ın "canlıya çıkma" launch-checklist sinyali budur), diğer
 * satırlar paragraf. Ardışık aynı tip satırlar (örn. çok satırlı `>` uyarısı) tek bloğa
 * birleştirilir — aksi halde art arda kopuk kutucuklar UX'i bozar.
 * `**bold**` işaretleri strip edilir (gerçek bold render YOK — literal yıldız görünmesin
 * yeter). Kalan renderer sınırları (###, liste, gerçek bold/italik) docs/GAP-ANALYSIS.md
 * P2 backlog'unda — hukukçu metni geldiğinde markdown kapsamı metne göre gözden geçirilir.
 */
function parseMarkdown(markdown: string): MarkdownBlock[] {
  const lines = markdown
    .split('\n')
    .map((line) => line.trim().replace(/\*\*(.+?)\*\*/g, '$1'))
    .filter((line) => line.length > 0);

  const blocks: MarkdownBlock[] = [];
  for (const line of lines) {
    let block: MarkdownBlock;
    if (line.startsWith('## ')) block = { type: 'h2', text: line.slice(3).trim() };
    else if (line.startsWith('# ')) block = { type: 'h1', text: line.slice(2).trim() };
    else if (line.startsWith('> ')) block = { type: 'warning', text: line.slice(2).trim() };
    else block = { type: 'p', text: line };

    const last = blocks[blocks.length - 1];
    if (last && last.type === block.type && (block.type === 'warning' || block.type === 'p')) {
      last.text = `${last.text} ${block.text}`;
    } else {
      blocks.push(block);
    }
  }
  return blocks;
}

export default function LegalDocScreen() {
  const params = useLocalSearchParams<{ doc: string }>();
  const router = useRouter();
  const doc = params.doc ?? '';
  const isValidSlug = (VALID_SLUGS as string[]).includes(doc);

  const query = useQuery({
    queryKey: ['legal-doc', doc],
    queryFn: () => LegalApi.get(doc as LegalDocSlug),
    enabled: isValidSlug,
  });

  const blocks = useMemo(() => (query.data ? parseMarkdown(query.data.markdown) : []), [query.data]);

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <View style={styles.header}>
        <Pressable
          // Deep-link/ilk rota olarak açıldığında history boş olabilir — geri düşecek
          // yer yoksa login'e dön (RFC-008 K2 review M-3).
          onPress={() => (router.canGoBack() ? router.back() : router.replace('/(auth)/login' as never))}
          style={styles.backBtn}
          accessibilityRole="button"
          accessibilityLabel="Geri"
          hitSlop={8}
        >
          <Icon name="chevronLeft" size={18} color={Colors.ink} strokeWidth={2} />
          <Text style={styles.back}>Geri</Text>
        </Pressable>
      </View>

      {!isValidSlug ? (
        <EmptyState
          icon="alert"
          title="Belge bulunamadı"
          description="Bu bağlantı geçersiz görünüyor."
        />
      ) : query.isLoading ? (
        <View style={styles.scroll}>
          <Skeleton width="70%" height={28} />
          <View style={{ height: Spacing.lg }} />
          <Skeleton width="100%" height={14} />
          <View style={{ height: Spacing.sm }} />
          <Skeleton width="100%" height={14} />
          <View style={{ height: Spacing.sm }} />
          <Skeleton width="85%" height={14} />
          <View style={{ height: Spacing.lg }} />
          <Skeleton width="50%" height={20} />
        </View>
      ) : query.isError ? (
        <EmptyState
          icon="alert"
          title="Belge yüklenemedi"
          description={extractApiError(query.error)}
          actionLabel="Tekrar dene"
          onAction={() => query.refetch()}
        />
      ) : (
        <ScrollView contentContainerStyle={styles.scroll} showsVerticalScrollIndicator={false}>
          <Text style={styles.version}>Sürüm {query.data?.version}</Text>
          {blocks.map((block, i) => {
            if (block.type === 'h1') {
              return (
                <Text key={i} style={styles.h1}>
                  {block.text}
                </Text>
              );
            }
            if (block.type === 'h2') {
              return (
                <Text key={i} style={styles.h2}>
                  {block.text}
                </Text>
              );
            }
            if (block.type === 'warning') {
              return (
                <View key={i} style={styles.warningBox} accessibilityRole="text">
                  <Icon name="alert" size={16} color={Colors.warning} strokeWidth={1.8} />
                  <Text style={styles.warningText}>{block.text}</Text>
                </View>
              );
            }
            return (
              <Text key={i} style={styles.paragraph}>
                {block.text}
              </Text>
            );
          })}
        </ScrollView>
      )}
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: { paddingHorizontal: Layout.screenPaddingX, paddingTop: Spacing.sm },
  backBtn: {
    flexDirection: 'row', alignItems: 'center', gap: 2,
    alignSelf: 'flex-start', paddingVertical: Spacing.sm, marginLeft: -4,
    minHeight: Layout.tapTargetMin, justifyContent: 'center',
  },
  back: { color: Colors.ink, fontSize: FontSize.sm, fontWeight: FontWeight.medium },
  scroll: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingTop: Spacing.sm,
    paddingBottom: Spacing.xxl,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  version: { fontSize: FontSize.xs, color: Colors.textMuted, marginBottom: Spacing.md },
  h1: {
    fontSize: FontSize.xxl,
    fontWeight: FontWeight.bold,
    color: Colors.text,
    marginBottom: Spacing.md,
    letterSpacing: LetterSpacing.title,
  },
  h2: {
    fontSize: FontSize.lg,
    fontWeight: FontWeight.semibold,
    color: Colors.text,
    marginTop: Spacing.lg,
    marginBottom: Spacing.sm,
  },
  paragraph: {
    fontSize: FontSize.sm,
    color: Colors.textSecondary,
    lineHeight: 21,
    marginBottom: Spacing.sm,
  },
  warningBox: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: 8,
    backgroundColor: Colors.warningSoft,
    borderRadius: Radius.md,
    borderWidth: 1,
    borderColor: Colors.warning,
    padding: Spacing.md,
    marginBottom: Spacing.md,
  },
  warningText: {
    flex: 1,
    fontSize: FontSize.sm,
    color: Colors.text,
    lineHeight: 20,
    fontWeight: FontWeight.medium,
  },
});
