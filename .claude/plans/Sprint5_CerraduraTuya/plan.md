# Sprint 5 — Cerradura Tuya

> Al aprobarse se mueve a `.claude/plans/Sprint5_CerraduraTuya/plan.md`.

## Contexto

El usuario tiene una cerradura **XOOMI C20** (Tuya, categoría `videolock`, device `eb8d8e837c0a688a86f1kb`, data center Western America) con cámara a batería, y quiere integrarla en CamGuard junto a las iCSee. La PoC (`poc/tuya/`) comprobó:

- **Video:** la cerradura no admite WebRTC (`webrtc-configs` → `device not supports`), pero `POST /v1.0/devices/{id}/stream/actions/allocate` entrega una URL **RTSPS temporal** (también HLS). FFmpeg grabó H.264 864×480 + audio; **la API despierta la cerradura** (sin tocar el timbre). Primer cuadro con la cerradura dormida: ~40 s. La cámara va de lado: **girar 90° a la derecha** (queda vertical).
- **DPs:** `doorbell` (bool), `alarm_lock` (enum: wrong_finger, wrong_password, tongue_bad, low_battery…), `hijack` (bool), `unlock_fingerprint|password|card|face|key|phone_remote|temporary|dynamic` (nº de usuario), `residual_electricity` (batería %).
- Nada es LAN: no hay puerto que sondear ni DVRIP. Todo pasa por la Tuya Cloud API.

Decisiones del usuario:
- **Graban:** timbre + alarmas (`doorbell`, `alarm_lock`, `hijack`). Las aperturas normales solo se registran y avisan.
- **Aviso ntfy inmediato** al llegar cada evento (timbre, alarma, apertura), además del aviso normal del clip.
- **"Ver en vivo" a demanda** para la cerradura (la despierta, gasta batería y tarda).

## Enfoque

Mismo pipeline que las iCSee, cambiando solo la fuente de "despertó" y el origen del stream:

```
Tuya Message Service (Pulsar por WebSocket) ─► TuyaEventService ─► CameraStatePublisher (Awake/Asleep)
                                                   │                    └► CameraEventChannel → RecordingService (sin cambios)
                                                   ├► ntfy inmediato + CameraEvent (timbre/alarma/apertura)
                                                   └► batería → Camera.BatteryPercent + CameraInfoUpdated
RecordingService / Live ─► TuyaStreamPreparer: URL RTSPS nueva de Tuya ─► PATCH go2rtc stream "chapa" ─► FFmpeg / WebRTC
```

- **Eventos por Message Service, no por sondeo:** sondear `/status` cada pocos segundos supera de lejos la cuota gratuita de la API (~500 mil llamadas/mes por equipo). El Message Service empuja los reportes de DPs sin costo por llamada. Se consume por la **API WebSocket de Pulsar** (como `tuya-connector-python`), sin librería de Pulsar:
  `wss://mqe.tuyaus.com:8285/ws/v2/consumer/persistent/{accessId}/out/event/{accessId}-sub?ackTimeoutMillis=3000&subscriptionType=Failover`, con las cabeceras `username = accessId` y `password = md5(accessId + md5(accessSecret))[8..24]`. Cada mensaje trae `payload` en base64 → JSON `{ data, protocol, … }`. `data` va cifrado con AES y la clave `accessSecret[8..24]`: ECB/PKCS7, o GCM si `properties.em == "aes_gcm"` (IV de 12 bytes al inicio y tag de 16 al final). Se responde `{"messageId": …}` como ack. El protocolo 4 es un reporte de estado: `{ devId, status: [{ code, value, t }] }`.
- **Sin señal de "dormida":** la activación dura `Tuya:ActivationSeconds` (90 s por defecto) desde el último evento disparador, y otro evento la extiende. `MaxClipSeconds` sigue siendo el tope.
- **Stream preparado antes de usarlo:** justo antes de grabar o de abrir el vivo, la API pide una URL RTSPS nueva a Tuya y la registra en go2rtc con `PATCH`, que no corta una conexión abierta. Grabación (`rtsp://go2rtc:8554/<stream>`) y vivo (WebRTC de go2rtc) quedan iguales que en las iCSee.
  > **Cambio durante la implementación:** el plan original usaba una fuente `expr:` de go2rtc que llamaba a un endpoint interno de la API. go2rtc rechaza por API los orígenes `expr:`/`exec:`/`echo:` ("source from insecure producer") y no tiene opción para permitirlos. Por eso se quitaron el endpoint interno, la clave y el puerto publicado de la API.

