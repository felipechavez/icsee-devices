# Sprint 4 — Rediseño

> Al aprobarse se mueve a `.claude/plans/Sprint4_Rediseno/plan.md`.

## Contexto

Hay un rediseño completo en `designs/CamGuard_Rediseño/` (`Login.dc.html` y `CamGuard.dc.html`, prototipos con datos simulados): estética oscura/clara estilo "consola de seguridad" con Geist / Geist Mono, acento rojo, tarjetas de cámara con miniatura, línea de tiempo diaria y configuración con barra de "Cambios sin guardar". El sitio actual (frontend React + tema Keycloakify) usa otro estilo (azul, system-ui) y no muestra varios datos que el diseño pide.

Decisiones del usuario:
- **Implementar todo el diseño**, incluidos los datos que hoy no existen: batería, almacenamiento (SD), modelo, sensibilidad al movimiento, "Probar conexión" y último fotograma por cámara → requiere backend (cliente DVRIP + migración).
- **Login**: panel izquierdo con imagen estática (`thumb.png` del diseño) borrosa en mosaico + obturador de privacidad animado al escribir la contraseña, dentro del tema Keycloakify.

Alcance: login (Keycloak) → Inicio → Grabaciones → reproductor → Configuración. Los correos FreeMarker quedan fuera.

## Adaptaciones obligadas (el prototipo no es 1:1 con la realidad)

- **No se puede despertar una cámara a batería desde la web** (el puerto DVRIP está cerrado mientras duerme). En la vista de una cámara dormida se muestra "La cámara está en reposo · se activa sola al detectar movimiento" **sin** botón "Ver en vivo". Despierta → conecta sola como hoy (`LivePlayer`), con "Detener" y "Ver en vivo" para reanudar.
- **Estados de cámara** (derivados): `Pausada` (`!enabled`), `Grabando` (despierta + grabación en curso), `Despierta`, `En reposo`. El estado "Sin conexión" del prototipo se muestra solo como resultado de "Probar conexión" y como aviso de batería baja (< 15 %), no como estado permanente (no se distingue dormida de apagada).
- **Batería/almacenamiento/modelo** solo se pueden leer mientras la cámara está despierta: se leen al despertar y se guarda el último valor con su fecha ("hace 2 h"). Si el firmware no expone un dato, la UI lo oculta (campos `null`).
- **Sensibilidad** se guarda en BD y se aplica a la cámara por DVRIP la próxima vez que despierte (UI: "Se aplicará cuando la cámara despierte" mientras esté pendiente).
- El campo **Stream go2rtc** no está en el diseño pero es necesario: se mantiene dentro del panel expandido de la cámara (auto-derivado del nombre en altas, como hoy).
- El reproductor usa los controles nativos de `<video>` (la barra del prototipo es maqueta); se agregan "‹ Anterior / Siguiente ›" dentro de la lista visible.
- Login: se omite "v2.4.1" y la leyenda "CIFRADO AES-256 · TLS 1.3" (afirmación no verificable); se usa "ACCESO PRIVADO · CONEXIÓN SEGURA".

## Fase 0 — Verificación DVRIP en la cámara real (antes de la UI de datos)

Las claves de batería y sensibilidad varían por firmware XM/iCSee. Se implementa primero `DvripClient` y un endpoint **solo en Development** `GET /api/cameras/{id}/dvrip-probe` que, con la cámara despierta, devuelve el JSON crudo de: `SystemInfo`, `StorageInfo`, y los candidatos de batería (`Dev.ElectCapacity`, `Dev.PowerInfo`, `Status.Battery`…) y de detección (`Detect.MotionDetect`, `Detect.PIRDetect`…). Con eso se fijan las claves en `DvripQueries` (constantes) y se documenta el hallazgo en `poc/README.md`. Si alguna no existe, ese dato queda `null` y la UI lo oculta.

## Backend

