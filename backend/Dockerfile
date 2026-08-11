# syntax=docker/dockerfile:1
#
# Kuaforum backend (ASP.NET Core 8 API). Multi-stage: SDK image builds/publishes,
# runtime image is the slim ASP.NET runtime only (no SDK, no source, non-root).
#
# Build context MUST be the `backend/` directory:
#   docker build -f backend/Dockerfile -t kuaforum-api:local backend
# (infra/docker-compose.prod.yml sets `context: ../backend` accordingly.)

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files first (layer caching: restore only re-runs when a .csproj changes,
# not on every source edit).
COPY src/Kuaforum.Api/Kuaforum.Api.csproj src/Kuaforum.Api/
COPY src/Kuaforum.Application/Kuaforum.Application.csproj src/Kuaforum.Application/
COPY src/Kuaforum.Domain/Kuaforum.Domain.csproj src/Kuaforum.Domain/
COPY src/Kuaforum.Infrastructure/Kuaforum.Infrastructure.csproj src/Kuaforum.Infrastructure/
RUN dotnet restore src/Kuaforum.Api/Kuaforum.Api.csproj

COPY src/ src/
RUN dotnet publish src/Kuaforum.Api/Kuaforum.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# --- Runtime -----------------------------------------------------------------
# IMPORTANT: mcr.microsoft.com/dotnet/aspnet:8.0 (Debian bookworm-slim) ships
# full ICU by default — tr-TR culture formatting (dates, currency, string
# comparison) used across the app needs this. Do NOT set
# DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true and do NOT switch to an
# invariant/no-ICU base image.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# curl is needed for the HEALTHCHECK below; the aspnet base image doesn't ship it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# ASPNETCORE_URLS ARTIK burada sabitlenmiyor — docker-entrypoint.sh runtime'da
# $PORT env'ini okuyup kurar (Koyeb gibi PaaS'lar portu deploy anında $PORT ile
# verir; Docker ENV build-time'da donduğu için başka bir env'e referans veremez,
# bu yüzden shell entrypoint şart). Hetzner docker-compose.prod.yml PORT=8080
# explicit veriyor (mevcut Caddyfile/healthcheck davranışı korunuyor).
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish .
COPY docker-entrypoint.sh /app/docker-entrypoint.sh
RUN chmod +x /app/docker-entrypoint.sh

# Non-root: .NET 8+ base images ship a built-in "app" user, UID exposed via
# $APP_UID. See infra/README.md for the source on this ("What's new in
# containers for .NET 8", Microsoft Learn).
USER $APP_UID

# EXPOSE metadata amaçlı — Docker bunu fiilen publish etmez (ports: mapping ile
# yapılır) ve Koyeb kendi PORT taramasını kullanır. 8000 = Koyeb default'u.
EXPOSE 8000

# Shell form (no brackets after CMD) — Docker runs it via `/bin/sh -c`, which
# expands $PORT at healthcheck-run time. PORT set değilse 8000'e düşer —
# entrypoint.sh'deki default ile tutarlı.
# `/health` (liveness) — `/health/ready` DEĞİL. İkisinin farkı burada kritik:
#
# `/health/ready` dış bağımlılıkları (veritabanı) da yoklar. Bunu konteyner
# HEALTHCHECK'ine bağlamak iki sorun üretti:
#   1. Render'da deploy hiç canlıya çıkmadı. Neon uykudan uyanırken /health/ready
#      5 sn'lik timeout'u aşıyordu → konteyner "unhealthy" → Render trafiği HİÇ
#      yönlendirmedi. Uygulama sorunsuz açılıp 0.0.0.0'ı dinlemesine rağmen
#      Serilog'da tek bir istek görünmüyordu; üç deploy bu yüzden kaybedildi.
#   2. Tasarım olarak da yanlış: veritabanı bir an yavaşladığında konteyner
#      yeniden başlatılır — oysa uygulama sağlamdır ve DB dönünce kendiliğinden
#      düzelir. Liveness "süreç ayakta mı", readiness "trafik alabilir mi"
#      sorusudur; orkestratör (Render/compose) readiness'ı ayrıca sorar.
#
# start-period 60s: soğuk başlangıçta migration + ilk DB bağlantısı 25 sn'yi
# aşabiliyor; bu süre içindeki başarısızlıklar retry sayacına yazılmaz.
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD curl -fsS "http://localhost:${PORT:-8000}/health" || exit 1

ENTRYPOINT ["/app/docker-entrypoint.sh"]
