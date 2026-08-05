import type { ReactNode } from 'react';

// Minimal markdown render — legal metinlerin kullandığı alt küme:
// #/##/### başlık, > blockquote, -/* liste, 1. sıralı liste, **bold**, paragraf.
// Bilinçli olarak react-markdown eklenmedi: bağımlılık yüzeyini küçük tutmak için
// (legal metinler backend'de kontrol ettiğimiz embedded resource'lar).
// HTML pass-through YOK — her satır düz metin olarak React'e verilir (XSS güvenli).

let keyCounter = 0;
function nextKey(): number {
  keyCounter += 1;
  return keyCounter;
}

/** `**bold**` parçalarını <strong>'a çevirir; kalan her şey düz metin. */
function renderInline(text: string): ReactNode[] {
  const parts = text.split(/(\*\*[^*]+\*\*)/g);
  return parts.map((part) => {
    if (part.startsWith('**') && part.endsWith('**') && part.length > 4) {
      return <strong key={nextKey()}>{part.slice(2, -2)}</strong>;
    }
    return <span key={nextKey()}>{part}</span>;
  });
}

type Block =
  | { kind: 'heading'; level: 1 | 2 | 3; text: string }
  | { kind: 'quote'; lines: string[] }
  | { kind: 'ul'; items: string[] }
  | { kind: 'ol'; items: string[] }
  | { kind: 'p'; lines: string[] };

function parseBlocks(markdown: string): Block[] {
  const lines = markdown.replace(/\r\n/g, '\n').split('\n');
  const blocks: Block[] = [];
  let current: Block | null = null;

  const flush = () => {
    if (current) blocks.push(current);
    current = null;
  };

  for (const raw of lines) {
    const line = raw.trimEnd();
    const trimmed = line.trim();

    if (trimmed === '') {
      flush();
      continue;
    }

    const heading = /^(#{1,3})\s+(.*)$/.exec(trimmed);
    if (heading) {
      flush();
      blocks.push({
        kind: 'heading',
        level: heading[1].length as 1 | 2 | 3,
        text: heading[2],
      });
      continue;
    }

    if (trimmed.startsWith('>')) {
      const content = trimmed.replace(/^>\s?/, '');
      if (current?.kind === 'quote') current.lines.push(content);
      else {
        flush();
        current = { kind: 'quote', lines: [content] };
      }
      continue;
    }

    const ulItem = /^[-*]\s+(.*)$/.exec(trimmed);
    if (ulItem) {
      if (current?.kind === 'ul') current.items.push(ulItem[1]);
      else {
        flush();
        current = { kind: 'ul', items: [ulItem[1]] };
      }
      continue;
    }

    const olItem = /^\d+[.)]\s+(.*)$/.exec(trimmed);
    if (olItem) {
      if (current?.kind === 'ol') current.items.push(olItem[1]);
      else {
        flush();
        current = { kind: 'ol', items: [olItem[1]] };
      }
      continue;
    }

    if (current?.kind === 'p') current.lines.push(trimmed);
    else {
      flush();
      current = { kind: 'p', lines: [trimmed] };
    }
  }
  flush();
  return blocks;
}

export function MarkdownContent({ markdown }: { markdown: string }) {
  const blocks = parseBlocks(markdown);

  return (
    <div className="space-y-5">
      {blocks.map((block) => {
        switch (block.kind) {
          case 'heading': {
            if (block.level === 1)
              return (
                <h1
                  key={nextKey()}
                  className="font-display text-3xl font-semibold text-ink-900 sm:text-4xl"
                >
                  {renderInline(block.text)}
                </h1>
              );
            if (block.level === 2)
              return (
                <h2
                  key={nextKey()}
                  className="pt-4 font-display text-xl font-semibold text-ink-900 sm:text-2xl"
                >
                  {renderInline(block.text)}
                </h2>
              );
            return (
              <h3 key={nextKey()} className="pt-2 text-lg font-semibold text-ink-900">
                {renderInline(block.text)}
              </h3>
            );
          }
          case 'quote':
            return (
              <blockquote
                key={nextKey()}
                className="border-l-4 border-copper-200 bg-paper-warm px-4 py-3 text-[0.95rem] leading-relaxed text-ink-700"
              >
                {block.lines.map((l) => (
                  <p key={nextKey()}>{renderInline(l)}</p>
                ))}
              </blockquote>
            );
          case 'ul':
            return (
              <ul key={nextKey()} className="list-disc space-y-1.5 pl-6 leading-relaxed text-ink-700">
                {block.items.map((item) => (
                  <li key={nextKey()}>{renderInline(item)}</li>
                ))}
              </ul>
            );
          case 'ol':
            return (
              <ol key={nextKey()} className="list-decimal space-y-1.5 pl-6 leading-relaxed text-ink-700">
                {block.items.map((item) => (
                  <li key={nextKey()}>{renderInline(item)}</li>
                ))}
              </ol>
            );
          case 'p':
          default:
            return (
              <p key={nextKey()} className="leading-relaxed text-ink-700">
                {renderInline(block.lines.join(' '))}
              </p>
            );
        }
      })}
    </div>
  );
}
