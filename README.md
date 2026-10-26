# CamGuard

Graba automáticamente cada activación por movimiento de una o varias cámaras iCSee a batería,
sube los clips a un bucket S3-compatible (Cloudflare R2) y permite verlos, junto con
el video en vivo, desde una web en React. Reemplaza la suscripción de la app iCSee.

Diseño y decisiones: [.claude/plans/](.claude/plans/) (un `plan.md` por sprint).

```
cámara ──DVRIP:34567──► go2rtc ──RTSP──► API (.NET 10 + FFmpeg) ──► R2
                          │                 │  SignalR / REST
                          └──WebRTC──────►  web (React, nginx) ◄── navegador
                                                 │ /auth
                                   Keycloak (login) ◄┘ ── Postgres
```

## Requisitos

- Un equipo siempre encendido con Docker: x86-64 o ARM64 (Raspberry Pi 4/5).
- Cada cámara con IP fija (reserva DHCP en el router).
- Un bucket en R2 (o B2, MinIO…) y, si quieres notificaciones, un tópico de ntfy (ver [docs/ntfy.md](docs/ntfy.md)).

## Puesta en marcha

1. Haz primero la prueba de concepto: [poc/README.md](poc/README.md).
2. `cp .env.example .env` y completa los valores.
3. `docker compose up -d --build`
4. Abre la web en `PUBLIC_URL` (siempre esa URL: es el issuer de los tokens) y entra con
   `CAMGUARD_ADMIN_USER`/`CAMGUARD_ADMIN_PASSWORD`. Esa contraseña es temporal: Keycloak
   pide cambiarla en el primer login.

go2rtc corre con `network_mode: host` (necesario en Linux para ver la cámara y servir
WebRTC). El navegador recibe el video directo de go2rtc por el puerto 8555, así que
`HOST_IP` debe ser una IP que el navegador alcance (la de la LAN o la de Tailscale).

### Usuarios y login (Keycloak)

El login lo hace Keycloak, publicado por nginx en `PUBLIC_URL/auth` con el tema de
CamGuard (`keycloak/theme`, hecho con Keycloakify). La web usa Authorization Code + PKCE
(`oidc-spa`) y la API valida los tokens del realm `camguard` (audience `camguard-api`).

- Consola: `PUBLIC_URL/auth/admin`, con `KC_ADMIN_USER`/`KC_ADMIN_PASSWORD` (realm *master*).
  Ahí se crean usuarios en el realm **camguard** (el registro público está desactivado).
- `keycloak/realm-camguard.json` se importa solo la primera vez: después, cambiar
  `PUBLIC_URL`, `CAMGUARD_ADMIN_*` o `SMTP_*` en `.env` no tiene efecto; hazlo en la consola
  (cliente `camguard-web` → redirect URIs; *Realm settings → Email*).
- "¿Olvidaste tu contraseña?" necesita `SMTP_*`. Los correos usan el tema de CamGuard.
- Tras varios intentos fallidos la cuenta se bloquea unos minutos (protección de fuerza bruta).

### Varias cámaras

`CAMERA_*` del `.env` solo siembra la primera cámara. Las demás se agregan en
**Configuración → Agregar cámara** (nombre, IP, puerto DVRIP, usuario y contraseña).
La API registra un stream por cámara habilitada en go2rtc y lo vuelve a registrar si
go2rtc se reinicia; no hace falta tocar `go2rtc.yaml`. Las contraseñas se guardan
cifradas con las llaves de `/data/keys` (volumen `apidata`): si se pierde ese volumen,
vuelve a escribirlas en Configuración.

Una cámara con grabaciones no se puede eliminar (sus clips quedarían huérfanos en R2):
deshabilítala para dejar de monitorearla y conservar el historial.

### Acceso remoto

- **Tailscale:** instala Tailscale en el equipo y en tus dispositivos; usa la IP de
  Tailscale en `HOST_IP` y en `PUBLIC_URL`. No se abre ningún puerto del router.
- **Cloudflare Tunnel:** publica `web:80` con `cloudflared`. El video en vivo
  necesita además llegar al puerto 8555 de go2rtc (TURN o Tailscale).

### Respaldo de la base

`scripts/backup-db.sh` crea volcados comprimidos de la base de CamGuard (`camguard-*.sql.gz`, Postgres) y de Keycloak
(`keycloak-*.sql.gz`, usuarios incluidos) en `./backups`, y borra los de más de 14 días. Prográmalo con cron (ver el encabezado del script).
Las llaves que cifran las contraseñas de las cámaras viven en el volumen `apidata` (`/data/keys`): respáldalo también.

### Migrar desde SQL Server (instalaciones hasta el Sprint 5)

CamGuard usaba SQL Server, que no corre en ARM. Para pasar los datos a Postgres una sola vez, en el equipo
que tiene la base anterior (necesita el SDK de .NET):

```sh
# .env: agrega DB_PASSWORD y conserva SQL_PASSWORD (la de antes)
docker compose stop api
docker compose --profile legacy-sqlserver up -d sqlserver db
cd backend && dotnet run --project tools/CamGuard.DbMigrator -- \
  --from "Server=127.0.0.1,14330;Database=CamGuard;User Id=sa;Password=<SQL_PASSWORD>;TrustServerCertificate=True" \
  --to   "Host=localhost;Port=5433;Database=camguard;Username=camguard;Password=<DB_PASSWORD>"
cd .. && docker compose stop sqlserver && docker compose up -d --build
```

La herramienta copia cámaras, grabaciones, eventos y ajustes conservando los Ids, y compara los conteos al
final. Si Postgres ya tiene datos se detiene (`--force` lo vacía y copia de nuevo). El volumen `sqldata` queda
intacto hasta que lo borres.

Para mudarte a otro equipo (p. ej. una Raspberry Pi): corre `scripts/backup-db.sh` en el actual, restaura
`camguard-*.sql.gz` en el nuevo (ver el encabezado del script) y copia el volumen `apidata`.

## Desarrollo

```sh
# Postgres local (127.0.0.1:5433, la de appsettings.Development.json)
docker run -d --name camguard-pg -e POSTGRES_DB=camguard -e POSTGRES_USER=camguard -e POSTGRES_PASSWORD=CamGuard_Dev_123 -p 127.0.0.1:5433:5432 postgres:17-alpine

# Keycloak (http://localhost:8180, publicado solo en 127.0.0.1); PUBLIC_URL apunta a Vite
PUBLIC_URL=http://localhost:5173 docker compose up -d --build keycloak

# API (http://localhost:5253) — usa appsettings.Development.json
cd backend && dotnet run --project src/CamGuard.Api

# Web (http://localhost:5173) — proxy de /api y /hubs hacia la API y de /auth hacia Keycloak
cd frontend && npm install && npm run dev
```

Usuario de desarrollo: el de `CAMGUARD_ADMIN_*` del `.env` (contraseña temporal en el primer
login). El `.env` necesita además `KC_DB_PASSWORD`, `KC_ADMIN_PASSWORD` y `CAMGUARD_ADMIN_EMAIL`.

Tema de Keycloak (`keycloak/theme`): `npm install && npm run dev` para ver una página con
datos de prueba (`http://localhost:5173/?page=login-update-password.ftl&message=error`), o `npm run start-keycloak` para
probarlo en un Keycloak real (necesita Maven y Docker). Los correos son FreeMarker en
`src/email`; antes de editar uno ajeno, `npx keycloakify own --path "email/..."`.

Pruebas: `cd backend && dotnet test` (TUnit).