### Nuevo slice `Features/Devices/`
- `DvripClient.cs` — cliente mínimo del protocolo Sofia (TCP :34567): cabecera de 20 bytes (0xFF, sesión, secuencia, msgId, longitud) + JSON. Operaciones: `LoginAsync` (msg 1000, hash Sofia de la contraseña: MD5 → 8 caracteres de `[0-9A-Za-z]`), `GetInfoAsync(name)` (1020), `GetConfigAsync(name)` (1042), `SetConfigAsync(name, json)` (1040), `LogoutAsync`. Timeout corto (5 s). Sin dependencias de ASP.NET; el codificador de tramas y el hash como métodos `static` puros para TUnit.
- `DeviceInfoParser.cs` (puro): JSON crudo → `DeviceSnapshot(BatteryPercent?, StorageUsedBytes?, StorageTotalBytes?, Model?, FirmwareVersion?)`.
- `SensitivityMap.cs` (puro): `Low/Medium/High` ↔ nivel del firmware (p. ej. 2/4/6 en `MotionDetect.Level`).
- `DeviceInfoService : BackgroundService` — al recibir "despertó" (ver abajo) espera `Devices:QueryDelaySeconds` (por defecto 3 s, para no competir con el arranque de go2rtc/FFmpeg), abre una sesión DVRIP corta, lee el snapshot, aplica la sensibilidad si está pendiente, guarda y emite `CameraInfoUpdated` por SignalR. Desactivable con `Devices:Enabled`.
- `TestConnection.cs` — `POST /api/cameras/test` con `{ cameraId?, ip, dvripPort, username, password? }` (contraseña vacía + `cameraId` → usa la guardada). Intenta login DVRIP y devuelve `{ ok, message, model? }` ("Conexión OK", "Sin respuesta: dormida o fuera de la red", "Usuario o contraseña incorrectos"). No registrar el origen ni la contraseña.
- `AddDevicesFeature(...)` en `Program.cs`.

### Eventos
- `CameraEventChannel` es `SingleReader` (lo consume Capture). Se agrega un segundo canal `CameraAwakeChannel` en `Common/Events` que `CameraMonitorService` también escribe al despertar, y que lee `DeviceInfoService` (sigue el patrón de canales entre slices).
- `EventsHub`: nuevo `CameraInfoUpdated(CameraInfoMessage)`.

### Datos (migración `AddDeviceInfo`)
- `Camera`: `Model` (string?, 100, editable), `MotionSensitivity` (enum `Low|Medium|High`, por defecto `Medium`), `SensitivityPending` (bool).
- `Camera` también: `BatteryPercent` (int?), `StorageUsedBytes`/`StorageTotalBytes` (long?), `DeviceInfoAt` (DateTimeOffset?). (Una fila por cámara: no hace falta tabla aparte.)
- `CameraInput` + `Validate`: `Model`, `MotionSensitivity`; `ApplyTo` marca `SensitivityPending = true` si cambia.
- `ListCameras.CameraResponse` agrega: `Model`, `MotionSensitivity`, `SensitivityPending`, `BatteryPercent`, `StorageUsedBytes`, `StorageTotalBytes`, `DeviceInfoAt`, `IsRecording` (desde `ActivationGate`/estado de Capture vía un store de solo lectura), `LastRecordingAt`, `LastThumbnailUrl` (prefirmada del último clip `Uploaded` con miniatura), `ClipsToday` (rango del día lo manda el cliente: `?todayFrom=&todayTo=` para respetar la zona local; si falta, se omite).

### Grabaciones
- `ListRecordings`: parámetro `status` (`Failed`) para el filtro "Fallidos".
- Nuevo `GET /api/recordings/timeline?from&to&cameraId` → `[{ id, cameraId, startedAt, status }]` sin paginar (tope 2000) para la barra de 24 h y el histograma "Actividad hoy".
- `GET /api/recordings/summary` → `{ totalCount }` para "N clips guardados · se conservan X días".

