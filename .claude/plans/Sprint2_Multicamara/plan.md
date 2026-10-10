# Multicámara en CamGuard

## Context

Hoy CamGuard solo puede observar una cámara. El dominio ya es multicámara (`Camera` en BD; `CameraMonitorService`, `ActivationGate`, `RecordingService`, `UploadService`, `WebRtcSignaling` y `ListRecordings` trabajan por `CameraId`), pero el límite real está en:

1. `go2rtc.yaml` define un único stream fijo (`icsee`) armado con `CAMERA_IP/USER/PASSWORD` del `.env`.
2. La API no permite crear ni borrar cámaras (solo `PUT /cameras/{id}`); `DatabaseInitializer` siembra una sola.
3. La UI solo edita cámaras existentes, apila los vivos uno debajo de otro y Grabaciones no filtra por cámara.

Decisiones del usuario: **registro dinámico de streams en go2rtc desde la API** (la cámara se da de alta solo desde la web, credenciales en BD) y **cuadrícula responsiva** para el vivo.

## Backend

### 1. Credenciales de la cámara en BD (cifradas)
- `Common/Data/Entities.cs`: agregar a `Camera` `Username` (string, máx. 64) y `PasswordProtected` (string?, contraseña cifrada). Índice único en `Go2rtcStream` en `AppDbContext`.
- Cifrado con ASP.NET Data Protection: `Common/Security/CameraSecretProtector` (envuelve `IDataProtector` con propósito `"CamGuard.CameraPassword"`, métodos `Protect`/`Unprotect`). Llaves persistidas en `{DataDirectory}/keys` (`PersistKeysToFileSystem`), ya cubierto por el volumen `apidata`. Registrar en `Program.cs`.
- Migración `AddCameraCredentials` (`dotnet ef migrations add ... --output-dir Common/Data/Migrations`).
- La contraseña **nunca** sale en respuestas: `CameraResponse` expone `Username` y `HasPassword`.

### 2. Sincronización de streams con go2rtc (nuevo slice `Features/Streams/`)
- `Go2rtcStreamSource.For(Camera, string password)` — método `static` puro que arma `dvrip://user:pass@ip:port?channel=0&subtype=0` (usuario/contraseña con `Uri.EscapeDataString`). Probado sin infraestructura.
- `Go2rtcStreamsClient` (HttpClient tipado, misma base/auth básica que `Go2rtcClient` en [WebRtcSignaling.cs](backend/src/CamGuard.Api/Features/Live/WebRtcSignaling.cs) — extraer la configuración del HttpClient a un helper compartido para no duplicarla): `GetStreamsAsync` (`GET api/streams`), `PutStreamAsync(name, src)` (`PUT api/streams?name=&src=`), `DeleteStreamAsync(name)` (`DELETE api/streams?src=name`).
- `StreamSyncService : BackgroundService`: al arrancar y cada `Streams:SyncSeconds` (30 s) compara las cámaras habilitadas con `GET api/streams` y hace `PUT` de las que falten o cuyo origen cambió, y `DELETE` de streams que ya no correspondan a ninguna cámara. Esto cubre el reinicio de go2rtc (los streams agregados por API viven en memoria porque `go2rtc.yaml` va montado `:ro`). La decisión de qué agregar/quitar va en un método `static` puro `StreamSyncPlan.Compute(desired, current)` → `(toPut, toDelete)`.
- `StreamSyncTrigger` (singleton con `SemaphoreSlim`/`Channel`) para que los endpoints de cámaras pidan sincronizar de inmediato tras crear/editar/borrar.
- `AddStreamsFeature(config)` en `Program.cs`.

### 3. Endpoints de cámaras (`Features/Cameras/`)
- `CreateCamera.cs`: `POST /cameras` → 201 con la cámara. Request: `Name, Ip, DvripPort, Go2rtcStream, Username, Password, Enabled`. Reutiliza `UpdateCamera.Validate` (extenderla con `Username` obligatorio y `Password` obligatorio solo al crear) + chequeo de stream e IP+puerto duplicados (409/ValidationProblem).
- `UpdateCamera.cs`: aceptar `Username` y `Password?`; contraseña vacía/nula = conservar la actual. Dispara `StreamSyncTrigger`.
- `DeleteCamera.cs`: `DELETE /cameras/{id}`. Si la cámara tiene grabaciones responde 409 con "Deshabilítala para conservar su historial…" (la FK es cascade y borraría filas dejando objetos huérfanos en R2). Si no, borra y dispara sync. Además limpia `CameraStateStore` (agregar `Remove(id)`).
- `ListCameras.cs`: agregar `Username`, `HasPassword` al response.
- `CameraMonitorService` ya recarga cámaras cada `CameraRefreshSeconds`; quitar del diccionario `_trackers` las que ya no estén para no arrastrar estado.

