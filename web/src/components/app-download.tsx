import { Apple, Play, Smartphone } from 'lucide-react';

// Store linkleri founder-bloklu (RFC-011 founder aksiyonları tablosu).
// Placeholder link YOK — tıklanamayan rozet + "Yakında" etiketi.
function StoreBadge({
  icon,
  topLine,
  storeName,
}: {
  icon: React.ReactNode;
  topLine: string;
  storeName: string;
}) {
  return (
    <div
      className="relative flex w-full items-center gap-3 rounded-xl border border-paper/20 bg-paper/5 px-5 py-3.5 sm:w-56"
      aria-label={`${storeName} — yakında`}
    >
      {icon}
      <span className="flex flex-col text-left leading-tight">
        <span className="text-[0.7rem] uppercase tracking-wide text-paper/50">{topLine}</span>
        <span className="text-base font-semibold text-paper">{storeName}</span>
      </span>
      <span className="absolute -right-2 -top-2 rounded-full bg-gold px-2.5 py-0.5 text-[0.7rem] font-bold uppercase tracking-wide text-ink-950">
        Yakında
      </span>
    </div>
  );
}

export function AppDownloadSection() {
  return (
    <section
      id="indir"
      aria-labelledby="indir-baslik"
      className="scroll-mt-20 bg-ink-950"
    >
      <div className="pinstripe-dark">
        <div className="copper-glow">
          <div className="mx-auto max-w-site px-4 py-16 sm:px-6 sm:py-24">
            <div className="mx-auto max-w-2xl text-center">
              <span className="inline-flex items-center gap-2 rounded-full border border-paper/15 bg-paper/5 px-4 py-1.5 text-sm font-medium text-paper/70">
                <Smartphone className="h-4 w-4" aria-hidden="true" />
                Mobil uygulama
              </span>
              <h2
                id="indir-baslik"
                className="text-balance mt-5 font-display text-3xl font-semibold text-paper sm:text-4xl"
              >
                Randevu almak için uygulamayı indir
              </h2>
              <p className="mt-4 text-base leading-relaxed text-paper/60 sm:text-lg">
                Kuaforum çok yakında App Store ve Google Play&apos;de. Salonları web&apos;den
                incele; randevu, ödeme ve hatırlatmalar uygulamada.
              </p>

              <div className="mt-8 flex flex-col items-center justify-center gap-4 sm:flex-row">
                <StoreBadge
                  icon={<Apple className="h-7 w-7 text-paper" aria-hidden="true" />}
                  topLine="App Store"
                  storeName="iPhone için"
                />
                <StoreBadge
                  icon={<Play className="h-6 w-6 text-paper" aria-hidden="true" />}
                  topLine="Google Play"
                  storeName="Android için"
                />
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
