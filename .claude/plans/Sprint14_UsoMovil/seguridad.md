---
commit: d0ea574
base: 06e6c86
fecha: 2026-11-16
sprints: [14]
---

# Revisión de seguridad — Sprint 14 Uso móvil

Resumen: 0 altas, 0 medias, 0 bajas. La PWA, el mosaico y `/latest` no guardan datos ni tokens, no agregan
endpoints y no exponen el servicio fuera de la tailnet.

El diff desde `06e6c86` también trae lo de los sprints 17 y 19, ya revisados en sus `seguridad.md` sobre la
misma base. Aquí se revisó lo propio del Sprint 14 (commit `2262dec`). Queda sin commitear un cambio de
`TZ` en `.env.example`, sin efecto en la seguridad.

## Hallazgos

Ninguno.

## Dependencias
- `dotnet list package --vulnerable --include-transitive`: sin paquetes vulnerables.
- `npm audit --omit=dev` (frontend; cambió `package-lock.json`): 0 vulnerabilidades.
- `keycloak/theme`: no cambió, no se auditó.

## Revisado sin hallazgos
- **Service worker** (`frontend/public/sw.js`):
  - Solo guarda `offline.html` en la caché. Atiende únicamente navegaciones (`mode === 'navigate'`) y deja pasar
    `/auth/`.
  - `/api`, `/hubs`, los assets y las URLs prefirmadas nunca pasan por la caché, así que no quedan tokens ni video en el dispositivo.
  - Se registra después de `initOidc()` y solo en producción.
- **Manifest e `index.html`**: solo rutas internas (`/live`, `/latest`) y los íconos. El script en línea solo lee el
  tema de `localStorage`.
- **nginx**:
  - El `map` de `X-Forwarded-Proto` respeta el valor que manda quien llama. Un cliente que lo falsee solo cambia
    cómo Keycloak arma la respuesta a su propia petición: el issuer queda fijo en `KC_HOSTNAME` y no hay caché compartida.
  - `/sw.js` y `/manifest.webmanifest` son archivos estáticos con `no-cache`.
  - No hay `proxy_pass` nuevos.
- **Frontend**:
  - `LatestRedirect` arma la URL con `URLSearchParams` (día e id del clip).
  - Los enlaces del mosaico usan el `id` numérico de la cámara.
  - No hay `dangerouslySetInnerHTML` ni `href` con datos externos.
  - El vivo sigue pasando por `POST /api/live/{id}/webrtc` con token; el mosaico no conecta solo.
- **Tailscale** (`docs/tailscale.md`): se usa `tailscale serve`, que solo publica dentro de la tailnet. No se usa `funnel`
  ni se abren puertos en el router.
  - El comando `kcadm.sh` lee la contraseña de admin de las variables del propio contenedor. No queda en el historial del host
    ni en archivos del repositorio.
  - Las redirect URIs propuestas son `$PUBLIC_URL/*` (mismo origen, sin comodín de host).
- **Secretos commiteados**: ninguno. `.env.example` solo trae valores de ejemplo.
