# Sprint 20 — Cámaras ONVIF (Imou Cue 2)

## Contexto

El usuario tiene una **Imou Cue 2** (base Dahua, enchufada, siempre encendida) y CamGuard solo admite iCSee
(DVRIP, despierta = puerto abierto) y Tuya (nube). La PoC (`poc/imou/`, resultados en `poc/README.md` §6) confirmó:

- RTSP :554 con Digest. Principal **H.265** 1080p + AAC; substream **H.264** + AAC.
- El CGI de Dahua no responde (no hay `eventManager`).
- **ONVIF sí**: WS-Security `PasswordDigest`, eventos PullPoint, tópico `tns1:VideoSource/MotionAlarm`
  con `State=true` al empezar el movimiento y `State=false` al terminar (35 s en la prueba), y
  `RuleEngine/TamperDetector/Tamper`. `CreatePullPointSubscription` devuelve `http://IP/onvif/Subscription?Idx=N`.

Objetivo: un tipo de cámara **`Onvif`** genérico (no atado a Imou) cuyo `MotionAlarm` hace de "despertó/dormida".
Así toda la cadena actual se reutiliza sin cambios: `ActivationGate` → FFmpeg → IA → R2 → ntfy.
**Decisión del usuario**: se graba el stream principal H.265 (`-c:v copy -tag:v hvc1`). El vivo usa el substream H.264.

## Diseño

### Datos (`Common/Data/Entities.cs` + migración `AddOnvifCameras`)
- `CameraKind.Onvif`. Reutiliza `Ip`, `Username`, `PasswordProtected` (cifrada con `CameraSecretProtector`) y `Model`.
- Campos nuevos en `Camera`:
  - `OnvifPort` (int, 80).
  - `OnvifMainStreamUri`, `OnvifSubStreamUri` (string?, **sin credenciales**), descubiertos por ONVIF.
  - `OnvifMainCodec` (string?, "H264"/"H265").
- `CameraEventType.Tamper` (cámara tapada o manipulada).

### Slice nuevo `Features/Onvif/`
- **`OnvifSoap`** (pura): arma el sobre SOAP 1.2 con WS-Addressing (`Action`, `To`) y el `UsernameToken`
  (`PasswordDigest = Base64(SHA1(nonce + created + password))`). `created` va con el desfase de reloj de la cámara.
  Recibe `nonce`/`now` por parámetro, para probarla.
- **`OnvifParser`** (pura, `XDocument` + `local-name`). Interpreta:
  - `GetSystemDateAndTime`, que da el desfase de reloj;
  - `GetCapabilities`, que da la `XAddr` de Events y Media;
  - `GetDeviceInformation`, que da fabricante y modelo;
  - `GetProfiles` + `GetStreamUri`: el perfil de mayor resolución es el principal y el de menor, el substream, cada uno con su códec;
  - `CreatePullPointSubscription`, que da la dirección de la suscripción. Si viene vacía, relativa o con
    host `0.0.0.0`/`localhost`, se usa la IP de la cámara (el bug que apareció en la PoC);
  - `PullMessages`, que da una lista de `OnvifNotification(Topic, Operation, UtcTime, Items)`.
- **`OnvifEventRules`** (pura, como `TuyaEventRules`): convierte `OnvifNotification` en señal.
  - Movimiento: `MotionAlarm` `State`, `CellMotionDetector` `IsMotion` o `MotionDetector` `State`.
  - Manipulación: `Tamper` `State`/`IsTamper`.
  - Se ignoran `GlobalSceneChange`, `Media/*` y `DigitalInput`.
- **`OnvifMotionTracker`** (pura, como `CameraStateTracker`): `true` → `Awake`; `false` → `Asleep` recién tras
  `Onvif:AsleepDelaySeconds` (10) sin otro `true`. Así un parpadeo no corta el clip. Un `Initialized` con `true` cuenta como
  movimiento; si la suscripción se corta con la cámara despierta, `Asleep`.
- **`OnvifClient`**: `HttpClient` + `OnvifSoap`/`OnvifParser`, sin paquetes WCF. Lanza `OnvifException` con el texto
  del fault, por ejemplo "not authorized".
