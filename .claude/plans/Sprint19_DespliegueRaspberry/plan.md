# Despliegue de cambios a la Raspberry Pi 4

## Context
No había un flujo documentado para actualizar la Pi: el README solo cubría la primera instalación y la mudanza de datos. Todo corre con `docker compose`. La API, la web y Keycloak se construyen en el equipo (`build:`); go2rtc, db y keycloak-db usan imágenes públicas multiarquitectura.

## Flujo
PC: cambios → `git push` a la rama que sigue la Pi (`main`). Pi: `sh scripts/deploy.sh [servicio ...]`.

`scripts/deploy.sh`:
1. `git pull --ff-only` y comparar HEAD antes/después.
2. Si cambió `backend/src/CamGuard.Api/Common/Data/Migrations/`, correr `sh scripts/backup-db.sh` antes de recrear la API (las migraciones se aplican solas al arrancar y no tienen vuelta atrás automática).
3. `docker compose up -d --build [servicios]`. Sin argumentos se reconstruye todo; lo que no cambió sale de la caché de Docker.
4. Si cambió `go2rtc.yaml`, `docker compose restart go2rtc`: es un archivo montado y su cambio no recrea el contenedor.
5. Esperar a la API por nginx con `GET /api/status` sin token: 401 significa que está arriba; 000/502/503/504 significa que sigue arrancando. Tope de 2 min; si se cumple, muestra `logs --tail 50 api` y sale con error. El puerto se lee de `WEB_PORT` en `.env` (por defecto 8090). `/` no sirve para esto: nginx responde 200 con `try_files` aunque la API esté caída.
6. `docker image prune -f` y `docker compose ps`.

Los scripts se invocan con `sh` porque git los guarda como `100644` (sin bit de ejecución, se editan desde Windows). En el índice están en LF, así que un clone en la Pi queda bien sin `.gitattributes`.

## Archivos
- `scripts/deploy.sh` (nuevo).
- `README.md`: sección "Actualizar en la Raspberry Pi" (uso, respaldo y reversión, `.env`, go2rtc, tiempos de compilación y RAM, acceso al repo).

## Pendiente / fuera de alcance
- Construir imágenes arm64 en el PC (`docker buildx`) y publicarlas en GHCR, para que la Pi solo haga `pull`. Conviene si compilar en la Pi resulta demasiado lento.
- El hook `move-plan.ps1` asigna cualquier plan al primer sprint reservado aunque no tenga relación (este cayó en el Sprint 9 y se movió a mano).

## Verificación
- `sh -n scripts/deploy.sh` (sintaxis) ✔.
- En la Pi: cambio trivial en el frontend → `sh scripts/deploy.sh web` → la web muestra el cambio y el script imprime "API arriba (HTTP 401)".
- Con la API detenida (`docker compose stop api` y luego `sh scripts/deploy.sh web`), el script debe esperar y fallar a los 2 min mostrando los logs.
