#!/usr/bin/env bash
#
# Kuaforum — tek komut dev ortamı.
#
# 1) Docker dev servislerini (Postgres+PostGIS, Redis, Meilisearch, Seq) ayağa
#    kaldırır, Postgres + Redis sağlıklı olana kadar bekler.
# 2) Mac'in LAN IP'sini bulur ve ekrana büyük punto basar — test APK'sı bu IP'yi
#    build-time'da `EXPO_PUBLIC_API_BASE_URL` olarak gömüyor (bkz. builds/README.md).
# 3) Backend .NET API'yi tüm arayüzlere bağlı (0.0.0.0:5100), Development ortamında
#    foreground'da başlatır.
# 4) OTP kodlarının göründüğü log satırlarını ("OTP code is" / "MOCK SMS") ayrı,
#    renkli bir akışta gösterir — founder terminalde tüm logun içinde aramasın.
# 5) Ctrl+C ile backend + log-tail temiz kapanır. Docker servisleri AYAKTA kalır
#    (kasıtlı — tekrar `./dev-up.sh` çalıştırınca yeniden indirip beklemek gerekmesin).
#    Docker servislerini de kapatmak için: ./dev-down.sh
#
# Kullanım: ./dev-up.sh

set -uo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INFRA_DIR="$ROOT_DIR/infra"
BACKEND_API_DIR="$ROOT_DIR/backend/src/Kuaforum.Api"
LOG_DIR="$ROOT_DIR/builds"
BACKEND_LOG="$LOG_DIR/dev-backend.log"
PID_FILE="$ROOT_DIR/.dev-up.pid"
BACKEND_PORT=5100

BOLD=$'\033[1m'; GREEN=$'\033[32m'; YELLOW=$'\033[33m'; CYAN=$'\033[36m'; RED=$'\033[31m'; RESET=$'\033[0m'

mkdir -p "$LOG_DIR"

# infra/docker-compose.override.yml makineye özel (gitignored) — varsa katıl, yoksa
# tek dosyayla devam et (o zaman backend appsettings.Development.json'daki 5435 portu
# 5432'ye çekilmeli, ya da bu dosyayı oluşturmalısın — RUNNING.md'ye bak).
COMPOSE_FILES=(-f "$INFRA_DIR/docker-compose.dev.yml")
if [ -f "$INFRA_DIR/docker-compose.override.yml" ]; then
  COMPOSE_FILES+=(-f "$INFRA_DIR/docker-compose.override.yml")
else
  echo "${YELLOW}Uyarı: infra/docker-compose.override.yml yok — Postgres varsayılan 5432 portunda${RESET}" >&2
  echo "${YELLOW}açılacak. Backend appsettings.Development.json 5435 bekliyor, uyuşmazsa bağlanamaz.${RESET}" >&2
fi

echo "${BOLD}${CYAN}==> Docker dev servisleri başlatılıyor...${RESET}"
if ! docker compose "${COMPOSE_FILES[@]}" up -d; then
  echo "${RED}Docker compose başlatılamadı — Docker Desktop açık mı?${RESET}" >&2
  exit 1
fi

echo "${BOLD}${CYAN}==> Postgres + Redis sağlıklı olana kadar bekleniyor...${RESET}"
ATTEMPTS=0
MAX_ATTEMPTS=30
until [ "$(docker inspect -f '{{.State.Health.Status}}' kuaforum-postgres-dev 2>/dev/null)" = "healthy" ] \
   && [ "$(docker inspect -f '{{.State.Health.Status}}' kuaforum-redis-dev 2>/dev/null)" = "healthy" ]; do
  ATTEMPTS=$((ATTEMPTS + 1))
  if [ "$ATTEMPTS" -ge "$MAX_ATTEMPTS" ]; then
    echo "${RED}Postgres/Redis $MAX_ATTEMPTS denemede (60sn) sağlıklı olmadı.${RESET}" >&2
    echo "${RED}Kontrol et: docker compose -f infra/docker-compose.dev.yml ps${RESET}" >&2
    exit 1
  fi
  sleep 2
done
echo "${GREEN}Postgres + Redis hazır.${RESET}"

LAN_IP="$(ipconfig getifaddr en0 2>/dev/null || ipconfig getifaddr en1 2>/dev/null || true)"
if [ -z "$LAN_IP" ]; then
  echo "${RED}LAN IP bulunamadı (en0/en1 boş döndü) — Wi-Fi bağlantısını kontrol et.${RESET}" >&2
  LAN_IP="<BULUNAMADI>"
