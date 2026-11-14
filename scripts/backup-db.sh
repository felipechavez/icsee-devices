#!/usr/bin/env sh
# Respaldo completo de la base CamGuard y de Keycloak (ambas Postgres) en ./backups.
# Pensado para cron en el equipo de la casa, seguido de la verificación (scripts/verify-backup.sh, que
# restaura el respaldo en un Postgres temporal y deja el resultado en el panel Estado), p. ej. a las 3:00:
#   0 3 * * * cd /ruta/a/icsee-devices && { sh scripts/backup-db.sh && sh scripts/verify-backup.sh; } >> backups/backup.log 2>&1
# Restaurar:
#   gunzip -c backups/camguard-<fecha>.sql.gz | docker compose exec -T db psql -U camguard -d camguard
#   gunzip -c backups/keycloak-<fecha>.sql.gz | docker compose exec -T keycloak-db psql -U keycloak -d keycloak
#   (el de Keycloak no lleva --clean: restáuralo en una base vacía, con keycloak detenido)
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