## Fase 0 — Validar el Message Service (PoC, antes del backend)

1. En Tuya Developer Platform → proyecto CamGuard → **Message Service** → *Enable*, con el tipo *Device status report* (entorno **prod**: tópico `event`).
2. Nuevo `poc/tuya/watch-messages.ps1`: `ClientWebSocket` con las cabeceras de arriba; decodifica y descifra (ECB/GCM) y muestra `devId code=value` con su retraso. Se reutiliza `tuya-api.ps1` para la config.
3. Tocar el timbre, abrir con huella y poner una clave errónea; anotar en `poc/tuya/README.md` qué llega y cuánto tarda. Si los mensajes no llegan, se detiene el sprint y se reevalúa (sondeo con cuota limitada).

## Backend

### Modelo (`Common/Data/Entities.cs`, `AppDbContext.cs`) — migración `AddTuyaCameras`
- `enum CameraKind { Icsee, Tuya }` (conversión a string). En `Camera`: `Kind` (por defecto `Icsee`), `TuyaDeviceId` (string?, máx. 64, índice único filtrado) y `Rotation` (int: 0/90/180/270, por defecto 0). Para las Tuya, `Ip` queda `""`.
- `CameraEventType` suma `Doorbell`, `Alarm`, `Unlock`; `CameraEvent.Detail` (string?, máx. 100), p. ej. `wrong_password` o `fingerprint #1`.

### Cameras (`Features/Cameras/`)
- `CameraInput`: suma `Kind`, `TuyaDeviceId` y `Rotation`. `Validate` según el tipo:
  - iCSee: reglas actuales.
  - Tuya: `TuyaDeviceId` obligatorio (`[A-Za-z0-9]{10,64}`); se ignoran IP, puerto, usuario, contraseña y sensibilidad.
  - `Rotation ∈ {0, 90, 180, 270}`.
  - `Kind` no cambia al editar.
- `ValidateUniqueAsync`: IP+puerto solo entre iCSee; `TuyaDeviceId` único.
- `ApplyTo` y `ListCameras.CameraResponse`/`ToResponse` suman `Kind`, `TuyaDeviceId` y `Rotation`.

### Monitoring (`Features/Monitoring/`)
- Se extrae `PublishAsync` de `CameraMonitorService` a un singleton **`CameraStatePublisher`**. Hace lo mismo que hoy: `CameraStateStore`, `CameraEventChannel`, `CameraAwakeChannel`, hub y `CameraEvent` en BD. Lo usan Monitoring y Tuya.
- `CameraMonitorService` solo sondea las cámaras `Kind == Icsee`.

### Nuevo slice `Features/Tuya/` (`AddTuyaFeature`)
- `TuyaOptions` (`Tuya:`):
  - `Endpoint` (`https://openapi.tuyaus.com`), `AccessId`, `AccessSecret`, `MessageEndpoint` (`wss://mqe.tuyaus.com:8285/`) y `MessageTopic` (`event`).
  - `ActivationSeconds` (90), `StartupGraceSeconds` (90), `StatusRefreshMinutes` (360) y `StreamUrlBase` (la URL con que go2rtc llega a la API).
  - Sin `AccessId`, el slice queda inactivo y lo avisa en el log.
