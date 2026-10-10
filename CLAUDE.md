# CamGuard

Grabación por movimiento de cámaras iCSee a batería (una o varias). go2rtc mantiene la única conexión con cada cámara (DVRIP :34567) y la reparte por RTSP (grabación) y WebRTC (vivo). Los streams de go2rtc no están en `go2rtc.yaml`: los registra la API (slice `Streams`) a partir de las cámaras en BD. Diseño completo en `.claude/plans/Sprint1_Visualizacion/plan.md` (planes por sprint en `.claude/plans/`); resultados de campo en `poc/README.md`.

## Estructura

- `backend/` — .NET 10, un solo proyecto `CamGuard.Api` organizado en **vertical slices**. Pruebas en `tests/CamGuard.Tests` con **TUnit**.
- `frontend/` — React + Vite + TypeScript, TanStack Query, React Router, SignalR. nginx lo sirve y hace proxy de `/api` y `/hubs`.
- `keycloak/` — imagen de Keycloak: `realm-camguard.json` (realm, cliente `camguard-web`, primer usuario; se importa solo si el realm no existe) y `theme/` (Keycloakify: login en `src/login` sin CSS de Keycloak: `Template.tsx` propio, clases `cg-*` mapeadas en `KcPage.tsx` y estilos en `main.css`, correos FreeMarker en `src/email`). nginx lo publica en `/auth`.
- `docker-compose.yml`, `go2rtc.yaml`, `.env.example` — despliegue. `poc/` — scripts de la fase 1. `scripts/backup-db.sh` — respaldo de SQL Server.

## Comandos

```sh
cd backend && dotnet build                 # SDK fijado en global.json (10.0.x)
cd backend && dotnet test                  # TUnit sobre Microsoft.Testing.Platform (global.json "test.runner")
cd backend && dotnet ef migrations add <Nombre> --project src/CamGuard.Api --output-dir Common/Data/Migrations
cd frontend && npm run build && npm run lint   # tsc + vite, oxlint
cd keycloak/theme && npm run build             # tema; el .jar lo genera keycloak/Dockerfile (Maven)
docker compose up -d --build
```

`dotnet ef` es una herramienta local (`backend/dotnet-tools.json`): usa `dotnet tool restore` si falta. Las migraciones se aplican solas al arrancar (`DatabaseInitializer`), con reintentos mientras SQL Server levanta.

## Backend: vertical slices

- `Features/<Slice>/` contiene todo lo de una capacidad: endpoint, request/response, validación, lógica y su `BackgroundService`. No hay capas Core/Infrastructure ni MediatR.
- Endpoints: clase anidada `Endpoint : IEndpoint` dentro de una clase estática por caso de uso (`ListRecordings`, `UpdateSettings`…). Se descubren por reflexión (`AddEndpoints`/`MapEndpoints`) bajo el grupo `/api`. Un archivo nuevo con `IEndpoint` queda registrado solo.
- Cada slice con servicios expone `Add<Slice>Feature(...)`, que se llama en `Program.cs`.
- `Common/` solo para lo compartido de verdad: `AppDbContext` y entidades, `IVideoStorage` (S3/R2), `INotifier` (ntfy), `EventsHub` (SignalR), validación de tokens de Keycloak (`AuthSetup`), opciones y `CameraEventChannel`.
- Entre slices: Monitoring → Capture por `CameraEventChannel` y Monitoring → Devices por `CameraAwakeChannel` (aviso de "despertó"); Capture → Upload por `UploadQueue`; Cameras y Live leen `CameraStateStore` (Monitoring); Cameras → Streams por `StreamSyncTrigger`.
- Devices: `DvripClient` (protocolo Sofia, puerto 34567) lee modelo, batería y SD y aplica la sensibilidad pendiente **solo cuando la cámara ya despertó** (`DeviceInfoService`, nunca la despierta). Las claves DVRIP de batería y detección dependen del firmware: se prueban en orden (`DvripQueries`) y lo que no exista queda `null`. En Development, `GET /api/cameras/{id}/dvrip-probe` devuelve las respuestas crudas para descubrirlas. "Probar conexión" es `POST /api/cameras/test`.
- Streams: `StreamSyncService` registra con `PATCH /api/streams` (no `PUT`: ese intenta escribir `go2rtc.yaml`, montado `:ro`) un stream por cámara habilitada cada `Streams:SyncSeconds` y al pedirlo `StreamSyncTrigger`; quita los que no son de ninguna cámara. La decisión es `StreamSyncPlan.Compute` (pura).
- La contraseña DVRIP de cada cámara va cifrada en BD (`CameraSecretProtector`, Data Protection con llaves en `{DataDirectory}/keys`) y nunca sale en una respuesta (`HasPassword`). No registres el origen `dvrip://` en logs: lleva la contraseña.
- La lógica pura queda en métodos `static` o en clases sin dependencias (`CameraStateTracker`, `ActivationGate`, `RetryPolicy`, `RetentionPolicy`, `Validate(...)`, `FfmpegRecorder.RecordArguments`) para probarla sin infraestructura. Mantén ese patrón al agregar reglas.
- Todo endpoint exige un access token de Keycloak (fallback policy; issuer `Auth:Authority`, audience `camguard-api`). Lo público lleva `.AllowAnonymous()` explícito. No hay login propio ni usuarios en la BD: los usuarios viven en Keycloak.
- Usa `TimeProvider` (inyectado) en lugar de `DateTime.Now`. La lógica pura recibe `now` por parámetro.

