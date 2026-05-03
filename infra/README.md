# Infra — Local Development

```bash
cp .env.example .env
docker compose -f docker-compose.dev.yml up -d
docker compose -f docker-compose.dev.yml ps
```

Servisler:
- **Postgres** (PostGIS hazır) → `localhost:5432` — DB `kuaforum_dev`, user `kuaforum`, password `devpassword`.
- **Redis** → `localhost:6379`.
- **Meilisearch** → `localhost:7700` — master key `dev_master_key_change_me`.
- **Seq** (log UI) → `http://localhost:5341`.

Postgres init script `postgres-init/01-create-extensions.sql` ile **postgis**, **pgcrypto**, **uuid-ossp** uzantılarını otomatik kurar.

## Durdur / Reset

```bash
docker compose -f docker-compose.dev.yml down            # durdur (data kalır)
docker compose -f docker-compose.dev.yml down -v         # data dahil sıfırla
```
