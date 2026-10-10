# Plan: grabación y visualización cámara iCSee

Oct 4, 2026 · @Felipe

## Objetivo y alcance

El sistema graba automáticamente cada vez que la cámara iCSee despierta por movimiento, sube los clips a almacenamiento propio y permite verlos, junto con el video en vivo, desde una web en React. Reemplaza la suscripción de la app.

**Incluye:**

- Detección de cuando la cámara despierta (el puerto DVRIP 34567 responde).
- Grabación del stream mientras está despierta, con un tope por activación.
- Subida de clips y miniaturas a un bucket S3-compatible.
- Notificación push por ntfy con enlace al clip.
- Web React: video en vivo, historial de grabaciones por día, reproducción, descarga y borrado.
- Retención automática (borrar clips con más de N días).

**No incluye (por ahora):**

- Monitoreo 24/7: la cámara es a batería y no lo soporta.
- Despertar la cámara a demanda desde la web: queda como fase opcional (SDK de JFTech o ingeniería inversa).

## Arquitectura

&#91;embedded content: arquitectura · 7 componentes en 3 zonas\]

go2rtc mantiene la única conexión con la cámara y la reparte: RTSP para que la API grabe con FFmpeg y WebRTC para el video en vivo. Los clips se reproducen directo desde R2 con URLs prefirmadas que entrega la API.

## Stack tecnológico

Backend en .NET 9 y frontend en React con TypeScript. Las piezas de video (go2rtc y FFmpeg) se usan como procesos externos, sin reinventar códecs.

| Componente | Tecnología | Rol |
| --- | --- | --- |
| Puente de video | go2rtc | Traduce DVRIP (puerto 34567) a RTSP para grabar y a WebRTC para ver en vivo |
| Backend | ASP.NET Core 9 (Web API + BackgroundServices) | Monitoreo, grabación, subida, API REST, SignalR |
| Grabación | FFmpeg | Graba con `-c copy` (sin recodificar) y genera miniaturas |
| Base de datos | SQL Server 2022 Express + EF Core | Cámaras, grabaciones y eventos |
| Almacenamiento | Cloudflare R2 (S3-compatible) vía AWSSDK.S3 | Clips y miniaturas; sin costo por descarga. Alternativa: Backblaze B2 |
| Tiempo real | SignalR | Avisa al frontend: cámara despierta, dormida, nuevo clip |
| Notificaciones | ntfy (instancia propia) | Push al celular con enlace al clip |
| Frontend | React + Vite + TypeScript, TanStack Query, React Router, @microsoft/signalr | Vista en vivo, historial y configuración |
| Acceso remoto | Tailscale o Cloudflare Tunnel | Ver la web fuera de casa sin abrir puertos |

Revisa los límites vigentes del nivel gratuito de R2 o B2 antes de decidir; para clips cortos suele bastar.

## Backend .NET

Un solo host ASP.NET Core corre la API y los servicios en segundo plano. Es más simple de desplegar que separar API y worker, y se puede dividir después si hace falta.

### Estructura de la solución

- `CamGuard.Api`: endpoints, hub de SignalR, autenticación, registro de los hosted services.
- `CamGuard.Core`: entidades, interfaces (`IVideoStorage`, `INotifier`, `IRecorder`) y la lógica de estados de la cámara.
- `CamGuard.Infrastructure`: EF Core + SQL Server, almacenamiento R2, cliente de go2rtc, wrapper de FFmpeg, cliente de ntfy.
- `CamGuard.Tests`: pruebas unitarias de la lógica de estados y de retención.

### Servicios en segundo plano

- **CameraMonitorService**: cada 1 s intenta conectar por TCP al puerto 34567. Publica los cambios dormida → despierta → dormida en un `Channel<CameraEvent>`.
- **RecordingService**: al recibir "despierta", lanza FFmpeg contra `rtsp://go2rtc:8554/icsee` con un tope por activación (60 s, configurable). Al terminar, genera una miniatura y deja el clip en la cola de subida.
- **UploadService**: consume la cola, sube clip y miniatura a R2, actualiza la base de datos, avisa por SignalR y envía la notificación por ntfy. Reintenta con backoff si falla.
- **RetentionService**: una vez al día borra de R2 y de la base de datos los clips más antiguos que el periodo configurado.

### Flujo de una activación

1. La cámara detecta movimiento y despierta.
2. El monitor detecta el puerto abierto (1 a 2 s) y emite `CameraAwake`.
3. Se crea la grabación en estado `Recording` y FFmpeg empieza a grabar.
4. FFmpeg termina por el tope de tiempo o porque la cámara se durmió y cortó el stream.
5. Estado `Uploading`: se suben clip y miniatura.
6. Estado `Uploaded`: SignalR avisa al frontend y ntfy envía la notificación.
7. Pausa de enfriamiento (10 s) antes de aceptar otra activación.