## Flujo de una activación

`CameraMonitorService` sondea TCP cada 1 s (despierta con 1 éxito, dormida tras `AsleepAfterFailures` fallos) → `RecordingService` respeta `ActivationGate` (una grabación por cámara + enfriamiento) y lanza FFmpeg contra `rtsp://go2rtc:8554/<stream>` con tope `MaxClipSeconds`; al dormirse o al apagar el host le escribe `q` por stdin para cerrar el MP4 limpiamente → `UploadQueue` (JSON en `{DataDirectory}/queue`) → `UploadService` sube a R2 con backoff infinito, marca `Uploaded`, emite `RecordingCompleted` y notifica por ntfy. El archivo local se borra solo tras confirmar la subida.

## Detalles que ya causaron bugs

- Formatos de fecha en claves o nombres de archivo: usa `CultureInfo.InvariantCulture`. En un formato, `/` es el separador de la cultura actual (en es-* puede ser `-`).
- AWS SDK v4 con R2: checksums en `WHEN_REQUIRED`; `DisablePayloadSigning` y el protocolo de las URLs prefirmadas dependen de que el endpoint sea https (el SDK firma con https por defecto).
- nginx debe resolver `api` en cada petición (`resolver 127.0.0.11` + variable en `proxy_pass`); si no, da 502 tras recrear el contenedor de la API.
- FFmpeg: el audio G.711 de las iCSee no cabe en MP4, por eso es `-c:v copy -c:a aac`. No mates el proceso: un MP4 sin cerrar no tiene `moov atom` y no se puede reproducir.
- Keycloak: `KC_HOSTNAME=${PUBLIC_URL}/auth` fija el issuer; la web debe abrirse con `PUBLIC_URL` y la API lee discovery/JWKS por `keycloak:8080` (`KC_HOSTNAME_BACKCHANNEL_DYNAMIC`). En el realm importado, `temporary: true` en la credencial no fuerza el cambio: hace falta `requiredActions: ["UPDATE_PASSWORD"]`. Un `SMTP_FROM` vacío hace fallar el import.
- go2rtc no usa trickle ICE: el `LivePlayer` espera a que termine la recolección de candidatos antes de enviar la oferta.

## Frontend

- `src/main.tsx` llama a `oidcEarlyInit` antes de cargar nada y `bootstrap.tsx` espera a `initOidc()` (`src/auth/oidc.ts`, `autoLogin`: sin sesión redirige a Keycloak). `src/api/client.ts` pide el token a oidc-spa en cada petición (lo renueva solo); un 401 cierra la sesión.
- `src/api/types.ts` (espejo de los DTO de la API; enums como string).
- Estilo: `src/index.css` con los tokens del rediseño (`designs/CamGuard_Rediseño`), oscuro por defecto y claro con `html[data-theme=light]` (`useTheme`, guardado en localStorage). Fuentes Geist autoalojadas (`@fontsource-variable`). Sin librerías de UI: clases CSS planas.
- `EventsHubProvider` mantiene una sola conexión de SignalR y actualiza la caché de TanStack Query: `['cameras']` con `CameraAwake`/`CameraAsleep`; invalida `['recordings']` con `RecordingCompleted`.
- Features en `src/features/<área>/`. Se prefiere estado derivado en lugar de `setState` dentro de `useEffect` (lo marca oxlint).
- Las URLs prefirmadas caducan (`Storage:PresignedUrlMinutes`): no se cachean entre aperturas del reproductor.

## Planes (modo plan)

- Todo plan queda en `.claude/plans/Sprint<N>_<Nombre>/plan.md` (`<Nombre>` corto en PascalCase y en español, sin tildes), p. ej. `Sprint1_Visualizacion/plan.md`, `Sprint2_Multicamara/plan.md`.
- El sprint es el siguiente al mayor que exista en `.claude/plans/`, salvo que el usuario indique otro o el plan amplíe uno existente.
- `plansDirectory` (`.claude/settings.json`) guarda el borrador en `.claude/plans/` con nombre aleatorio; al aprobarse, el hook `PostToolUse` de `ExitPlanMode` ejecuta `.claude/hooks/move-plan.ps1`, que lo mueve a `Sprint<N>_<Nombre>/plan.md` (nombre tomado del primer `#` del plan). No lo muevas a mano; si quedó un plan suelto, corre el script.

## Convenciones

- Código, nombres y comentarios: identificadores en inglés y textos visibles, logs y comentarios en español.
- Pruebas TUnit: `[Test]`, `[Arguments]`, `await Assert.That(...)`, nombres `Metodo_Escenario_Resultado`, carpetas espejo de `Features/`.
- Secretos solo en `.env` (ignorado por git) o variables de entorno; nunca en `appsettings.json`. `appsettings.Development.json` lleva solo valores de desarrollo.
