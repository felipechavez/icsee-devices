# CamGuard

Graba automáticamente cada activación por movimiento de una o varias cámaras iCSee a batería,
dispositivos Tuya con cámara y cámaras ONVIF enchufadas (p. ej. Imou), sube los clips a un bucket S3-compatible (Cloudflare R2) y permite verlos, junto con
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
- Para entrar desde el celular (e instalar la app): Tailscale con HTTPS (ver [docs/tailscale.md](docs/tailscale.md)).

## Puesta en marcha

1. Haz primero la prueba de concepto: [poc/README.md](poc/README.md).
2. `cp .env.example .env` y completa los valores.
3. `docker compose up -d --build`
4. Abre la web en `PUBLIC_URL` (siempre esa URL: es el issuer de los tokens) y entra con
   `CAMGUARD_ADMIN_USER`/`CAMGUARD_ADMIN_PASSWORD`. Esa contraseña es temporal: Keycloak
   pide cambiarla en el primer login.

go2rtc corre con `network_mode: host` (necesario en Linux para ver la cámara y servir
WebRTC). El navegador recibe el video directo de go2rtc por el puerto 8555, así que
`HOST_IP` debe ser una IP que el navegador alcance (la de la LAN o la de Tailscale; ver
[docs/tailscale.md](docs/tailscale.md)).

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
- **Administradores**: el rol de realm `camguard-admin` habilita lo administrativo, por ahora
  **Buscar en la red** en Configuración. El primer usuario lo trae en una instalación nueva. En una anterior
  (o para dárselo a otro usuario): `sh scripts/keycloak-admin-role.sh <usuario>`, que crea el rol si falta.
  Después, ese usuario debe cerrar sesión y volver a entrar. También se asigna en la consola: *Users → usuario →
  Role mapping*.
- **Solo lectura** (p. ej. para familiares): el rol de realm `camguard-viewer` solo deja ver las cámaras (en vivo)
  y las grabaciones; no ve Estado ni Configuración ni puede borrar clips (la API responde 403). Se asigna con
  `sh scripts/keycloak-admin-role.sh <usuario> viewer` (crea el rol si falta) o en la consola. Si el usuario
  también tiene `camguard-admin`, gana el de administrador. Un usuario sin ninguno de los dos sigue con acceso
  completo salvo lo de administrador.

### Varias cámaras

`CAMERA_*` del `.env` solo siembra la primera cámara. Las demás se agregan en
**Configuración → Agregar cámara** (nombre, IP, puerto DVRIP, usuario y contraseña).
La API registra un stream por cámara habilitada en go2rtc y lo vuelve a registrar si
go2rtc se reinicia; no hace falta tocar `go2rtc.yaml`. Las contraseñas se guardan
cifradas con las llaves de `/data/keys` (volumen `apidata`): si se pierde ese volumen,
vuelve a escribirlas en Configuración.

Una cámara con grabaciones no se puede eliminar (sus clips quedarían huérfanos en R2):
deshabilítala para dejar de monitorearla y conservar el historial.

#### Buscar cámaras nuevas en la red

Un administrador (rol `camguard-admin`) ve **Configuración → Buscar en la red**. La búsqueda tarda unos 20 s y lista
lo que aún no está agregado:

- **iCSee y ONVIF** en las subredes de `LAN_SUBNETS` (`.env`, en CIDR, p. ej. `192.168.1.0/24`; vacía = los /24 de las
  cámaras ya guardadas). La API corre en la red bridge de Docker y no ve la LAN por multicast, así que revisa
  cada dirección por TCP: el 34567 para DVRIP y el 80, 8000, 2020 y 8080 para ONVIF. Cada puerto abierto se
  confirma con el protocolo, sin probar contraseñas.
- **Tuya**: los dispositivos con cámara (`sp`, `videolock`) del proyecto de Tuya Cloud.

**Agregar** abre el alta precargada (tipo, IP, puerto, Device ID y nombre sugerido). Falta escribir la contraseña,
usar *Probar conexión* y guardar. Una iCSee a batería solo aparece si está despierta en ese momento: actívala con
movimiento o con el timbre justo antes de buscar.

