# Scripts

Córrelos desde la raíz del repositorio. `backup-db.sh` lo exige; los demás se cambian a la raíz por su cuenta.
Los `.sh` son POSIX: en Linux o la Raspberry Pi, con `sh`; en Windows, con **Git Bash** (no desde PowerShell:
`find`, `gzip` y las tuberías binarias no funcionan igual). Los `.ps1` son para Windows.

| Script | Dónde | Para qué |
|---|---|---|
| [`backup-db.sh`](#backup-dbsh) | equipo de la casa | Respaldo diario de las bases de CamGuard y Keycloak |
| [`verify-backup.sh`](#verify-backupsh) | equipo de la casa | Prueba que el último respaldo se pueda restaurar |
| [`deploy.sh`](#deploysh) | Raspberry Pi | `git pull` y recrear los servicios |
| [`up.ps1`](#upps1) | Windows | Levantar todo con Docker Desktop, validando el `.env` |
| [`backup-local.ps1`](#backup-localps1) | Windows | Respaldar lo que git no guarda antes de formatear |

---

## `backup-db.sh`

Vuelca las dos bases Postgres a `backups/`, comprimidas con gzip:

| Archivo | Servicio | Contenido |
|---|---|---|
| `camguard-AAAAMMDD-HHMMSS.sql.gz` | `db` | cámaras, historial de clips, eventos, ajustes, alertas de salud |
| `keycloak-AAAAMMDD-HHMMSS.sql.gz` | `keycloak-db` | usuarios, credenciales y sesiones del realm `camguard` |

Después borra los respaldos con más de `KEEP_DAYS` días (14 por defecto).

No respalda los videos (están en R2) ni el volumen `apidata`, donde viven las llaves que cifran las contraseñas
DVRIP (`/data/keys`) y los clips que todavía no se suben. Para eso, ver [`backup-local.ps1`](#backup-localps1) o
copia el volumen a mano.

### Requisitos

- Los servicios `db` y `keycloak-db` corriendo (`docker compose up -d`). Si alguno está detenido, el script falla
  (`set -eu`) y no borra respaldos viejos.
- Ejecutarlo desde la raíz del repo: usa rutas relativas y el `docker-compose.yml` de esa carpeta.
- Permiso de escritura en `backups/`. Si Docker creó la carpeta al montarla en la API, queda de root:
  `sudo chown -R "$USER" backups` (`deploy.sh` la crea antes para evitarlo).

### Uso

```sh
cd ~/icsee-devices              # en Windows (Git Bash): cd /d/Desarrollo/icsee-devices
sh scripts/backup-db.sh
KEEP_DAYS=30 sh scripts/backup-db.sh   # conservar 30 días
```

Desde PowerShell, a través de Git Bash:

```powershell
& "C:\Program Files\Git\bin\sh.exe" scripts/backup-db.sh
```

Salida esperada:

```
Respaldo creado: backups/camguard-20261108-030000.sql.gz
Respaldo creado: backups/keycloak-20261108-030000.sql.gz
```

### Programarlo

**Linux / Raspberry Pi** (`crontab -e` con tu usuario, que debe estar en el grupo `docker`), junto con la
verificación. Las llaves hacen que el log reciba la salida de los dos scripts, no solo la del último:

```sh
0 3 * * * cd ~/icsee-devices && { sh scripts/backup-db.sh && sh scripts/verify-backup.sh; } >> backups/backup.log 2>&1
```

cron corre aunque no haya sesión iniciada, pero si el equipo está apagado a esa hora se salta ese día (el panel
lo avisa a las 36 h).

**Windows** (Programador de tareas → Crear tarea):

- Programa: `C:\Program Files\Git\bin\sh.exe`
- Argumentos: `-c "{ sh scripts/backup-db.sh && sh scripts/verify-backup.sh; } >> backups/backup.log 2>&1"`
- Iniciar en: la carpeta del repo (p. ej. `D:\Desarrollo\icsee-devices`)
- Configuración: marca "Ejecutar la tarea lo antes posible si se omitió un inicio programado" y, en un notebook,
  desmarca "Iniciar la tarea solo si el equipo está conectado a la corriente alterna". Corre solo con la sesión
  iniciada, como Docker Desktop.

El panel **Estado** vigila que esto siga corriendo: abre el problema `backup` si el último `camguard-*.sql.gz`
tiene más de 36 h (`Health:BackupMaxAgeHours`). La API lee `./backups` montado en solo lectura
(`Health:BackupDirectory`; vacío = no se revisa).

### Restaurar

Hazlo desde `sh`/Git Bash. En PowerShell 5.1 la tubería convierte los bytes en texto y daña el gzip.

**CamGuard.** El volcado lleva `--clean --if-exists`: borra y recrea las tablas, así que se puede cargar sobre la
base existente. Detén la API para que no escriba mientras tanto:

```sh
docker compose stop api
gunzip -c backups/camguard-<fecha>.sql.gz | docker compose exec -T db psql -U camguard -d camguard
docker compose start api
```

**Keycloak.** Este volcado no lleva `--clean`: cárgalo en una base vacía y con Keycloak detenido.

```sh
docker compose stop keycloak
docker compose exec -T keycloak-db psql -U keycloak -d postgres \
  -c "DROP DATABASE keycloak" -c "CREATE DATABASE keycloak OWNER keycloak"
gunzip -c backups/keycloak-<fecha>.sql.gz | docker compose exec -T keycloak-db psql -U keycloak -d keycloak
docker compose start keycloak
```

Si el respaldo es de antes de un deploy con migraciones, la API las vuelve a aplicar al arrancar.

---

## `verify-backup.sh`

Prueba que el respaldo se pueda restaurar, sin tocar las bases reales:

1. Revisa la integridad de los dos `.sql.gz` (`gzip -t`).
2. Levanta un Postgres temporal (`postgres:17-alpine`, sin red ni volumen; se borra al terminar).
3. Restaura ambos volcados.
4. Comprueba que haya migraciones de EF, la fila de `Settings`, al menos una cámara, el realm `camguard` y al
   menos un usuario. Si la base en vivo tiene una migración más nueva, lo agrega como aviso.

El resultado queda en `backups/last-verify.json`:

```json
{"verifiedAt":"2026-11-08T06:00:12Z","ok":true,"file":"camguard-20261108-030000.sql.gz","detail":"2 cámaras, 418 clips, migración …; Keycloak con 1 usuarios"}
```

Sale con 0 si todo está bien y con 1 si algo falla. El panel **Estado** abre el problema `backup-verify` si dice
`ok: false` o tiene más de 8 días (`Health:BackupVerifyMaxAgeDays`).

```sh
sh scripts/verify-backup.sh                                       # el más nuevo de backups/
sh scripts/verify-backup.sh backups/camguard-20261101-030000.sql.gz   # uno en particular (busca su keycloak-*)
```

La primera vez descarga la imagen de Postgres, así que necesita internet.

---

## `deploy.sh`

Actualiza el equipo de la casa con lo último de la rama actual:

1. `git pull --ff-only`.
2. Si llegaron migraciones de EF, corre `backup-db.sh` antes (la API las aplica al arrancar y no se revierten
   solas).
3. `docker compose up -d --build [servicios]`. Reinicia go2rtc si cambió `go2rtc.yaml`.
4. Espera hasta 2 min a que la API responda por nginx (`/api/status`) y muestra los logs si no lo hace.
5. Limpia imágenes huérfanas y muestra `docker compose ps`.

```sh
sh scripts/deploy.sh            # todo
sh scripts/deploy.sh api web    # solo esos servicios
```

No toca el `.env`: si el cambio agrega variables (ver `.env.example`), edítalo antes. Más detalles en
[README.md › Actualizar en la Raspberry Pi](../README.md#actualizar-en-la-raspberry-pi).

---

## `up.ps1`

Levanta CamGuard en Windows con Docker Desktop:

- Comprueba que Docker esté corriendo y que exista el `.env`. Si no existe, lo crea desde `.env.example` y se
  detiene para que lo completes.
- Valida las variables obligatorias. `PUBLIC_URL` no puede terminar en `/`, y `GO2RTC_PASSWORD` y `DB_PASSWORD`
  solo pueden llevar letras y números.
- Si `HOST_IP` ya no es una IP de este equipo, la reemplaza en el `.env` por la IP de la LAN (de lo contrario, el
  vivo por WebRTC no conecta).
- Revisa que `WEB_PORT` (8090 por defecto) esté libre.
- Usa `docker-compose.yml` + `docker-compose.windows.yml`, porque en Docker Desktop `network_mode: host` no expone
  go2rtc.
- Espera a que la web responda.

```powershell
.\scripts\up.ps1                  # construye y levanta
.\scripts\up.ps1 -NoBuild -Logs   # sin reconstruir; sigue los logs de la API
.\scripts\up.ps1 -TimeoutSeconds 300
```

En Windows, los comandos `docker compose` sueltos también necesitan el override:
`$env:COMPOSE_FILE = 'docker-compose.yml;docker-compose.windows.yml'`.

---

## `backup-local.ps1`

Respaldo completo antes de formatear o cambiar de equipo (también se puede pedir con el comando `/formatear` de
Claude Code). Crea `camguard-respaldo-<fecha>/` dentro de `-Destino` con:

- `repo\`: archivos ignorados, sin versionar o modificados (`.env`, `poc\…`), sin `bin`, `obj`, `node_modules`
  ni `dist`.
- `claude\`: `CLAUDE.md`, `settings.json`, skills y memorias de este proyecto (sin credenciales).
- `docker\`: volcados de CamGuard y Keycloak y el volumen `apidata` (llaves y clips en cola).
- `LEEME.txt` con los pasos para restaurar y `MANIFIESTO.csv` con el SHA256 de cada archivo.

Verifica cada copia por hash, no borra nada y sale con 1 si algo quedó sin respaldar. También avisa si hay
commits sin subir o entradas en `git stash`.

```powershell
.\scripts\backup-local.ps1 -Destino E:\Respaldos
.\scripts\backup-local.ps1 -Destino "$env:USERPROFILE\OneDrive\Respaldos" -PermitirDiscoDelSistema
```

El destino no puede estar dentro del repo ni, salvo que pases `-PermitirDiscoDelSistema`, en el disco del sistema
o del repo. **El respaldo contiene secretos en claro**: guárdalo en un lugar privado.
