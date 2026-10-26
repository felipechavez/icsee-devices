#!/usr/bin/env sh
# Respaldo completo de la base CamGuard y de Keycloak (ambas Postgres) en ./backups.
# Pensado para cron en el equipo de la casa, p. ej. todos los días a las 3:00:
#   0 3 * * * cd /ruta/a/icsee-devices && ./scripts/backup-db.sh >> backups/backup.log 2>&1
# Restaurar: gunzip -c backups/camguard-<fecha>.sql.gz | docker compose exec -T db psql -U camguard -d camguard
set -eu

KEEP_DAYS="${KEEP_DAYS:-14}"
STAMP="$(date +%Y%m%d-%H%M%S)"

mkdir -p backups

# CamGuard (cámaras, historial de clips, eventos, ajustes).
FILE="camguard-${STAMP}.sql.gz"
docker compose exec -T db pg_dump -U camguard -d camguard --clean --if-exists | gzip > "backups/${FILE}"
echo "Respaldo creado: backups/${FILE}"
find backups -name 'camguard-*.sql.gz' -mtime +"${KEEP_DAYS}" -delete

# Keycloak (usuarios y sesiones).
KC_FILE="keycloak-${STAMP}.sql.gz"
docker compose exec -T keycloak-db pg_dump -U keycloak -d keycloak | gzip > "backups/${KC_FILE}"
echo "Respaldo creado: backups/${KC_FILE}"
find backups -name 'keycloak-*.sql.gz' -mtime +"${KEEP_DAYS}" -delete
