# Sprint 14 — Uso móvil

## Context
CamGuard se usa sobre todo desde el celular, pero hoy es una web común: hay que abrir el navegador, escribir la URL y navegar hasta la cámara o el clip. El alcance (`Sprint14_UsoMovil/alcance.md`) pide:
1. **PWA instalable** con acceso rápido a vivo y a la última grabación.
2. **Mosaico en vivo bajo demanda**: ver todas las cámaras a la vez sin que nada se conecte solo, para no despertar ni mantener despiertas las cámaras a batería.

Decisiones tomadas:
- Hoy se entra por Tailscale con HTTP. Una PWA exige HTTPS, así que se agrega **Tailscale Serve** (certificado de `*.ts.net`, sin abrir puertos).
- En el mosaico **ninguna cámara se conecta sola**, tampoco una iCSee que ya despertó: cada mosaico se conecta solo cuando lo tocas.

No hace falta tocar el backend: `POST /api/live/{id}/webrtc` (`Features/Live/WebRtcSignaling.cs`), `GET /api/cameras` (`lastThumbnailUrl`, `lastRecordingAt`, `isAwake`) y `GET /api/recordings?page=1&pageSize=1` ya cubren lo necesario.

> Paso 0 (al aprobar): el hook `move-plan.ps1` lleva el plan al primer sprint reservado (**Sprint10_ModosHorarios**), no al 14. Hay que moverlo a mano a `.claude/plans/Sprint14_UsoMovil/plan.md` y confirmar que `Sprint10_ModosHorarios/` vuelve a tener solo `alcance.md`.

## 1. HTTPS con Tailscale Serve (despliegue)
- `nginx.conf` (frontend): con Tailscale Serve delante, `$scheme` es `http` y Keycloak (`KC_PROXY_HEADERS: xforwarded`) creería que la conexión es insegura. Se agrega `map $http_x_forwarded_proto $forwarded_proto { default $http_x_forwarded_proto; '' $scheme; }` y se usa `$forwarded_proto` en los `X-Forwarded-Proto` de `/api/` y `/auth/`. Sin proxy delante funciona igual que ahora.
- `README.md`, sección "Acceso remoto / Tailscale":
  - Activar MagicDNS y HTTPS en la consola de Tailscale.
  - En el host, ejecutar `tailscale serve --bg http://localhost:${WEB_PORT}`.
  - Poner `PUBLIC_URL=https://<host>.<tailnet>.ts.net` sin puerto y ejecutar `docker compose up -d`. Keycloak y `App__PublicBaseUrl` (enlaces de ntfy) toman la URL nueva.
  - **El realm ya importado no se actualiza solo**: en la consola de Keycloak hay que cambiar `rootUrl`, `redirectUris` y `post.logout.redirect.uris` del cliente `camguard-web` a la URL https. También se puede hacer con `kcadm.sh` y conviene dejar el comando en el README.
  - `HOST_IP` (WebRTC de go2rtc) no cambia: sigue siendo la IP de Tailscale.
- `.env.example`: el ejemplo de `PUBLIC_URL` pasa a `https://camguard.tu-tailnet.ts.net` y un comentario explica que la PWA lo requiere.

## 2. PWA instalable
Hecha a mano, sin `vite-plugin-pwa`: son pocos archivos y así se evita una dependencia nueva.

- `frontend/public/manifest.webmanifest`:
  - `name` "CamGuard", `short_name`, `start_url: "/"`, `scope: "/"`, `display: "standalone"`.
  - `background_color` y `theme_color` con el fondo oscuro de los tokens de `index.css`.
  - Íconos PNG de 192 y 512 px, más una versión 512 *maskable*.
  - `shortcuts` (menú al mantener presionado el ícono en Android):
    - "En vivo" → `/live`
    - "Último clip" → `/latest`
- Íconos: `public/icons/icon-192.png`, `icon-512.png`, `icon-maskable-512.png` y `apple-touch-icon.png` (180 px), generados una vez desde `favicon.svg`. La versión maskable lleva el fondo a sangre y el logo dentro de la zona segura. Se versionan en git y en el README queda el comando con el que se generaron (p. ej. `npx @vite-pwa/assets-generator`), sin agregarlo a `package.json`.
- `frontend/public/sw.js`: service worker mínimo.
  - `install`: guarda solo `/offline.html` en caché.
  - `fetch`: interviene únicamente en las navegaciones (`request.mode === 'navigate'`): intenta la red y, si no hay, devuelve `offline.html`. No toca `/api`, `/hubs`, `/auth` ni los assets, así no hay caché de datos ni de tokens ni conflictos con las URLs prefirmadas.
  - `activate`: borra las cachés de versiones anteriores y llama a `clients.claim()`.