### Cámaras ONVIF (Imou, Dahua…)

Una cámara enchufada con ONVIF, como la **Imou Cue 2**, se agrega con el tipo **ONVIF** en
Configuración:

- **IP**: la de la cámara. Resérvala en el router (DHCP estático), porque si cambia, CamGuard la pierde.
  Las Dahua/Imou responden también en `192.168.1.108`, la IP de fábrica, pero conviene usar la que asignó el router.
- **Puerto ONVIF**: 80.
- **Usuario**: `admin`.
- **Contraseña**: en Imou es el código de seguridad (*Safety Code*) de la etiqueta. Varios
  intentos fallidos bloquean la cuenta de la cámara por un rato.
- **Probar conexión** muestra el modelo y los streams; por ejemplo, "H.265 1920×1080 + H.264 640×480".

La cámara avisa el movimiento por ONVIF y CamGuard graba el stream principal (H.265, sin
recodificar) mientras dura, más 10 s (`Onvif:AsleepDelaySeconds`). El vivo usa el substream H.264 y se
puede abrir en cualquier momento. La sensibilidad se ajusta en la app del fabricante, y la detección de
movimiento debe estar activada ahí. Si alguien tapa o mueve la cámara, llega un aviso por ntfy.
En Estado, una cámara ONVIF se da por caída si pierde la conexión más de `Health:ServiceGraceMinutes`.

Los clips en H.265 se reproducen en Chrome, Edge, Safari, Android e iPhone, pero **no en Firefox**.
Para revisar qué expone una cámara nueva: `poc/imou/probe-device.ps1` y `poc/imou/onvif-events.ps1`.

### Acceso remoto

- **Tailscale (recomendado):** red privada entre el equipo y tus dispositivos, sin abrir
  puertos del router, y HTTPS con Tailscale Serve, necesario para instalar la app en el celular.
  Paso a paso en [docs/tailscale.md](docs/tailscale.md). En resumen: `HOST_IP` es la IP de
  Tailscale y `PUBLIC_URL` es `https://<equipo>.<tailnet>.ts.net`.
- **Cloudflare Tunnel:** publica `web:80` con `cloudflared`. El video en vivo
  necesita además llegar al puerto 8555 de go2rtc (TURN o Tailscale).

### App en el celular (PWA) y HTTPS con Tailscale Serve

CamGuard se instala como app desde el navegador del celular. Al mantener presionado el
ícono en Android aparecen los atajos **En vivo** (`/live`, mosaico de cámaras) y
**Último clip** (`/latest`). El navegador solo ofrece instalarla con **HTTPS**
(o en `localhost`). Con Tailscale Serve se obtiene HTTPS sin abrir puertos ni gestionar
certificados. La instalación, la configuración de `.env`, la actualización del realm de
Keycloak y la instalación en Android/iPhone están en [docs/tailscale.md](docs/tailscale.md).

La app no guarda datos sin conexión: el service worker (`frontend/public/sw.js`) solo muestra
una página "Sin conexión" cuando no hay red y nunca guarda `/api` ni `/auth`. Los íconos de
`frontend/public/icons/` se dibujan a partir de `favicon.svg`. Si cambia el logo, hay que
regenerarlos con el mismo diseño (PNG de 192, 512, 512 *maskable* con margen y 180 opaco para iOS).

El mosaico en vivo nunca conecta una cámara sola: cada cuadro se activa con "Ver en vivo".
El video se corta al salir de la app o bloquear la pantalla y se reanuda al volver, para no
mantener despierta una cámara a batería.

### Respaldo de la base

`scripts/backup-db.sh` crea volcados comprimidos de la base de CamGuard (`camguard-*.sql.gz`, Postgres) y de Keycloak
(`keycloak-*.sql.gz`, usuarios incluidos) en `./backups`, y borra los de más de 14 días.
`scripts/verify-backup.sh` prueba que el último se pueda restaurar: lo carga en un Postgres temporal (sin red ni
volumen, se borra al terminar), revisa migraciones, ajustes, cámaras y el realm de Keycloak, y deja el resultado en
`backups/last-verify.json`. Prográmalos juntos con cron (`crontab -e`):

