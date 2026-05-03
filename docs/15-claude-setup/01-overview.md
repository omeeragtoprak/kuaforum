# Claude Code Setup — Genel Bakış

> Bu doküman, Kuaforum projesinde Claude Code'un nasıl konumlandırıldığını, hangi yapılandırmaların neden olduğunu, founder'ın yeni bir conversation açtığında neyi nasıl beklediğini özetler.

---

## 1. Üç Katmanlı Bağlam

Claude Code her conversation'da üç katmandan bağlam alır — yazılı bir founder hafızasına ihtiyaç duymaz:

1. **`CLAUDE.md`** (proje kökü) — proje rehberi, stack özeti, kurallar, kodlama standartları.
2. **`docs/PHASE-0-MASTER-SPEC.md`** — kararlar, kapsam, riskler. Bağlayıcı kabul.
3. **`~/.claude/projects/.../memory/`** — Claude'un sürekli bellek dosyaları (founder profili, feedback, kalıcı kurallar).

Üçü çelişirse: **memory > master spec > CLAUDE.md** önceliği. Çelişki tespit edilirse **RFC açılır**.

---

## 2. Dosya Yapısı

```
kuaforum/
├── CLAUDE.md                       ← her conversation'da otomatik yüklenir
├── .claude/
│   ├── agents/                     ← sub-agent definitions
│   │   ├── backend-developer.md
│   │   ├── mobile-developer.md
│   │   ├── database-architect.md
│   │   ├── payment-integration.md
│   │   ├── security-auditor.md
│   │   ├── test-engineer.md
│   │   ├── devops-engineer.md
│   │   ├── product-strategist.md
│   │   └── documentation-writer.md
│   └── settings.local.json         ← project-level Claude settings
└── docs/
    └── 15-claude-setup/
        ├── 01-overview.md          ← bu doküman
        ├── 02-agents.md            ← agent kullanımı + tetikleyiciler
        ├── 03-mcp-context7.md      ← context7 MCP kullanımı
        └── 04-permanent-rules.md   ← founder + Claude için kalıcı kurallar
```

---

## 3. Founder İçin Hızlı Başlangıç

```bash
# Yeni bir conversation açtığında, Claude şu sırayla bağlam yükler:
# 1. CLAUDE.md (otomatik)
# 2. memory/MEMORY.md indeks (otomatik)
# 3. PHASE-0-MASTER-SPEC.md (Claude ilk işlemde okur)

# Claude'u doğru subagent'a yönlendirmek için:
# "Backend modülü ekle" → backend-developer
# "Mobil ekran yaz" → mobile-developer
# "Migration düz" → database-architect
# "PSP entegrasyon" → payment-integration
# vs.
```

Claude yanıt verirken **dürüst, şüpheci, sorgulayıcı** olmaya kayıtlı (memory rule). Bu davranış kapatılmaz.

---

## 4. Conversation Tipi → Hangi Agent

| Tip | Agent |
|---|---|
| ".NET endpoint ekle / handler yaz / repository genişlet" | `backend-developer` |
| "RN ekran yaz / Expo build sorunu / TanStack Query hook" | `mobile-developer` |
| "EF Core migration / sorgu yavaş / partition" | `database-architect` |
| "PayTR webhook / Iyzico onboarding / Nilvera fatura" | `payment-integration` |
| "KVKK compliance / OWASP review / threat model" | `security-auditor` |
| "Unit test / integration / Detox E2E / k6" | `test-engineer` |
| "Docker / GitHub Actions / Hetzner deploy / Sentry" | `devops-engineer` |
| "MVP scope / persona / customer discovery / pricing" | `product-strategist` |
| "Module spec / RFC / runbook / CLAUDE.md update" | `documentation-writer` |

Çoklu domain karışırsa main Claude koordinatör, sub-agent çağrılarını paralel yapar.

---

## 5. MCP

- **`context7`** — library/framework docs için. Hep kullan.
- Diğer MCP eklenmesi gerekiyorsa RFC ile tartışılır (örn. GitHub MCP, PostgreSQL MCP).

Detay: [`docs/15-claude-setup/03-mcp-context7.md`](03-mcp-context7.md).

---

## 6. Skills

Kuaforum projesi global Claude skill'lerinden yararlanır:
- **brainstorming** — yeni özellik için tasarım turu.
- **writing-plans** — onaylı tasarımı implementation plan'a çevirir.
- **simplify** — kod review + sadeleştirme.
- **schedule** — gelecek bir tarihte otomatik agent çalıştırma.
- **fewer-permission-prompts** — sık kullanılan komutları allowlist'e ekleme.

Founder bu skill'leri `/<skill-name>` ile çağırır. Detay: ileride `docs/15-claude-setup/05-skills.md` (gerekirse).

---

## 7. Hooks (Phase 1 sonu)

Şu an **hook yok**. Phase 1 sonunda değerlendirilecek:
- Pre-commit: dotnet format + biome (mobile)
- Pre-push: test çalıştır
- Stop hook: Claude'un yanıtı bittiğinde lint hatasını rapor

`update-config` skill ile ayarlanır.

---

## 8. Permanent Memory

`~/.claude/projects/-Users-omer-Desktop-Projelerim-sirket-projeleri-kuaforum/memory/` altında:

```
memory/
├── MEMORY.md                              ← indeks
├── rules_honest_skeptic_researcher.md     ← founder kuralı
├── project_kuaforum_overview.md           ← proje özet
├── ...                                    ← yeni feedback / öğrenme eklendikçe
```

Her conversation'da memory yüklü. Founder "şunu hatırla" derse Claude `Write` tool ile ek dosya açıp `MEMORY.md`'ye link ekler.

---

## 9. İletişim Stili

- **Konuşma:** Türkçe gündelik (founder "kanka" + casual). Claude aynı tonda.
- **Doc:** Türkçe formal + İngilizce teknik terim.
- **Push-back:** her zaman gerekçeli; Claude yes-man değil.

---

## 10. Bağlantılar

- [`CLAUDE.md`](../../CLAUDE.md)
- [`docs/15-claude-setup/02-agents.md`](02-agents.md)
- [`docs/15-claude-setup/03-mcp-context7.md`](03-mcp-context7.md)
- [`docs/15-claude-setup/04-permanent-rules.md`](04-permanent-rules.md)