- `frontend/public/offline.html`: página autocontenida (estilos en línea, oscuro y claro) con "Sin conexión con CamGuard" y un botón "Reintentar".
- `frontend/index.html`: agrega `<link rel="manifest">`, `<meta name="theme-color">` (uno por esquema con `media`), `apple-touch-icon`, `apple-mobile-web-app-capable` / `-status-bar-style` y `viewport-fit=cover`.
- Registro del SW en `src/bootstrap.tsx`, después de `initOidc()` y solo en producción: `if (import.meta.env.PROD && 'serviceWorker' in navigator) navigator.serviceWorker.register('/sw.js')`. Así no interfiere con el callback `?code=` de `oidcEarlyInit`.
- `frontend/nginx.conf`: `location = /sw.js` y `location = /manifest.webmanifest` con `Cache-Control: no-cache`, para que un SW nuevo se tome en el siguiente deploy. nginx 1.30 ya trae el tipo `application/manifest+json`.
- `src/index.css`: `env(safe-area-inset-*)` en `.topbar` y `.content` para el modo standalone en iPhone (notch y barra inferior).

## 3. Acceso rápido
- **`/latest`** (`src/features/recordings/LatestRedirect.tsx`, nueva ruta en `App.tsx`):
  - Usa `useLatestRecordings(1)` (`useRecordings.ts`) y hace `<Navigate replace to="/recordings?day=<localDayKey(startedAt)>&clip=<id>">`.
  - Sin clips, va a `/recordings`. En carga muestra "Buscando el último clip…".
  - `RecordingsPage` ya abre `?clip=` aunque el clip no esté en la lista cargada (es el camino de los enlaces de ntfy); `day` hace que el día correcto quede seleccionado.
- **Navegación** (`components/Layout.tsx`): se agrega la entrada "Vivo" (`/live`). En móvil (≤ 760 px) la nav pasa a ser una **barra inferior fija** (Inicio · Vivo · Grabaciones · Estado · Config.) con el badge de problemas. Los 96 px de padding inferior que ya tiene `.content` en móvil le dejan lugar, más el área segura.
- **Inicio**: un botón "Último clip" (→ `/latest`) junto a "Ver todos →" en la sección *Últimos clips* de `HomePage.tsx`.

## 4. Mosaico en vivo bajo demanda (`/live`)
- **`LivePlayer.tsx`**: nuevas props `autoConnect?: boolean` (por defecto `true`, como hoy) y `compact?: boolean`.
  - Con `autoConnect = false` la conexión depende solo de `onDemand`, que se generaliza a ambos tipos: `demanded = onDemand && camera.enabled && (tuya || camera.isAwake)`. Una iCSee dormida muestra "En reposo, se activa sola con movimiento" sin botón, como hoy. Si se duerme mientras se mira, `demanded` pasa a `false` y el player se cierra solo (el backend ya responde 503).
  - Con `autoConnect = true` el comportamiento queda igual al actual (vista de una cámara).
  - **Página oculta = desconectar** (en ambos modos): un hook `usePageVisible()` (`src/hooks/usePageVisible.ts`) lee `document.visibilityState` con `useSyncExternalStore`, sin `setState` en un efecto, y entra en `shouldConnect`. Al volver a la app, la vista de una cámara reconecta si sigue despierta; en el mosaico, un mosaico con `onDemand` también reconecta (la intención del usuario sigue vigente). Así una PWA en segundo plano no mantiene despierta una cámara.
  - `compact`: textos de estado más cortos (sin los párrafos largos de `live-idle`), botones más chicos y "Ver en vivo" como acción principal del mosaico. "Pantalla completa" y "Detener" se mantienen.