### Pruebas TUnit (`tests/CamGuard.Tests/Features/Devices/…`)
- `DvripFrame_Encode_Decode_RoundTrip`, `SofiaHash_KnownPassword_MatchesExpected` (vector conocido de go2rtc/python-dvrip), `DeviceInfoParser_*` (con JSON capturado en Fase 0), `SensitivityMap_*`, `CameraInput.Validate` con los campos nuevos, filtro `status` en `ListRecordings` (lógica pura si se extrae).

## Frontend (`frontend/`)

### Base visual
- `src/index.css` reescrito con los tokens del diseño (`--bg`, `--surface…`, `--line…`, `--text…`, `--muted`, `--dim`, `--red`, `--red-text`, `--green`, `--amber`, `--scrim`…) para `[data-theme=dark]` (por defecto) y `[data-theme=light]`, más clases de componentes (`.btn`, `.btn-primary` blanco/negro, `.seg` segmentado, `.pill`, `.chip`, `.stepper`, `.switch`, `.card-clip`, `.thumb-failed` con rayas, `.save-bar`, `.toast`, `.overlay`). Se mantiene CSS plano (sin librería nueva de estilos).
- Fuentes autoalojadas: `@fontsource-variable/geist` y `@fontsource-variable/geist-mono` importadas en `main.tsx` (sin Google Fonts: el sitio es privado y debe funcionar sin depender de terceros).
- `src/hooks/useTheme.ts` — `oscuro|claro` en `localStorage` (try/catch), aplica `document.documentElement.dataset.theme`; `index.html` lleva un script inline mínimo para evitar el parpadeo.

### Componentes nuevos/compartidos (`src/components/`)
- `Layout.tsx` — header sticky translúcido: marca (punto rojo), nav (`NavLink` con fondo `--line` en activo), selector Oscuro/Claro, reloj `HH:mm:ss` (Geist Mono, `useClock`), aviso de hub desconectado y "Salir".
- `BrandMark`, `StatusPill` (reemplaza `CameraStatusBadge`, con los estados derivados), `BatteryBadge` (color por umbral 15/40), `MeterBar`, `Segmented`, `Stepper`, `Switch`, `SaveBar`, `Toast` (`useToast` simple con contexto), `ClipThumb` (miniatura / "Subiendo…" / rayas "No se pudo grabar").
- `src/lib/cameraStatus.ts` (puro): `Camera` → `{ key, label, color }`; `src/lib/format.ts`: `formatClock`, `formatAgo` ("hace 12 min"), `dayLabel(offset)` ("Hoy", "Ayer", "mié 5 oct"), `hourBuckets(timeline)`.

### Inicio (`features/live/HomePage.tsx`)
- Más de una cámara y sin `?camera=` → **cuadrícula**: título "Cámaras" + resumen ("4 cámaras · 1 grabando"), botón "Administrar" → `/settings`; `CameraCard` con `LastThumbnailUrl` (gris si en reposo, borde rojo si graba), pill de estado, batería, nombre, `Modelo · IP`, "Último movimiento" y "N clips hoy".
- Una cámara o `?camera=id` → **vista individual**: "‹ Todas", nombre, modelo, pill; `LivePlayer` restilizado (overlay "EN VIVO · reloj" con punto pulsante, "Detener"; dormida → último fotograma borroso + mensaje + "Último fotograma · hora"); aside "Estado" (batería con barra, almacenamiento `x / y GB`, Red = IP, "leído hace…") y "Actividad hoy" (histograma 24 barras desde `/timeline`).
- "Últimos clips" (5) con "Ver todos →".

