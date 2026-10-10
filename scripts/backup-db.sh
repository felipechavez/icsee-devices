#!/usr/bin/env sh
# Respaldo completo de la base CamGuard (SQL Server) y de Keycloak (Postgres) en ./backups.
# Pensado para cron en el equipo de la casa, p. ej. todos los días a las 3:00:
#   0 3 * * * cd /ruta/a/icsee-devices && ./scripts/backup-db.sh >> backups/backup.log 2>&1
set -eu

KEEP_DAYS="${KEEP_DAYS:-14}"
STAMP="$(date +%Y%m%d-%H%M%S)"
FILE="CamGuard-${STAMP}.bak"

mkdir -p backups

docker compose exec -T sqlserver sh -c \
  "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"\$MSSQL_SA_PASSWORD\" -C -b \
   -Q \"BACKUP DATABASE CamGuard TO DISK = N'/var/opt/mssql/backup/${FILE}' WITH COMPRESSION, INIT\""

echo "Respaldo creado: backups/${FILE}"

# Borra respaldos más antiguos que KEEP_DAYS.
find backups -name 'CamGuard-*.bak' -mtime +"${KEEP_DAYS}" -delete

# Keycloak (usuarios y sesiones): volcado de Postgres comprimido.
KC_FILE="keycloak-${STAMP}.sql.gz"
docker compose exec -T keycloak-db pg_dump -U keycloak -d keycloak | gzip > "backups/${KC_FILE}"
echo "Respaldo creado: backups/${KC_FILE}"
find backups -name 'keycloak-*.sql.gz' -mtime +"${KEEP_DAYS}" -delete
