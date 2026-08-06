#!/usr/bin/env bash
#
# Kuaforum — dev-up.sh'in açtığı her şeyi kapatır: backend süreci + Docker dev
# servisleri (Postgres, Redis, Meilisearch, Seq). dev-up.sh Ctrl+C ile zaten backend'i
# durdurur ama Docker'ı AYAKTA bırakır (kasıtlı) — asıl tam kapanış bu script.
#
# Kullanım: ./dev-down.sh

set -uo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INFRA_DIR="$ROOT_DIR/infra"
PID_FILE="$ROOT_DIR/.dev-up.pid"

BOLD=$'\033[1m'; YELLOW=$'\033[33m'; GREEN=$'\033[32m'; RESET=$'\033[0m'

echo "${BOLD}${YELLOW}==> Backend süreçleri durduruluyor...${RESET}"
if [ -f "$PID_FILE" ]; then
  PID="$(cat "$PID_FILE")"
  kill "$PID" 2>/dev/null || true
  rm -f "$PID_FILE"
fi
# Güvenlik ağı: dev-up.sh normal şartlarda Ctrl+C ile kendi backend'ini zaten
# kapatmış olur (PID dosyası da kendi cleanup'ında silinir); bu satırlar dev-up.sh
# beklenmedik şekilde öldüyse (terminal kapatıldı vb.) kalan süreçleri temizler.
pkill -f "Kuaforum.Api.dll" 2>/dev/null || true
pkill -f "dotnet run --no-launch-profile" 2>/dev/null || true
pkill -f "tail -n0 -F .*dev-backend\.log" 2>/dev/null || true

COMPOSE_FILES=(-f "$INFRA_DIR/docker-compose.dev.yml")
if [ -f "$INFRA_DIR/docker-compose.override.yml" ]; then
  COMPOSE_FILES+=(-f "$INFRA_DIR/docker-compose.override.yml")
fi

echo "${BOLD}${YELLOW}==> Docker dev servisleri indiriliyor...${RESET}"
docker compose "${COMPOSE_FILES[@]}" down

echo "${GREEN}Kapandı. (Veri volume'leri korunuyor — tamamen sıfırlamak için 'down -v' kullan.)${RESET}"