### Grabaciones (`features/recordings/`)
- Encabezado "Grabaciones" + "N clips guardados · se conservan X días"; segmentado "Todos · n / Fallidos · n"; navegador de día "‹ Hoy ›" (no permite futuro, no antes de la retención); chips de cámara si hay más de una. Todo en la URL (`day`, `camera`, `status`, `clip`) como hoy, para que siga funcionando el enlace de ntfy.
- `DayTimeline` — barra de 24 h con marcas clicables (rojas = fallidos) desde `/timeline`.
- Lista del día agrupada **por hora** (columna `HH:00` sticky) con `RecordingCard` nuevo; "Cargar más" se conserva (paginación del día).
- `ClipPlayerModal` → overlay a pantalla completa con `--scrim` y blur: hora (mono), "cámara · día", "‹ Anterior / Siguiente › / Cerrar", video, y "Descargar" / "Eliminar" (borde `--danger-line`). Clip fallido → panel rayado con explicación. Se sigue usando `<dialog>` (`Modal.tsx`) para foco/Esc.

### Configuración (`features/settings/SettingsPage.tsx`)
- Dos secciones con columna descriptiva a la izquierda y separador.
- **Grabación**: tres filas con `Stepper` (−/valor/unidad/+) para tope (paso 10 s), pausa (5 s) y retención (1 día), con los límites de `UpdateSettings`.
- **Cámaras**: "N cámaras" + "+ Agregar cámara" (abre una tarjeta nueva expandida arriba); acordeón por cámara (miniatura, nombre, estado · IP, batería, `+/−`). Expandida: Nombre, Modelo, Dirección IP, Puerto DVRIP, Usuario, Contraseña ("sin cambios"), Stream go2rtc, Sensibilidad (Baja/Media/Alta + aviso de pendiente), switch "Grabar al detectar movimiento" (= `enabled`); pie con "Eliminar cámara" y "Probar conexión" (→ "Probando…" / "Conexión OK · modelo" / mensaje de error).
- **Barra flotante "Cambios sin guardar · Descartar · Guardar"** que cubre ajustes + todas las cámaras editadas: estado de borradores en la página (`drafts: Record<id, CameraInput>`, derivado frente a los datos del servidor, sin `setState` en `useEffect`); Guardar ejecuta en orden `updateSettings` y los `updateCamera`/`createCamera` cambiados; errores de validación (400/409) se muestran en el campo de la cámara correspondiente y la barra queda visible; éxito → toast "Cambios guardados". Aviso `beforeunload` si hay cambios.

### Datos y tiempo real
- `api/types.ts` y `api/client.ts`: campos nuevos de `Camera`/`CameraInput`, `timeline`, `summary`, `testCamera`, `status` en `recordings`.
- `EventsHubProvider`: maneja `CameraInfoUpdated` (parchea `['cameras']`) e invalida también `['recordings','timeline']`/`summary` con `RecordingCompleted`.

## Login (Keycloak, `keycloak/theme/`)

- Copiar `designs/CamGuard_Rediseño/thumb.png` a `keycloak/theme/src/login/assets/feed.jpg` (comprimida) y Geist vía `@fontsource-variable/*` importado en `main.css`/`Template.tsx`.
- `Template.tsx`: layout de dos columnas (`flex-wrap`): **panel visual** (`cg-visual`, solo ≥ 900 px; en móvil se oculta) con mosaico 2×2 de la imagen (`blur(22px) grayscale`), líneas de escaneo, viñeta, esquinas de visor, pill "● SEÑAL CIFRADA" pulsante, reloj `dd/mm/aaaa HH:mm:ss`, retícula central "Señal cifrada · identifícate para ver", y **obturador** (dos hojas que cierran al 50 % con "OBTURADOR DE PRIVACIDAD · Nadie mira mientras escribes."). El obturador se activa con un listener `focusin/focusout` sobre `input[type=password]` que pone una clase en `.cg-page`; al enviar el formulario cambia a "VERIFICANDO" (ámbar). **Columna del formulario**: marca arriba, contenido centrado (máx. 380 px), pie "CamGuard · Acceso privado".
- Página de login propia `src/login/pages/Login.tsx` (expulsada con `npx keycloakify eject-page` → `login.ftl`) para igualar el diseño: título "Hola de nuevo." + subtítulo, etiqueta de contraseña con "¿La olvidaste?" a la derecha, botón de texto "Mostrar/Ocultar", botón primario blanco "Iniciar sesión" → "Verificando…", "Seguir conectado en este dispositivo" como switch (es el `rememberMe` del realm, ya habilitado), error en caja roja + animación `shake`. Se registra en `KcPage.tsx` (`switch (kcContext.pageId)`).
- `main.css`: tokens oscuros del diseño (el login es siempre oscuro), estilos `cg-*` actualizados; el resto de páginas (olvidé contraseña, actualizar contraseña, mensajes) heredan el nuevo look sin cambios de estructura.
- `i18n.ts`: `loginAccountTitle: "Hola de nuevo."`, `camguardLoginSubtitle`, `camguardShutter*`, `forgotPassword: "¿La olvidaste?"`, `rememberMe: "Seguir conectado en este dispositivo"`.
- `KcPageStory.tsx` sirve para revisar en Storybook/`npm run dev` las páginas `login.ftl` (con y sin error), `login-reset-password.ftl` y `login-update-password.ftl`.