- **`src/features/live/LiveMosaicPage.tsx`** (ruta `live` en `App.tsx`):
  - Encabezado "En vivo" con un resumen (`n despiertas · n en vivo`) y el botón "Detener todo".
  - Grilla `.mosaic` con un `LiveTile` por cámara habilitada: nombre, `StatusPill` y `BatteryBadge` superpuestos (igual que `CameraCard` en `HomePage.tsx`) y `<LivePlayer autoConnect={false} compact />` debajo.
  - "Detener todo" usa una clave `generation` en el `key` de cada `LivePlayer`: al remontarlos, todos vuelven a reposo.
  - Las cámaras pausadas van al final, atenuadas.
  - Un enlace "Detalle" por mosaico lleva a `/?camera=<id>`.
  - Con una sola cámara se muestra igual, en un único mosaico ancho.
- **CSS** (`index.css`): `.mosaic` con `grid-template-columns: repeat(auto-fill, minmax(320px, 1fr))` en escritorio y 2 columnas en ≤ 760 px (1 columna en ≤ 420 px). Reusa `.frame` y la rotación (`data-rotation`). Tamaños de `.live-idle` y de los botones reducidos bajo `.frame[data-compact]`.
- Para Tuya, el texto del botón deja claro que despierta la cámara ("Despertar y ver"), reutilizando el aviso que ya existe.

## Archivos
- Nuevos:
  - `frontend/public/manifest.webmanifest`, `sw.js`, `offline.html`, `icons/*.png`
  - `src/features/live/LiveMosaicPage.tsx`
  - `src/features/recordings/LatestRedirect.tsx`
  - `src/hooks/usePageVisible.ts`
- Modificados:
  - `frontend/index.html`, `nginx.conf`, `src/bootstrap.tsx`, `src/App.tsx`
  - `src/components/Layout.tsx`, `src/features/live/LivePlayer.tsx`, `src/features/live/HomePage.tsx`, `src/index.css`
  - `README.md`, `.env.example`
- `CLAUDE.md` (Frontend): una línea sobre la PWA (el SW solo da la página offline y nunca guarda `/api` ni `/auth`), `/latest`, `/live` y la regla del vivo (desconecta con la página oculta; el mosaico nunca se conecta solo).

## Verificación
- `cd frontend && npm run build && npm run lint`.
- `docker compose up -d --build web` y en Chrome de escritorio (`http://localhost:8090` cuenta como seguro):
  - En DevTools → Application, el manifest no da errores, el SW queda activo y aparece la opción de instalar.
  - Lighthouse (PWA / installable) aprueba.
  - Con *Offline* activado, una navegación muestra `offline.html`, y las llamadas a `/api` no pasan por el SW (pestaña Network).
- Tailscale Serve:
  - `https://<host>.ts.net` carga, el login de Keycloak vuelve a la app y el token tiene `iss` https.
  - El cierre de sesión vuelve a la app.
  - Un enlace de ntfy abre la URL https.
- Celular:
  - Android: instalar desde Chrome; los atajos "En vivo" y "Último clip" funcionan (mantener presionado el ícono).
  - iPhone: "Agregar a pantalla de inicio" abre en standalone y respeta el notch.
- Mosaico:
  - Al entrar a `/live`, ninguna petición `POST /api/live/*/webrtc` sale sola (Network), ni siquiera con una iCSee despierta.
  - Al tocar un mosaico se conecta solo ese; "Detener todo" corta todos.
  - Al mandar la app a segundo plano, `pc.close()` corta el video (en `chrome://webrtc-internals` no quedan conexiones).
  - Una Tuya muestra "Despertar y ver" y tarda ~30–40 s.
- `/latest` con clips de hoy y con el último clip de ayer abre el reproductor en el día correcto; sin clips, va a Grabaciones.
- `cd backend && dotnet test` no cambia (no hay cambios de backend).

## Fuera de alcance
- Notificaciones push propias (Web Push): ntfy ya las cubre.
- Caché offline de clips o de datos.
- Video por WebRTC fuera de la tailnet (TURN).

## Cambios al implementar
- El plan se movió a mano desde `Sprint9_AvisosEnriquecidos/` (ese era el primer sprint reservado sin `plan.md`, no el 10).
- El nombre, el estado y la batería de cada mosaico van **encima** del cuadro, no superpuestos: así no chocan con el distintivo "EN VIVO".
- En el celular el mosaico mantiene **2 columnas** también por debajo de 420 px; en esos anchos se ocultan los textos del cuadro y quedan solo el título y el botón.
- Los íconos se dibujan desde la geometría de `favicon.svg` con un script de Node sin dependencias, en lugar de `@vite-pwa/assets-generator`.
- nginx no trae `.webmanifest` en `mime.types`: se fija `application/manifest+json` en su `location`.