- **`OnvifEventService`** (`BackgroundService`):
  - Recarga las cámaras `Onvif` habilitadas cada 15 s, y un bucle por cámara.
  - Si cambian IP, puerto, usuario o contraseña, reinicia el bucle de esa cámara.
  - Una cámara borrada o deshabilitada se cierra con `Asleep`, igual que en `CameraMonitorService` y Tuya.
  - Ciclo de cada cámara:
    1. Lee la hora (desfase) y las capacidades.
    2. Descubre los perfiles y las URIs de stream. Si cambiaron, guarda los `Onvif*` en BD, completa `Model`
       si está vacío y llama a `StreamSyncTrigger.Request()`.
    3. `CreatePullPointSubscription` (`Onvif:SubscriptionSeconds` = 120) y luego `PullMessages`
       (`Timeout` = `Onvif:PullTimeoutSeconds` = 10). `Renew` a mitad de vigencia.
  - Señales:
    - Movimiento: `OnvifMotionTracker` → `CameraStatePublisher.PublishAsync`.
    - Manipulación: `CameraEvent` `Tamper` y aviso inmediato por ntfy ("Cámara manipulada: …").
  - Errores: reconecta con `RetryPolicy.Delay` (tope 1 min) y nunca registra la contraseña.
- **`OnvifConnectionState`**: `Connected`/`Since`/`LastError` por cámara, mismo patrón que `TuyaConnectionState`.
  Lo leen Health y Cameras.
- `AddOnvifFeature(...)` en `Program.cs`, con `OnvifOptions` en `appsettings.json`.

### Streams (`Features/Streams/`)
- `Go2rtcStreamSource.ForRtsp(uri, ip, user, password)` (pura) inserta las credenciales escapadas, fuerza el host
  a `camera.Ip` y conserva puerto, ruta y query.
- `StreamSyncService` registra dos streams para `Onvif`:
  - `{Go2rtcStream}` = principal, que es el que graba FFmpeg;
  - `{Go2rtcStream}_sub` = substream, para el vivo.
  - Mientras no haya URIs descubiertas, la cámara se omite con un warning, una sola vez.
  - Helper `OnvifStreams.LiveName(camera)`.
- `CameraInput.ValidateUniqueAsync`: el stream nuevo no puede chocar con `{otro}_sub`.

### Captura (`Features/Capture/`)
- `FfmpegRecorder.RecordArguments(..., hevc: bool)` agrega `-tag:v hvc1` (sin eso Safari/iOS no reproducen H.265 en MP4).
- `RecordingService` pasa `camera.OnvifMainCodec == "H265"`.
- Lo demás no cambia: la cámara está encendida, así que basta el `StartupGraceSeconds` de las iCSee. El audio AAC
  se vuelve a codificar a AAC, igual que hoy, con costo mínimo.

### Vivo
- `WebRtcSignaling`: una `Onvif` se puede ver sin estar "despierta" y usa `OnvifStreams.LiveName`.
- `LivePlayer.tsx`: `alwaysOn = kind === 'Onvif'`.
  - En la vista de una cámara se conecta al abrir.
  - En el mosaico sigue a demanda.
  - Con la página oculta se desconecta, como hoy.

### Cámaras, prueba de conexión y formulario
- `CameraInput` / `ApplyTo`: rama `Onvif` con IP, `OnvifPort` (1–65535), usuario y contraseña (obligatoria en el alta).
  Limpia los campos Tuya. Si cambia la IP o el puerto, borra las URIs descubiertas.
  `ListCameras` expone `OnvifPort` y `Connected` (de `OnvifConnectionState`).
- `TestConnection` (`Kind = Onvif`, `OnvifPort`): hora, `GetDeviceInformation` y perfiles.
  - Si funciona, responde "Conexión OK · H.265 1920×1080 + H.264 640×480" y el modelo.
  - Si no, el fault, o "Sin respuesta".
- Frontend:
  - `types.ts`: `CameraKind` agrega `'Onvif'`.
  - `cameraDraft.ts`: `onvifPort` 80 y usuario `admin` por defecto.
  - `CameraPanel.tsx`:
    - tipo "ONVIF (Imou, Dahua…)";
    - campos IP, Puerto ONVIF, Usuario y Contraseña, con la ayuda "En Imou: el código de seguridad de la etiqueta";
    - sin "Sensibilidad", que se ajusta en la app del fabricante;
    - texto de "Grabar al detectar movimiento" adaptado.
  - `cameraStatus`: "Movimiento" / "En espera" / "Sin conexión".

### Salud (`Features/Health/`)
- `CameraHealth` agrega `OfflineSince` (solo `Onvif`).
- `HealthEvaluator`: para `Onvif` no corre `Silent`, porque un cuarto puede pasar 48 h sin movimiento. En su lugar abre
  `offline:{id}` ("{Nombre} sin conexión") si lleva más de `ServiceGraceMinutes` desconectada. Sin batería no hay aviso de batería.
- `StatusPage.tsx` muestra "Conectada desde…" / "Sin conexión desde…" para estas cámaras.

### Reproductor de clips
- Si `<video>` da error de decodificación, muestra "Este navegador no reproduce H.265 (p. ej. Firefox); ábrelo en
  Chrome, Edge o Safari", en lugar de quedar en negro.

