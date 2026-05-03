# Claude Code Sub-Agent Kullanım Rehberi

Kuaforum'da 9 özelleşmiş sub-agent tanımlı. `.claude/agents/` altında her biri kendi system prompt + model + araç kapsamı ile yaşar.

---

## 1. Tüm Agent Listesi

| Agent | Model | Sorumluluk | Hangi konuda çağır |
|---|---|---|---|
| `backend-developer` | sonnet | .NET 8 implementation | endpoint, handler, EF, MediatR |
| `mobile-developer` | sonnet | Expo / RN | screen, navigation, state, EAS |
| `database-architect` | sonnet | PostgreSQL + EF | schema, migration, performans |
| `payment-integration` | **opus** | PSP + e-Arşiv | PayTR, Iyzico, Nilvera, refund |
| `security-auditor` | **opus** | KVKK + OWASP | review, threat model, compliance |
| `test-engineer` | sonnet | Test pyramid | unit, integration, E2E, load |
| `devops-engineer` | sonnet | CI/CD + infra | Docker, Actions, Hetzner, Sentry |
| `product-strategist` | **opus** | Strategy + scope | requirement, MVP, GTM, persona |
| `documentation-writer` | sonnet | Markdown disiplini | spec, RFC, runbook |

> **Model seçimi:** opus = derin akıl gerektiren strategy/security/payment için. Sonnet = pratik implementation için. Haiku şu an kullanılmıyor (kritik domain'ler için yetersiz).

---

## 2. Tetikleyici Sözlükler (founder konuşması ↔ agent)

Founder Türkçe konuşur. Aşağıdaki kelimeler agent çağrısı tetikler:

### `backend-developer`
- "endpoint", "API", "handler", "controller", "service", "MediatR", "repo", "EF", ".NET"

### `mobile-developer`
- "ekran", "screen", "Expo", "React Native", "mobil", "navigation", "form", "Zustand", "Tamagui"

### `database-architect`
- "migration", "tablo", "kolon", "index", "PostGIS", "sorgu yavaş", "explain", "partition"

### `payment-integration`
- "ödeme", "PayTR", "Iyzico", "Nilvera", "fatura", "refund", "iade", "komisyon", "webhook", "sub-merchant"

### `security-auditor`
- "güvenlik", "KVKK", "OWASP", "threat", "audit", "PII", "secret", "vulnerability"

### `test-engineer`
- "test", "coverage", "xUnit", "Jest", "Detox", "k6", "mock", "Testcontainers", "flaky"

### `devops-engineer`
- "deploy", "CI", "docker", "Hetzner", "Sentry", "log", "monitoring", "prod", "staging"

### `product-strategist`
- "MVP", "scope", "persona", "rakip", "fiyat", "komisyon tier", "GTM", "metric", "OKR"

### `documentation-writer`
- "doc", "spec", "RFC", "runbook", "CLAUDE.md", "README"

---

## 3. Çoklu Agent Paralel

Birden fazla bağımsız iş varsa **tek mesajda paralel çağrı**. Örnek:

> "Auth modülü için backend handler + mobil ekran + DB migration aynı anda."

→ Main Claude tek turda 3 paralel agent çağırır:
- `backend-developer` — handler + endpoint
- `mobile-developer` — login screen + form
- `database-architect` — migration + index

Sonuçlar dönünce main Claude koordinatör — birleştirir, çelişki varsa raporlar.

---

## 4. Agent Çıktı Disiplini

Her agent yanıtı şu yapıya sahip:
- **Yapılan iş** (özet)
- **Etkilenen dosyalar**
- **Test durumu**
- **Sonraki adım önerisi**
- **Push-back / risk uyarısı** (varsa)

---

## 5. Agent Eklemek

Yeni agent ihtiyacı doğarsa:
1. **RFC** aç: `docs/rfc/RFC-NNN-add-<agent>.md`.
2. Sorumluluk + çakışma analizi (mevcut agent'larla overlap).
3. Founder onayı.
4. `.claude/agents/<name>.md` ekle.
5. Bu doc'u (`02-agents.md`) güncelle.

Mevcut agent'ları **silme**: 1 hafta deprecation notice + master spec güncellenmesi.

---

## 6. Agent Performans Takibi

Manuel — her conversation sonu founder şu soruları cevaplayarak agent verimliliğini takip eder:

- Hangi agent doğru tetiklendi mi?
- Yanıt kalitesi (1-5)?
- Push-back yapıldı mı (gerekiyorsa)?

Phase 1 sonu: bu izleme otomatize edilebilir mi (hook ile log'a yaz)?

---

## 7. Bağlantılar

- [`docs/15-claude-setup/01-overview.md`](01-overview.md)
- [`docs/15-claude-setup/03-mcp-context7.md`](03-mcp-context7.md)
- `.claude/agents/*` (9 dosya)
