---
commit: 4400c46
base: d0ea574
fecha: 2026-11-18
sprints: [20]
---

# Revisión de seguridad — Sprint 20 Cámaras ONVIF

Resumen: 0 altas, 0 medias, 2 bajas. El cliente ONVIF no expone secretos ni abre endpoints nuevos. Lo recomendable es no seguir redirecciones HTTP y no pasar la contraseña por la línea de comandos en la PoC.

## Hallazgos

### [Baja] El HttpClient de ONVIF sigue redirecciones
- Dónde: `backend/src/CamGuard.Api/Features/Onvif/OnvifEventService.cs:331` (`AddHttpClient(OnvifClient.HttpClientName, …)`).
- Problema: el handler por defecto tiene `AllowAutoRedirect = true`. `OnvifParser.RebaseHttp` garantiza que la primera petición vaya a la IP configurada, pero un equipo que responda en esa IP con un 307/308 hace que la API repita el POST SOAP (con el `UsernameToken` digest) contra otro host, p. ej. un servicio interno de la red Docker (`keycloak:8080`, `db`). Hace falta un equipo malicioso en la LAN o alguien con sesión que apunte "Probar conexión" a una IP propia. La respuesta no vuelve al usuario salvo el código HTTP o el error de XML, y el digest no sirve fuera de su ventana de tiempo.
- Arreglo: `.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })` en el cliente `onvif`. Una cámara real no redirige el servicio SOAP.

### [Baja] La PoC pasa la contraseña de la cámara en la línea de comandos
- Dónde: `poc/imou/probe-device.ps1:95` (`curl.exe --digest -u "${User}:$Password"`).
- Problema: mientras corre curl, la contraseña queda visible en la lista de procesos (`Get-CimInstance Win32_Process`) para otros usuarios del equipo. Es un script de desarrollo que se usa a mano, por eso es Baja.
- Arreglo: pasar las credenciales por un archivo temporal de config de curl (`-K` con `user = "…"`, borrado en `finally`) o por stdin (`-K -`).

## Dependencias
- `dotnet list package --vulnerable --include-transitive`: sin paquetes vulnerables.
- `npm audit`: no cambió ningún `package-lock.json` y la última auditoría fue el 2026-11-08 (menos de 30 días); no se ejecutó.

## Revisado sin hallazgos
- **Autorización**: no hay endpoints nuevos. `POST /api/cameras/test` agrega ONVIF con la misma validación (IP, puerto 1–65535) y exige sesión, como el caso DVRIP existente.
- **Secretos en logs y respuestas**:
  - Las URIs RTSP se guardan sin credenciales (`OnvifParser.RebaseRtsp`) y la contraseña se agrega solo al armar el origen de go2rtc (`Go2rtcStreamSource.ForRtsp`, con `Uri.EscapeDataString`), que no se registra.
  - Los logs de `OnvifEventService` llevan el nombre de la cámara y el mensaje de `OnvifException`/`HttpRequestException` (host o estado), nunca credenciales.
  - El DTO agrega `OnvifPort` y `Connected`, no la contraseña.
- **XML externo**: `OnvifParser.Parse` usa `DtdProcessing.Prohibit`, `XmlResolver = null` y un tope de 4 M caracteres (sin XXE ni billion laughs). El cuerpo HTTP tiene tope de 2 MB, y el RTSP de 64 KB en cabeceras y cuerpo.
- **SSRF hacia otros equipos**: XAddr, suscripción y URIs RTSP se reapuntan siempre a la IP configurada (`RebaseHttp`/`RebaseRtsp`/`ForRtsp`). La excepción son las redirecciones (hallazgo arriba).
- **Inyección en RTSP**: la línea `DESCRIBE` usa una URI ya parseada por `Uri` (escapa CR/LF). El desafío Basic envía la contraseña en base64, igual que haría go2rtc con esa cámara. En la LAN el RTSP va en claro de todas formas.
- **FFmpeg**: `-tag:v hvc1` se agrega como elementos de `ArgumentList`, solo con H.265 informado por la propia cámara (valor fijo, no texto libre).
- **Migración**: columnas nuevas, sin SQL crudo.
- **Frontend**: sin `dangerouslySetInnerHTML` ni `href` dinámicos. El `onError` del video solo compara la URL.
- **docker-compose / Keycloak**: sin cambios. `.env.example` solo cambia `TZ`.
- **Secretos commiteados**: ninguno. `poc/imou/out/` está en `.gitignore`.
