#!/usr/bin/env sh
# Actualiza CamGuard en el equipo de la casa (p. ej. la Raspberry Pi) con lo último de la rama actual.
# Uso: sh scripts/deploy.sh [servicio ...]   (con sh: git guarda los scripts sin permiso de ejecución)
#   Sin servicios reconstruye y recrea lo que haya cambiado (lo que no cambió sale de la caché de Docker).
# Si el pull trae migraciones de EF, respalda la base antes (scripts/backup-db.sh); la API las aplica al arrancar.
# El .env no se toca: vive solo en este equipo.
set -eu

cd "$(dirname "$0")/.."

BEFORE="$(git rev-parse HEAD)"
git pull --ff-only
AFTER="$(git rev-parse HEAD)"

if [ "$BEFORE" != "$AFTER" ]; then
  echo "Cambios: $(git log --oneline "$BEFORE..$AFTER" | wc -l) commit(s)"
  if git diff --name-only "$BEFORE" "$AFTER" | grep -q '^backend/src/CamGuard.Api/Common/Data/Migrations/'; then
    echo "Hay migraciones nuevas: respaldando la base."
    sh scripts/backup-db.sh
  fi
  if git diff --name-only "$BEFORE" "$AFTER" | grep -qx 'go2rtc.yaml'; then
    RESTART_GO2RTC=1
  fi
fi

# La API monta ./backups (solo lectura) para mostrar el estado de los respaldos. Si Docker crea la carpeta
# del bind mount queda de root y backup-db.sh no puede escribir en ella.
mkdir -p backups

# shellcheck disable=SC2068
docker compose up -d --build $@

# go2rtc.yaml va montado como archivo: cambiarlo no recrea el contenedor.
if [ "${RESTART_GO2RTC:-0}" = 1 ]; then
  docker compose restart go2rtc
fi

# Espera a la API a través de nginx: /api/status sin token responde 401 si la API está arriba
# y 502/504 mientras arranca (aplica migraciones y espera a Postgres).
PORT="$(sed -n 's/^WEB_PORT=//p' .env 2>/dev/null | tr -d '\r"')"
PORT="${PORT:-8090}"
echo "Esperando a la API en http://localhost:${PORT} ..."
i=0
until code="$(curl -s -o /dev/null -w '%{http_code}' "http://localhost:${PORT}/api/status")" \
  && [ "$code" != 000 ] && [ "$code" != 502 ] && [ "$code" != 503 ] && [ "$code" != 504 ]; do
  i=$((i + 1))
  if [ "$i" -ge 60 ]; then
    echo "La API no respondió en 2 minutos. Últimos logs:" >&2
    docker compose logs --tail 50 api >&2
    exit 1
  fi
  sleep 2
done
echo "API arriba (HTTP ${code})."

docker image prune -f >/dev/null
docker compose ps