### 4. Arranque y despliegue
- `DatabaseInitializer`: `CameraSeedOptions` gana `Username`/`Password`. Si la tabla está vacía siembra como hoy (ahora con credenciales cifradas); si ya existe una cámara con `Go2rtcStream == seed.Go2rtcStream` sin contraseña, le completa usuario/contraseña desde el seed (migración sin fricción de la instalación actual).
- `go2rtc.yaml`: quitar `streams:` fijo (dejar comentario de que los registra la API). `docker-compose.yml`: quitar `CAMERA_*` del servicio go2rtc; en `api` agregar `Camera__User`/`Camera__Password` (solo para la siembra/migración). `.env.example`: aclarar que `CAMERA_*` solo siembra la primera cámara; las demás se agregan desde Configuración.
- `appsettings.json`: sección `Streams` y `Camera.Username`.

## Frontend

- `src/api/types.ts`: `Camera` + `username`, `hasPassword`; `CameraInput` (= campos editables + `password?`).
- `src/api/client.ts`: `createCamera`, `updateCamera` (nuevo tipo), `deleteCamera`; `recordings` acepta `cameraId`.
- `features/settings/SettingsPage.tsx`: `CameraForm` reutilizable para crear/editar con campos Usuario y Contraseña (placeholder "Sin cambios" al editar), botón **Eliminar** con confirmación y mensaje del 409; botón **Agregar cámara** que abre un `CameraForm` vacío (stream sugerido a partir del nombre, editable). Invalidar `['cameras']` tras cada mutación.
- `features/live/HomePage.tsx`: envolver las secciones en `.camera-grid` (CSS grid en `index.css`: 1 columna en móvil, `repeat(auto-fill, minmax(420px, 1fr))` en escritorio). `LivePlayer` no cambia (ya es por cámara).
- `features/recordings/RecordingsPage.tsx` + `useRecordings.ts`: select "Cámara" en los filtros (parámetro `camera` en la URL, solo visible si hay más de una cámara), incluido en la queryKey de `['recordings', …]`.
- `RecordingCard`: mostrar `cameraName` cuando hay más de una cámara (verificar si ya lo hace).

## Pruebas (TUnit, carpetas espejo)

- `Features/Streams/Go2rtcStreamSourceTests.cs`: URL correcta, escape de caracteres especiales en la contraseña.
- `Features/Streams/StreamSyncPlanTests.cs`: agrega faltantes, actualiza origen cambiado, borra sobrantes, ignora deshabilitadas.
- `Features/Cameras/UpdateCameraTests.cs` / `CreateCameraTests.cs`: validación de usuario, contraseña obligatoria al crear y opcional al editar.
- `Features/Monitoring/CameraStateStoreTests.cs`: `Remove`.

## Verificación

1. `cd backend && dotnet build && dotnet test`.
2. `cd frontend && npm run build && npm run lint`.
3. `docker compose up -d --build` con la instalación actual: la cámara existente sigue grabando y en `http://<host>:1984` (API de go2rtc) aparece el stream `icsee` registrado por la API.
4. Agregar una segunda cámara desde Configuración → aparece en go2rtc (`GET /api/streams`), en la cuadrícula de inicio y, al despertar, graba un clip con su nombre en la notificación y en Grabaciones (filtrable por cámara).
5. `docker compose restart go2rtc` → en ≤30 s los streams vuelven a estar registrados.
6. Intentar eliminar una cámara con clips → 409 con mensaje; deshabilitarla → deja de monitorearse y su stream se quita de go2rtc.
7. Actualizar `CLAUDE.md` (flujo y `Features/Streams`) y `README.md` con cómo agregar cámaras.