## Archivos clave

- Backend: `Common/Data/Entities.cs`, `Common/Events/*`, `Common/Realtime/EventsHub.cs`, `Features/Cameras/{CameraInput,ListCameras}.cs`, `Features/Monitoring/CameraMonitorService.cs`, `Features/Recordings/ListRecordings.cs` (+ `RecordingTimeline.cs`, `RecordingSummary.cs`), nuevo `Features/Devices/*`, `Program.cs`, migración `AddDeviceInfo`, `appsettings.json` (`Devices`).
- Frontend: `src/index.css`, `index.html`, `src/main.tsx`, `src/components/*`, `src/features/{live,recordings,settings}/*`, `src/api/*`, `src/hooks/{useEventsHub,useTheme}.tsx`, `src/lib/*`.
- Keycloak: `keycloak/theme/src/login/{Template,KcPage,i18n}.tsx|ts`, `main.css`, nuevo `pages/Login.tsx`, `assets/feed.jpg`.
- Documentación: `CLAUDE.md` (slice Devices, canal `CameraAwakeChannel`, regla "DVRIP solo con la cámara despierta"), `poc/README.md` (claves DVRIP encontradas).

## Orden de trabajo

1. Fase 0: `DvripClient` + probe en Development; confirmar claves con la cámara real.
2. Backend: migración, `Devices`, endpoints de cámaras/grabaciones, hub, pruebas.
3. Frontend base: tokens, fuentes, tema, `Layout` y componentes compartidos.
4. Inicio → Grabaciones/reproductor → Configuración.
5. Tema de Keycloak.
6. Documentación.

## Verificación

- `cd backend && dotnet build && dotnet test` (pruebas nuevas en verde).
- `cd frontend && npm run build && npm run lint`; `cd keycloak/theme && npm run build`.
- `docker compose up -d --build` y en `PUBLIC_URL`:
  - Login: panel con mosaico borroso, el obturador cierra al enfocar la contraseña, error con shake, "Seguir conectado" funciona, recuperar/actualizar contraseña con el nuevo estilo, vista móvil sin panel.
  - Inicio: cuadrícula con varias cámaras (y vista directa con una); al despertar una cámara (movimiento real) pasa a "Grabando", el video en vivo conecta, y en segundos aparecen batería/almacenamiento/modelo (`CameraInfoUpdated`).
  - Grabaciones: navegación por día, filtro Fallidos, chips de cámara, marcas de la línea de tiempo abren el clip, enlace `?clip=` de ntfy abre el reproductor, Anterior/Siguiente, Descargar y Eliminar.
  - Configuración: steppers respetan límites; editar varias cámaras + ajustes y guardar todo con la barra; Descartar restaura; Probar conexión con cámara dormida ("Sin respuesta") y despierta ("Conexión OK"); cambiar sensibilidad queda "pendiente" y se aplica al despertar (verificar con el probe).
  - Tema Oscuro/Claro persiste al recargar; 375 px de ancho sin scroll horizontal.