- `TuyaSigner` (estático, puro): `StringToSign`, `Sign` (HMAC-SHA256 en mayúsculas) y `CanonicalUrl` (query ordenada). Es lo mismo que `poc/tuya/tuya-api.ps1`.
- `TuyaApiClient` (`HttpClient` tipado): token con caché y renovación, `GetDeviceAsync`, `GetStatusAsync` y `AllocateStreamUrlAsync(deviceId, "rtsp")`. No registra URLs ni tokens en el log.
- `TuyaMessageDecoder` (puro): mensaje WebSocket → `TuyaStatusReport(DevId, [(Code, Value, At)])`. Implementa la contraseña MQ, base64, ECB/GCM y el protocolo 4. Lo demás (online/offline, etc.) devuelve `null`.
- `TuyaEventRules` (puro): `(code, value)` → `LockSignal` (`Doorbell`, `Alarm(detail)`, `Hijack`, `Unlock(method, user)`, `Battery(%)` o nada), con `TriggersRecording` y el texto del aviso:
  - "Tocaron el timbre"
  - "Alarma: clave errónea"
  - "Abrieron con huella (usuario 1)"
- `TuyaActivationWindow` (puro, por cámara): `Trigger(now) → bool startedNow` y `Expired(now) → bool`.
- `TuyaEventService` (`BackgroundService`):
  - Conecta el WebSocket con reconexión y backoff (`RetryPolicy` de Upload) y mapea `devId → cámara` (caché refrescada como en Monitoring).
  - Por cada señal:
    1. guarda `CameraEvent` con `Detail`;
    2. envía ntfy inmediato (`INotifier`);
    3. si dispara, abre o extiende la ventana y, si recién empieza, publica `Awake`;
    4. la batería va a la BD y a `CameraInfoUpdated`.
  - Un tick de 1 s publica `Asleep` al vencer la ventana.
  - Al arrancar, y cada `StatusRefreshMinutes`, lee `/status` de cada cerradura para la batería (muy pocas llamadas).
- `TuyaStreamKey` (singleton): clave aleatoria por proceso (`RandomNumberGenerator`). Al reiniciar la API cambia el origen y `StreamSyncService` lo vuelve a registrar.
- Endpoint interno **fuera de `/api`** (nginx solo publica `/api` y `/hubs`):
  - `GET /internal/tuya/cameras/{id}/stream-url?key=` con `.AllowAnonymous()`;
  - compara la clave en tiempo constante y devuelve la URL RTSPS en `text/plain`;
  - se mapea desde `Program.cs` con `app.MapTuyaInternal()`.
- `TestConnection` para Tuya: `GET /v1.0/devices/{id}` → "Conexión OK · Smart Lock (en línea)", con `model = product_name`.

### Streams (`Features/Streams/`)
- `Go2rtcStreamSource.ForTuya(streamUrlBase, cameraId, key)` → `expr:fetch('{base}/internal/tuya/cameras/{id}/stream-url?key={key}').text`.
- `StreamSyncService` elige el origen según `Kind`. La cerradura no necesita contraseña: el aviso "sin contraseña" pasa a ser solo para iCSee.

### Capture (`Features/Capture/`)
- `FfmpegRecorder.RecordArguments(..., rotation)`: si `rotation != 0`, agrega `-display_rotation -{rotation}` antes de `-i` (metadato del MP4, sin recodificar). Así lo probó la PoC. FFmpeg aplica la rotación al generar la miniatura.
- `IRecorder.RecordAsync(..., TimeSpan startupGrace)`: el tope duro pasa a ser `maxDuration + startupGrace`, en lugar de los 30 s fijos. Así no se mata FFmpeg mientras Tuya tarda en dar el primer cuadro, lo que dejaría un MP4 sin `moov`. `RecordingService` pasa 30 s a las iCSee y `Tuya:StartupGraceSeconds` a las Tuya, más la rotación de la cámara.

### Devices
- `DeviceInfoService` ignora las cámaras `Kind != Icsee`: nunca abre DVRIP contra una cerradura.

### Despliegue
- `docker-compose.yml` (api):
  - `ports: "127.0.0.1:${API_INTERNAL_PORT:-5080}:8080"`, para que go2rtc, que usa `network_mode: host`, llegue a la API;
  - `Tuya__StreamUrlBase: http://127.0.0.1:5080` y las variables `Tuya__*` desde `.env`.
- `docker-compose.windows.yml`: `Tuya__StreamUrlBase: http://api:8080`. El puerto 5080 queda fuera del rango reservado 1976–2075.
- `.env.example`: `TUYA_ENDPOINT`, `TUYA_ACCESS_ID`, `TUYA_ACCESS_SECRET` y `TUYA_MESSAGE_ENDPOINT`, con comentarios (data center y Message Service activado).

