#!/bin/sh
# Koyeb (ve benzeri PaaS'lar) container'a hangi portu dinleyeceğini $PORT env
# değişkeniyle bildirir, deploy anında değişebilir — bu yüzden ASPNETCORE_URLS
# Dockerfile'da SABİT bir ENV olarak yazılamaz (build-time'da donar, runtime'da
# $PORT'u göremez). Bu script runtime'da $PORT'u okuyup ASPNETCORE_URLS'i kurar,
# sonra dotnet'i exec eder (PID 1 kalır, SIGTERM doğru iletilir).
#
# PORT set edilmemişse (örn. Hetzner docker-compose.prod.yml, orada PORT=8080
# olarak explicit veriliyor — bkz. infra/docker-compose.prod.yml) 8000 varsayılan
# (Koyeb'in kendi default'u).
set -e
: "${PORT:=8000}"

# NEDEN `0.0.0.0` ve `+` DEĞİL: Kestrel `http://+:PORT` verildiğinde IPv6 wildcard'a
# bağlanır ve log'a `http://[::]:PORT` yazar. Dual-stack açık bir ortamda bu IPv4'ü de
# kapsar; ama Render'ın konteynerinde kapsamadı — uygulama sorunsuz açıldı, Render
# portu doğru tespit etti, fakat /health yoklaması uygulamaya HİÇ ULAŞMADI (Serilog
# istek log'u boştu) ve deploy 15 dk sonra "Timed Out" ile düştü. İki deploy bu
# yüzden kaybedildi.
#
# `0.0.0.0` açıkça IPv4 dinler; ters vekilin IPv4'ten bağlanması garanti altına alınır.
# IPv6-only bir platforma taşınırsa bu satır yeniden değerlendirilmeli.
export ASPNETCORE_URLS="http://0.0.0.0:${PORT}"
exec dotnet Kuaforum.Api.dll