```sh
0 3 * * * cd ~/icsee-devices && { sh scripts/backup-db.sh && sh scripts/verify-backup.sh; } >> backups/backup.log 2>&1
```

El panel **Estado** muestra la fila "Respaldo" (la API monta `./backups` en solo lectura) y abre un problema si no
hay respaldo de las últimas 36 h (el cron dejó de correr) o si la verificación falló o tiene más de 8 días.
Las llaves que cifran las contraseñas de las cámaras viven en el volumen `apidata` (`/data/keys`): respáldalo también.
Uso en Windows, restauración y el resto de los scripts: [scripts/README.md](scripts/README.md).

### Sin internet

"Sin internet" es que el router perdió la salida (o que R2/ntfy están caídos), no que se cayó el Wi-Fi: las iCSee
siguen hablando con el equipo por la red local. Lo que sigue funcionando:

- **Grabación, IA y vivo** de las iCSee, por la LAN. El login también (Keycloak valida en la red interna).
- **Clips en espera:** quedan en `Uploading` en el equipo y se suben solos al volver la red. Mientras R2 falle, la
  cola se pausa con backoff (5 s → 10 min) en vez de intentar cada clip. No se pueden reproducir desde la web hasta
  subirse.
- **Tope de disco:** si los clips en espera pasan *Espacio para clips sin subir* (Ajustes, 5 GB por defecto) o quedan
  menos de 512 MB libres (`Upload:MinFreeDiskMb`), se borran sin subir, primero los que la IA marcó "nada", luego los
  sin analizar, animales, personas y al final los con timbre o alarma; dentro de cada grupo, los más antiguos. Quedan
  como "Borrado sin subir" en Grabaciones.
- **Avisos:** los que no se pueden enviar esperan en una bandeja (`/data/notify-queue`, hasta 500) y salen al volver
  ntfy, con "Retrasado (hh:mm) · " si se atrasaron; los de más de 6 h (`Ntfy:MaxDelayHours`) se descartan.

Los dispositivos **Tuya** dependen de su nube: sin internet no avisan ni graban (Estado muestra "Sin conexión con
Tuya"). Un corte de luz o del router detiene todo: para eso, una UPS para el router y el equipo.

### Actualizar en la Raspberry Pi

Haz los cambios en tu PC y súbelos (`git push`) a la rama que sigue la Pi (normalmente `main`). En la Pi, por SSH:

```sh
cd ~/icsee-devices
sh scripts/deploy.sh           # git pull + docker compose up -d --build, y espera a la API
sh scripts/deploy.sh api web   # solo esos servicios
docker compose logs -f api     # ver el arranque
```

- El script respalda la base (`scripts/backup-db.sh`) si el pull trae migraciones; la API las aplica al arrancar.
  No hay vuelta atrás automática de una migración: para revertir, restaura ese respaldo.
- Si cambió `go2rtc.yaml`, reinicia go2rtc (el script lo hace solo).
- El `.env` vive solo en la Pi; `git pull` no lo toca. Si el cambio agrega variables (`.env.example`), edítalo allí antes.
- Compilar en la Pi es lento la primera vez, sobre todo Keycloak (Node + Maven) y la API (`dotnet publish`);
  después Docker reutiliza la caché. Con 2 GB de RAM conviene más swap. Alternativa: construir en el PC con
  `docker buildx build --platform linux/arm64` y publicar las imágenes en un registry.
- La Pi necesita acceso de lectura al repo (llave SSH o *deploy key* si es privado).

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

PWA: con Vite (`npm run dev`) el service worker no se registra, para que no cachee nada
mientras desarrollas. Para probar la instalación o la página sin conexión, usa el build de
`docker compose` en `http://localhost:8090`: Chrome considera `localhost` seguro aunque sea http.
Revisa en DevTools → *Application* → *Manifest* / *Service workers*. Si cambias `sw.js`, sube
`CACHE` para que los clientes instalados tomen la versión nueva.

Pruebas: `cd backend && dotnet test` (TUnit).