## Frontend

- `api/types.ts`: `CameraKind = 'Icsee' | 'Tuya'` y los campos `kind`, `tuyaDeviceId` y `rotation` en `Camera`, `CameraInput` y `TestConnectionRequest`.
- `settings/cameraDraft.ts` y `CameraPanel.tsx`:
  - `Segmented` "Tipo" (iCSee / Tuya), solo en altas.
  - Con Tuya se muestra "Device ID" (hint: *Tuya Developer Platform → Devices*) y se ocultan IP, puerto, usuario, contraseña y sensibilidad.
  - El switch dice "Grabar con timbre y alarmas".
  - `Segmented` "Rotación" (0°/90°/180°/270°) para todas.
  - La cabecera muestra `Tuya · <deviceId>` en lugar de la IP.
- `live/LivePlayer.tsx`:
  - Cerradura Tuya en reposo: botón **"Ver en vivo"**, con el texto "Despierta la cerradura: tarda hasta ~1 min y gasta batería".
  - Con la cerradura activa conecta sola, como hoy.
  - Tiempo hasta el primer cuadro: 90 s para las Tuya.
  - Rotación por CSS: `.frame` con `container-type: size`; con `data-rotation=90|270`, el `<video>` pasa a `width: 100cqh; height: 100cqw` con `translate(-50%,-50%) rotate(…)`.
- El reproductor de clips no cambia: el navegador respeta la rotación del MP4 (se verifica).

## Pruebas (TUnit, carpetas espejo)

- `Features/Tuya/TuyaSignerTests`: `CanonicalUrl` ordena la query; la firma es determinista y coincide con un vector calculado con `tuya-api.ps1`; el token entra o no en el mensaje según corresponda.
- `TuyaMessageDecoderTests`: ida y vuelta ECB y GCM (cifrado en la prueba), contraseña MQ, protocolo distinto de 4 → `null`, base64 inválido → `null`.
- `TuyaEventRulesTests`: doorbell true/false, cada `alarm_lock`, `hijack`, `unlock_*` y batería.
- `TuyaActivationWindowTests`: inicia, extiende y vence.
- `CameraInputTests`: reglas Tuya, rotación inválida, `Kind` inmutable.
- `Go2rtcStreamSourceTests.ForTuya`.
- `FfmpegRecorderTests`: `-display_rotation` antes de `-i`, y sin la opción si la rotación es 0.

## Documentación
- `CLAUDE.md`: el slice Tuya, el flujo de activación de las Tuya, el endpoint interno (que no debe registrar la URL en logs) y `CameraStatePublisher`.
- `poc/tuya/README.md`: resultados de la Fase 0 y de la verificación.

## Verificación

1. `cd backend && dotnet test` y `cd frontend && npm run build && npm run lint`.
2. `.env` con `TUYA_*`. Luego `docker compose -f docker-compose.yml -f docker-compose.windows.yml up -d --build`, siempre con el override en Windows.
3. En Configuración, alta de "Chapa de casa": tipo Tuya, Device ID y rotación 90°. "Probar conexión" debe responder OK.
4. Consola de go2rtc (http://localhost:11984): el stream existe con origen `expr:`. Al abrirlo debe verse el video. Si go2rtc no abre el RTSPS de Tuya, el endpoint devuelve `ffmpeg:<url>#video=copy#audio=aac` (opción `Tuya:StreamViaFfmpeg`).
5. Inicio: "Ver en vivo" muestra la cerradura **vertical**.
6. Tocar el timbre:
   - llega el ntfy inmediato "Tocaron el timbre";
   - la tarjeta pasa a Despierta → Grabando;
   - el clip queda vertical, se reproduce en el navegador, su miniatura es correcta y llega el ntfy del clip.
7. Clave errónea → aviso y clip. Apertura con huella → solo aviso, sin clip.
8. La batería aparece en la tarjeta y se actualiza con los reportes.
9. Las iCSee siguen igual: despertar → clip, y el DVRIP funciona.
