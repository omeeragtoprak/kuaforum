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
export ASPNETCORE_URLS="http://+:${PORT}"
exec dotnet Kuaforum.Api.dll