### Endpoints

| Método | Ruta | Uso |
| --- | --- | --- |
| POST | `/api/auth/login` | Login, devuelve JWT |
| GET | `/api/cameras` | Lista de cámaras con su estado actual |
| GET | `/api/recordings?from&to&page` | Historial paginado y filtrado por fecha |
| GET | `/api/recordings/{id}/play` | URL prefirmada de R2 para reproducir (válida unos minutos) |
| DELETE | `/api/recordings/{id}` | Borra el clip de R2 y de la base de datos |
| POST | `/api/live/{cameraId}/webrtc` | Recibe la oferta SDP del navegador y la reenvía a go2rtc; así go2rtc no queda expuesto |
| PUT | `/api/settings` | Tope por activación, enfriamiento, días de retención |
| WS | `/hubs/events` | SignalR: `CameraAwake`, `CameraAsleep`, `RecordingCompleted` |

### Modelo de datos

| Entidad | Campos principales |
| --- | --- |
| Camera | Id, Name, Ip, DvripPort, Go2rtcStream, Enabled |
| Recording | Id, CameraId, StartedAt, EndedAt, DurationSec, SizeBytes, StorageKey, ThumbnailKey, Status (Recording, Uploading, Uploaded, Failed) |
| CameraEvent | Id, CameraId, Type (Awake, Asleep), OccurredAt |
| Settings | MaxClipSeconds, CooldownSeconds, RetentionDays |

## Frontend React

Cuatro pantallas. El video en vivo usa WebRTC a través de go2rtc y los clips se reproducen con un `<video>` normal apuntando a la URL prefirmada de R2.

### Pantallas

| Pantalla | Contenido |
| --- | --- |
| Login | Usuario y contraseña; guarda el JWT |
| Inicio | Reproductor en vivo, indicador de estado (despierta o dormida, actualizado por SignalR) y los últimos 6 clips |
| Grabaciones | Clips agrupados por día con miniatura, hora y duración; filtro por fecha; reproductor en modal; descargar y borrar |
| Configuración | Tope por activación, enfriamiento, días de retención y datos de la cámara |

### Componentes clave

- **LivePlayer**: crea un `RTCPeerConnection`, envía la oferta SDP a `/api/live/{id}/webrtc` y conecta la respuesta al `<video>`. Si la cámara está dormida, muestra "Cámara en reposo: el video aparecerá cuando detecte movimiento" y se reconecta solo al recibir `CameraAwake`.
- **CameraStatusBadge**: escucha el hub de SignalR y muestra el estado con la hora del último cambio.
- **RecordingList** y **RecordingCard**: lista paginada con TanStack Query (`useInfiniteQuery`), invalidada al recibir `RecordingCompleted`.
- **ClipPlayerModal**: pide la URL de reproducción y la carga en `<video controls>`.
- **useEventsHub**: hook que maneja la conexión a SignalR y su reconexión automática.

### Estructura sugerida

- `src/api/`: cliente HTTP con el token y los tipos de la API.
- `src/features/live/`, `src/features/recordings/`, `src/features/settings/`.
- `src/hooks/`: `useEventsHub`, `useAuth`.
- `src/components/`: piezas compartidas (layout, modal, badge).

## Infraestructura y despliegue

Todo corre en un equipo de la casa siempre encendido, con Docker Compose. Solo los clips y las notificaciones salen a la nube.

- **Equipo:** un mini PC x86 es lo más cómodo. SQL Server no corre en ARM, así que si usas una Raspberry Pi hay que cambiar a PostgreSQL (con EF Core es un cambio de proveedor).
- **Red:** go2rtc necesita ver la cámara en la LAN; en Linux usa `network_mode: host`.
- **Acceso remoto:** Tailscale (privado, solo tus dispositivos) o Cloudflare Tunnel (con dominio). En ambos casos no se abre ningún puerto del router.
- **Cámara:** IP fija por reserva DHCP en el router.

```yaml
services:
  go2rtc:
    image: alexxit/go2rtc
    network_mode: host
    volumes:
      - ./go2rtc.yaml:/config/go2rtc.yaml
    restart: unless-stopped

  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_PID: Express
      MSSQL_SA_PASSWORD: ${SQL_PASSWORD}
    volumes:
      - sqldata:/var/opt/mssql
    restart: unless-stopped

  api:
    build: ./backend
    environment:
      ConnectionStrings__Default: Server=sqlserver;Database=CamGuard;User Id=sa;Password=${SQL_PASSWORD};TrustServerCertificate=True
      Camera__Ip: 192.168.1.86
      Go2rtc__BaseUrl: http://host.docker.internal:1984
      Go2rtc__RtspUrl: rtsp://host.docker.internal:8554/icsee
      Storage__Endpoint: ${R2_ENDPOINT}
      Storage__AccessKey: ${R2_ACCESS_KEY}
      Storage__SecretKey: ${R2_SECRET_KEY}
      Storage__Bucket: camguard
      Ntfy__Url: ${NTFY_URL}
    extra_hosts:
      - host.docker.internal:host-gateway
    depends_on:
      - sqlserver
    restart: unless-stopped

  web:
    build: ./frontend
    ports:
      - "8080:80"
    depends_on:
      - api
    restart: unless-stopped

volumes:
  sqldata:
```

