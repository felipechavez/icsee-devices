# PoC: cerradura XOOMI C20 (Tuya)

La C20 no habla DVRIP ni abre un puerto en la LAN al despertar: todo pasa por la nube
de Tuya. Antes de tocar CamGuard hay que responder:

1. ¿Se puede sacar el **video** de la cerradura fuera de la app (go2rtc, fuente `tuya://`)?
2. ¿Qué **señal** llega al tocar el timbre / detectar movimiento, y con cuánto retraso?
3. ¿Qué **datos** expone (batería, aperturas, alarmas) para mostrarlos en la web?

## 0. Preparación

1. **App Tuya Smart** (no Smart Life): go2rtc no admite cuentas Smart Life. Si la cerradura
   está en Smart Life, quítala y agrégala de nuevo en Tuya Smart.
2. **Proyecto en Tuya Developer Platform** (https://platform.tuya.com):
   - *Cloud → Development → Create Cloud Project*: tipo *Smart Home*, data center
     **Western America** (Colombia; si tu cuenta es de otra región, usa la de la app).
   - Servicios (*Service API*): *IoT Core*, *Authorization Token Management*,
     *Smart Lock Open Service* e ***IoT Video Live Stream*** (Free Trial; sin él no hay video).
   - *Devices → Link App Account → Add App Account*: escanea el QR desde Tuya Smart
     (*Yo → icono de escanear*). La cerradura debe aparecer en la lista de equipos.
   - Copia el **Access ID**, el **Access Secret** (pestaña *Overview*) y el **Device ID** de la cerradura.
3. Copia `tuya.poc.example.json` a `tuya.poc.json` (ignorado por git) y completa los
   valores. `endpoint` según el data center:

   | Data center | endpoint |
   | --- | --- |
   | Western America | `https://openapi.tuyaus.com` |
   | Eastern America | `https://openapi-ueaz.tuyaus.com` |
   | Central Europe | `https://openapi.tuyaeu.com` |
   | China | `https://openapi.tuyacn.com` |

## 1. ¿Qué expone la cerradura?

```powershell
./poc/tuya/probe-device.ps1
```

Muestra categoría, `uid`, DPs declarados (estados y funciones), estado actual, aperturas y
alarmas recientes, y si admite video (`webrtc-configs` y URL de stream). Las respuestas crudas
quedan en `poc/tuya/out/` (ignorado). Córrelo dos veces: con la cerradura dormida y justo
después de tocar el timbre.

- [ ] Categoría y producto: ____
- [ ] DP de batería (p. ej. `residual_electricity`, `battery_state`): ____
- [ ] DP de timbre / movimiento / alarma (p. ej. `doorbell`, `alarm_lock`, `hijack`): ____
- [ ] DPs de apertura (p. ej. `unlock_fingerprint`, `unlock_password`, `unlock_app`): ____
- [ ] `webrtc-configs` responde y `supports_webrtc` = ____
- [ ] Stream `rtsp`/`hls` por la API de IPC: ____

## 2. Video con go2rtc

Con go2rtc de la fase 1 (`go2rtc -config poc/go2rtc.poc.yaml`, http://localhost:1984; con Docker
la consola queda en http://localhost:11984):

**A. Cuenta Tuya Smart (más rápido):** en la web de go2rtc, *Add → Tuya*, elige la región, entra con
el correo y la contraseña de Tuya Smart y copia la URL que propone para la cerradura.

**B. Cloud API (la que usaría CamGuard):** con el `uid` del paso 1:

```yaml
streams:
  tuya: tuya://openapi.tuyaus.com?device_id=DEVICE_ID&uid=UID&client_id=ACCESS_ID&client_secret=ACCESS_SECRET
  tuya_sd: tuya://openapi.tuyaus.com?device_id=DEVICE_ID&uid=UID&client_id=ACCESS_ID&client_secret=ACCESS_SECRET&resolution=sd
```

Agrégalo a `poc/go2rtc.poc.yaml` (ignorado por git) y reinicia go2rtc.

Prueba con la cerradura **dormida** y luego **despierta** (timbre):

- [ ] ¿Se ve el stream `tuya`? ¿Solo despierta o también la despierta go2rtc? ____
- [ ] Tiempo desde abrir el stream hasta el primer cuadro: ____ s
- [ ] ¿Cuánto dura despierta con el stream abierto? ¿Lo corta sola? ____
- [ ] ¿Hay audio? ____
- [ ] Clip con el mismo FFmpeg de CamGuard (despiértala justo antes):

  ```powershell
  ./poc/record-clip.ps1 -Seconds 20 -Url rtsp://localhost:8554/tuya
  ```

- [ ] ¿Se puede ver a la vez en la app Tuya y en go2rtc? ____

### 2b. Stream RTSPS/HLS de la API de IPC

Si `webrtc-configs` responde `device not supports` (la C20 no admite WebRTC), go2rtc no sirve con
`tuya://`, pero la API entrega una URL RTSPS/HLS temporal. Despierta la cerradura y graba:

```powershell
./poc/tuya/record-stream.ps1 -Seconds 20            # RTSPS
./poc/tuya/record-stream.ps1 -Seconds 20 -Type hls  # si RTSPS falla
```

- [ ] ¿El clip tiene imagen (y audio)? ____
- [ ] ¿Funciona con la cerradura dormida (la despierta la API) o solo despierta? ____
- [ ] Segundos entre pedir la URL y el primer cuadro (`Inicio` del script vs. cronómetro en el clip): ____

## 3. Señal de activación y retraso

```powershell
./poc/tuya/watch-device.ps1
```

Muestra cada cambio de *en línea*, de DPs y cada reporte nuevo del registro del equipo con
su retraso. Haz cada prueba anotando la hora exacta (cronómetro del celular):

| Prueba | Hora real | Señal que llegó (código = valor) | Retraso |
| --- | --- | --- | --- |
| Tocar el timbre | | | |
| Pasar frente a la cámara (sin tocar) | | | |
| Abrir con huella | | | |
| Abrir con clave | | | |
| Clave errónea varias veces | | | |

- [ ] ¿Cambia `online` al despertar, o la cerradura figura siempre en línea? ____
- El registro (`/logs`) se consulta cada pocos segundos, así que el retraso medido incluye
  el intervalo. CamGuard usa la suscripción de mensajes de Tuya (Pulsar), que empuja el evento
  sin sondear: ver el paso 4.

## 4. Message Service (lo que usa CamGuard)

Activa *Message Service* en el proyecto (tipo *Device status report*) y escucha:

```powershell
dotnet run poc/tuya/watch-messages.cs
```

Muestra cada DP que reporta la cerradura y su retraso. Un **401** al conectar significa que el
servicio no está activado. Repite las pruebas del paso 3 y anota qué llega y cuánto tarda.

## Resultados

| Pregunta | Resultado |
| --- | --- |
| ¿Video fuera de la app? | **Sí.** No hay WebRTC (`device not supports`), pero sí RTSPS/HLS por la API de IPC. go2rtc abre el RTSPS si se registra por `PATCH` (`expr:` está prohibido por API). H.264 864×480 + audio |
| Segundos hasta el primer cuadro | ~30–40 s con la cerradura dormida (pedir la URL la despierta) |
| Orientación | Cámara de lado: girar **90°** a la derecha (vertical) |
| Señal de activación usable | DPs `doorbell`, `alarm_lock`, `hijack`; pendiente de medir por Message Service |
| Retraso de la señal | Pendiente (Message Service sin activar: 401) |
| Batería y aperturas disponibles | `residual_electricity` (48 %); `unlock_*` con nº de usuario. Los registros de `door-lock` requieren autorizar *Smart Lock Open Service* en el proyecto |

**Decisión:** video + señal → sprint de integración (`CameraKind` Tuya). Solo señal → eventos y
notificaciones sin grabación. Nada → no se integra.
