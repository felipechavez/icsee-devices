#!/usr/bin/env sh
# Verifica que el último respaldo se pueda restaurar: lo carga en un Postgres temporal (sin red ni volumen),
# revisa que tenga datos y deja el resultado en backups/last-verify.json, que el panel Estado muestra.
# Uso: sh scripts/verify-backup.sh [backups/camguard-<fecha>.sql.gz]
#   Sin argumento toma el camguard-*.sql.gz y el keycloak-*.sql.gz más nuevos de backups/.
# Cron (después del respaldo diario):
#   0 3 * * * cd /ruta/a/icsee-devices && { sh scripts/backup-db.sh && sh scripts/verify-backup.sh; } >> backups/backup.log 2>&1
set -u

cd "$(dirname "$0")/.."

IMAGE="postgres:17-alpine" # la misma del servicio db
NAME="camguard-verify-$$"
RESULT="backups/last-verify.json"
WARNINGS=""

newest() {
  # shellcheck disable=SC2012
  ls -t backups/$1-*.sql.gz 2>/dev/null | head -n 1
}

# Escribe el resultado (temporal + mv) y termina. $1 = true|false, $2 = detalle.
finish() {
  ok="$1"
  detail="$(printf '%s' "$2" | tr '\n\r\t' '   ' | sed 's/\\/\\\\/g; s/"/\\"/g' | cut -c1-500)"
  file="$(basename "${CG:-}")"
  mkdir -p backups
  printf '{"verifiedAt":"%s","ok":%s,"file":"%s","detail":"%s"}\n' \
    "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$ok" "$file" "$detail" > "$RESULT.tmp"
  mv "$RESULT.tmp" "$RESULT"
  if [ "$ok" = true ]; then
    echo "$(date '+%F %T') Respaldo verificado: $file · $2"
    exit 0
  fi
  echo "$(date '+%F %T') FALLÓ la verificación del respaldo ${file:-?}: $2" >&2
  exit 1
}

cleanup() {
  docker rm -f "$NAME" >/dev/null 2>&1 || true
}

# psql dentro del Postgres temporal. $1 = base, resto = argumentos.
pg() {
  db="$1"
  shift
  docker exec -i "$NAME" psql -U postgres -h 127.0.0.1 -d "$db" -v ON_ERROR_STOP=1 -q "$@"
}

CG="${1:-$(newest camguard)}"
[ -n "$CG" ] && [ -f "$CG" ] || finish false "No hay respaldo de CamGuard en backups/."

KC="$(dirname "$CG")/$(basename "$CG" | sed 's/^camguard-/keycloak-/')"
[ -f "$KC" ] || KC="$(newest keycloak)"
[ -n "$KC" ] && [ -f "$KC" ] || finish false "No hay respaldo de Keycloak en backups/."

# 1. Integridad de los archivos.
gzip -t "$CG" 2>/dev/null || finish false "$(basename "$CG") está dañado (gzip -t)."
gzip -t "$KC" 2>/dev/null || finish false "$(basename "$KC") está dañado (gzip -t)."

# 2. Postgres temporal.
trap cleanup EXIT INT TERM
docker run -d --rm --name "$NAME" --network none -e POSTGRES_PASSWORD=verify "$IMAGE" >/dev/null \
  || finish false "No se pudo iniciar el Postgres temporal ($IMAGE)."

# Por TCP: el servidor provisional del arranque solo escucha por socket.
i=0
until docker exec "$NAME" pg_isready -U postgres -h 127.0.0.1 >/dev/null 2>&1; do
  i=$((i + 1))
  [ "$i" -ge 60 ] && finish false "El Postgres temporal no respondió en 60 s."
  sleep 1
done

# 3. Roles y bases (el dump lleva OWNER TO).
out="$(pg postgres -c "CREATE ROLE camguard LOGIN" -c "CREATE ROLE keycloak LOGIN" \
  -c "CREATE DATABASE camguard OWNER camguard" -c "CREATE DATABASE keycloak OWNER keycloak" 2>&1)" \
  || finish false "No se pudieron crear las bases de prueba: $out"

# 4. Restauración.
out="$(gunzip -c "$CG" | pg camguard 2>&1 >/dev/null)" || finish false "Error al restaurar $(basename "$CG"): $out"
out="$(gunzip -c "$KC" | pg keycloak 2>&1 >/dev/null)" || finish false "Error al restaurar $(basename "$KC"): $out"

# 5. Revisiones.
q() {
  pg "$1" -tAc "$2" 2>/dev/null | tr -d '[:space:]'
}

migrations="$(q camguard 'SELECT count(*) FROM "__EFMigrationsHistory"')"
[ "${migrations:-0}" -gt 0 ] 2>/dev/null || finish false "La base restaurada no tiene migraciones."

settings="$(q camguard 'SELECT count(*) FROM "Settings"')"
[ "${settings:-0}" = 1 ] || finish false "La tabla Settings no tiene su fila (hay ${settings:-0})."

cameras="$(q camguard 'SELECT count(*) FROM "Cameras"')"
[ "${cameras:-0}" -ge 1 ] 2>/dev/null || finish false "La base restaurada no tiene cámaras."

recordings="$(q camguard 'SELECT count(*) FROM "Recordings"')"

last="$(q camguard 'SELECT max("MigrationId") FROM "__EFMigrationsHistory"')"
live="$(docker compose exec -T db psql -U camguard -d camguard -tAc 'SELECT max("MigrationId") FROM "__EFMigrationsHistory"' 2>/dev/null | tr -d '[:space:]')"
if [ -n "$live" ] && [ "$live" != "$last" ]; then
  WARNINGS=" · aviso: la base en vivo tiene la migración $live (hubo un deploy después del respaldo)"
fi

realm="$(q keycloak "SELECT id FROM realm WHERE name = 'camguard'")"
[ -n "$realm" ] || finish false "El respaldo de Keycloak no tiene el realm camguard."
users="$(q keycloak "SELECT count(*) FROM user_entity WHERE realm_id = '$realm'")"
[ "${users:-0}" -ge 1 ] 2>/dev/null || finish false "El realm camguard no tiene usuarios."

finish true "$cameras cámaras, $recordings clips, migración $last; Keycloak con $users usuarios$WARNINGS"