El contenedor `web` sirve el build de React con nginx y hace de proxy de `/api` y `/hubs` hacia `api`. La imagen de `api` debe incluir FFmpeg (`apt-get install ffmpeg` en el Dockerfile).

## Fases de desarrollo

La fase 1 es una prueba de concepto que valida cómo se comporta la cámara antes de escribir código de producción. Si falla algún supuesto, se ajusta el diseño ahí y no después.

1. **Prueba de concepto (1 a 2 días)**
   - [ ] go2rtc conectado por DVRIP y video visible en `http://localhost:1984`.
   - [ ] FFmpeg graba un clip manual desde `rtsp://localhost:8554/icsee`.
   - [ ] Confirmar si la cámara se duerme aunque go2rtc o FFmpeg sigan conectados, y cuánto tarda.
   - [ ] Medir cuántos segundos pasan entre el movimiento y el primer cuadro grabado.
2. **Núcleo del backend**
   - [ ] Solución .NET, EF Core, migraciones y entidades.
   - [ ] CameraMonitorService y RecordingService con tope y enfriamiento.
   - [ ] UploadService a R2 con reintentos.
   - [ ] Notificación por ntfy con enlace al clip.
3. **API y tiempo real**
   - [ ] Endpoints de cámaras, grabaciones y configuración.
   - [ ] URLs prefirmadas para reproducir.
   - [ ] Hub de SignalR y autenticación JWT.
4. **Frontend: grabaciones**
   - [ ] Login, layout y cliente de la API.
   - [ ] Pantalla de grabaciones con miniaturas, filtro y reproductor.
   - [ ] Estado de la cámara en vivo con SignalR.
5. **Frontend: video en vivo**
   - [ ] Proxy de señalización WebRTC en la API.
   - [ ] LivePlayer con reconexión al despertar.
6. **Despliegue**
   - [ ] Docker Compose en el equipo de la casa.
   - [ ] Tailscale o Cloudflare Tunnel.
   - [ ] RetentionService y respaldo de la base de datos.
7. **Opcionales**
   - [ ] Descargar clips desde la tarjeta SD por DVRIP, para no perder los primeros segundos.
   - [ ] Despertar la cámara desde la web (SDK de JFTech o la llamada capturada con PCAPdroid).
   - [ ] Cruzar las activaciones con los eventos del sensor PIR Tuya que ya tienes, para etiquetar o confirmar movimiento.

## Riesgos y decisiones pendientes

El mayor riesgo es el comportamiento de la batería: si la cámara no se duerme mientras alguien está conectado, el sistema podría agotarla. La fase 1 existe para medir eso.

| Riesgo | Impacto | Mitigación |
| --- | --- | --- |
| La cámara no se duerme con un cliente conectado | Batería agotada en horas | Tope corto por activación (30 a 60 s) y cortar la conexión al terminar; medirlo en la fase 1 |
| Se pierden los primeros 2 a 5 s del evento | Clips sin el inicio del movimiento | Descargar desde la SD (fase opcional) |
| La cámara rechaza una segunda conexión | Grabación y vista en vivo compiten, o falla la app iCSee | Todo el video pasa por go2rtc, que comparte una sola conexión con la cámara |
| Una actualización de firmware cierra el puerto 34567 | El sistema deja de funcionar | Desactivar actualizaciones automáticas en la app iCSee |
| Falla la subida a R2 | Clips que no llegan a la nube | Cola persistente en disco con reintentos; el clip local se borra solo tras confirmar la subida |
| Credenciales de la cámara en texto plano | Acceso a la cámara si se filtra la config | Variables de entorno o secretos de Docker; cambiar la contraseña actual |

### Decisiones pendientes

- [ ] ¿La cámara tiene tarjeta SD?
- [ ] ¿En qué equipo correrá el sistema (mini PC x86 o Raspberry Pi)? Define SQL Server o PostgreSQL.
- [ ] ¿R2, B2 u otro proveedor para los clips?
- [ ] ¿Cuántos días de retención?
- [ ] ¿Acceso remoto con Tailscale o con Cloudflare Tunnel y dominio propio?