### Documentación
- `CLAUDE.md`:
  - el slice Onvif en "Backend";
  - entre slices: Onvif → Monitoring (`CameraStatePublisher`), Onvif → Streams (`StreamSyncTrigger`) y Health lee
    `OnvifConnectionState`;
  - el flujo de activación ONVIF;
  - en "Detalles que ya causaron bugs": la dirección de suscripción vacía o `0.0.0.0` y `-tag:v hvc1`.
- `README`: cómo agregar una Imou y reservar su IP en el router (DHCP estático).

## Pruebas (TUnit, carpetas espejo)

| Archivo | Qué prueba |
|---|---|
| `Features/Onvif/OnvifSoapTests` | Digest con `nonce`/`created` fijos; el `created` aplica el desfase. |
| `Features/Onvif/OnvifParserTests` | Capacidades y tópicos tomados de `poc/imou/out`; `PullMessages` con `MotionAlarm` true/false e `Initialized`; perfiles (principal/sub por resolución, códecs); dirección de la suscripción (normal, vacía, relativa, `0.0.0.0`); fault "not authorized". |
| `Features/Onvif/OnvifEventRulesTests` | Movimiento, manipulación y tópicos ignorados. |
| `Features/Onvif/OnvifMotionTrackerTests` | true → `Awake`; false → `Asleep` solo tras el retardo; parpadeo dentro del retardo; `Initialized` false sin evento; desconexión estando despierta. |
| `Features/Streams/Go2rtcStreamSourceTests` | `ForRtsp`: credenciales escapadas, host forzado, query conservada. |
| `Features/Streams/StreamSyncPlanTests` | Los dos streams de una `Onvif`. |
| `Features/Cameras/CameraInputTests` | Validación `Onvif` y choque con `_sub`. |
| `Features/Capture/FfmpegRecorderTests` | `-tag:v hvc1` solo con `hevc`. |
| `Features/Health/HealthEvaluatorTests` | `offline:{id}` con gracia; sin `silent` para `Onvif`. |

## Verificación

1. `cd backend && dotnet build && dotnet test`; `cd frontend && npm run build && npm run lint`.
2. `docker compose up -d --build`. En Configuración, agregar "Imou" como ONVIF (192.168.1.102, puerto 80, admin, código).
   "Probar conexión" debe dar OK con el modelo y los códecs.
3. En los logs, la suscripción ONVIF queda activa y go2rtc tiene `imou` e `imou_sub` (`GET /api/streams`, sin
   ver la contraseña en los logs de CamGuard).
4. Moverse frente a la cámara: `Awake`, se crea el clip H.265 y, al terminar el movimiento + 10 s, `Asleep`.
   Luego IA, R2 y aviso ntfy. El clip se reproduce en Chrome/Android.
5. El vivo en la vista de la cámara conecta sin movimiento (substream).
6. Tapar el lente: ntfy "Cámara manipulada". Desenchufar la cámara: tras la gracia aparece `offline` en Estado y por ntfy, y se resuelve al volver.
7. En la Raspberry: medir el tiempo de la IA con un clip H.265 de 60 s. Si es excesivo, evaluar muestrear solo
   cuadros clave (`-skip_frame nokey`) en `FrameSampler` (queda fuera de este sprint salvo que sea necesario).

## Fuera de alcance
- Pre-grabación (los segundos antes del aviso): go2rtc abre la cámara al empezar a grabar (~1–2 s).
- PTZ, audio bidireccional, descubrimiento automático en la LAN (WS-Discovery).

## Cambios al implementar
- El plan se movió a mano desde `Sprint9_AvisosEnriquecidos/` (el hook lo lleva al primer sprint reservado), y `Sprint9_AvisosEnriquecidos/` quedó solo con su `alcance.md`.
- **Códec del stream principal**: ONVIF Media 1 no tiene valor para H.265 y varias Dahua lo informan como H264. Por eso `OnvifMainCodec` sale del SDP del propio stream (`RtspDescribe`, un DESCRIBE con Digest al descubrir) y el perfil ONVIF queda solo como respaldo.
- Todas las direcciones que informa la cámara (XAddr de Media y Events, suscripción y URIs RTSP) se reapuntan a la IP y el puerto configurados, no solo la de la suscripción. Así se evita contactar otro equipo, por ejemplo la `192.168.1.108` de fábrica.
- La manipulación se avisa una vez cada 30 min por cámara, para no repetirla en cada reconexión.
- Tras un "usuario o contraseña incorrectos" el reintento espera más (backoff desde el 5.º intento), porque las Dahua bloquean la cuenta.
- `CameraHealth` lleva `Connected`, `ConnectionSince` y `ConnectionError` (en lugar de `OfflineSince`) para mostrarlos en Estado.
- `ListCameras` expone `OnvifPort` y `Connected`; el estado se lee en `cameraStatus` ("Movimiento" / "En espera" / "Sin conexión").