fi

echo ""
echo "${BOLD}${GREEN}############################################################${RESET}"
echo "${BOLD}${GREEN}#${RESET}"
echo "${BOLD}${GREEN}#   Mac LAN IP :  $LAN_IP${RESET}"
echo "${BOLD}${GREEN}#   Backend    :  http://$LAN_IP:$BACKEND_PORT${RESET}"
echo "${BOLD}${GREEN}#${RESET}"
echo "${BOLD}${GREEN}#   Bu IP değiştiyse (Wi-Fi değişti/DHCP yeniledi) test APK'sını${RESET}"
echo "${BOLD}${GREEN}#   bu yeni IP ile YENİDEN build etmen gerekir — bkz. builds/README.md${RESET}"
echo "${BOLD}${GREEN}#${RESET}"
echo "${BOLD}${GREEN}############################################################${RESET}"
echo ""

TAIL_PID=""
BACKEND_PID=""

cleanup() {
  echo ""
  echo "${YELLOW}==> Kapatılıyor (backend durduruluyor, Docker servisleri AYAKTA kalıyor)...${RESET}"
  echo "${YELLOW}Docker servislerini de kapatmak için: ./dev-down.sh${RESET}"
  rm -f "$PID_FILE"
  # Job control kapalıyken (varsayılan) `&` ile başlatılan her alt süreç (backend
  # dotnet süreci, tail, grep) bu script'in KENDİ process group'unda kalır — o yüzden
  # -$$ ile grubun tamamını tek seferde durdurmak, PID'leri tek tek kill etmekten
  # (pipeline'ın ara aşamalarını atlıyordu, test'te doğrulandı) daha güvenilir.
  kill -TERM -- -$$ 2>/dev/null || true
  sleep 1
  # Ek güvenlik ağı: bazı macOS/exec senaryolarında torun süreç ayrı gruba düşebilir.
  pkill -f "Kuaforum.Api.dll" 2>/dev/null || true
  pkill -f "tail -n0 -F $BACKEND_LOG" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

echo "${BOLD}${CYAN}==> Backend başlatılıyor (ASPNETCORE_ENVIRONMENT=Development, 0.0.0.0:$BACKEND_PORT)...${RESET}"
: > "$BACKEND_LOG"
(
  cd "$BACKEND_API_DIR" || exit 1
  ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://0.0.0.0:$BACKEND_PORT" \
    dotnet run --no-launch-profile
) >>"$BACKEND_LOG" 2>&1 &
BACKEND_PID=$!
echo "$BACKEND_PID" > "$PID_FILE"

echo "${CYAN}Tam backend logu: $BACKEND_LOG${RESET}"
echo "${BOLD}${CYAN}==> OTP kodları burada görünecek (MOCK SMS — gerçek SMS gitmiyor):${RESET}"
echo ""

tail -n0 -F "$BACKEND_LOG" 2>/dev/null \
  | grep --line-buffered -E "OTP code is|MOCK SMS" \
  | while IFS= read -r line; do
      echo "${BOLD}${GREEN}📱  $line${RESET}"
    done &
TAIL_PID=$!

echo "${CYAN}==> Backend hazır olması bekleniyor (ilk (cold) build 1-2 dk sürebilir)...${RESET}"
READY=0
MAX_WAIT=180
for i in $(seq 1 "$MAX_WAIT"); do
  if curl -s -o /dev/null "http://localhost:$BACKEND_PORT/health" 2>/dev/null; then
    READY=1
    break
  fi
  if [ $((i % 15)) -eq 0 ]; then
    echo "${CYAN}   ...hâlâ bekleniyor (${i}s) — derleniyor olabilir, $BACKEND_LOG dosyasını izleyebilirsin.${RESET}"
  fi
  sleep 1
done

if [ "$READY" -eq 1 ]; then
  echo "${GREEN}==> Backend hazır: http://$LAN_IP:$BACKEND_PORT/health  (Ctrl+C ile kapat)${RESET}"
else
  echo "${RED}==> Backend ${MAX_WAIT}sn içinde ayağa kalkmadı — $BACKEND_LOG dosyasına bak.${RESET}" >&2
fi

wait "$BACKEND_PID"
