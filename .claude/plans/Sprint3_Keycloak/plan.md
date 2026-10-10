# Sprint 3 — Login con Keycloak + Keycloakify

> Al aprobarse se mueve a `.claude/plans/Sprint3_Keycloak/plan.md`.

## Contexto

Hoy hay un solo usuario `admin` definido en configuración (`Auth:Username/Password` en texto plano). `POST /api/auth/login` ([Login.cs](backend/src/CamGuard.Api/Features/Auth/Login.cs)) emite un JWT HS256 propio, y el frontend lo guarda en localStorage ([client.ts](frontend/src/api/client.ts), [useAuth.tsx](frontend/src/hooks/useAuth.tsx)). Queremos pasar la identidad a **Keycloak**, con estas decisiones ya tomadas:
- Keycloak es un servicio nuevo en compose.
- Sustituye por completo el login propio.
- La SPA usa el flujo **Authorization Code + PKCE** con redirección.
- Las páginas de **login y los correos** llevan un tema propio hecho con **Keycloakify**.

Nada en la BD referencia usuarios (no hay `CreatedBy` ni tabla de usuarios), así que no hay migración de datos.

## Arquitectura

```
navegador ──► web (nginx :80)
               ├─ /          SPA (oidc-spa)
               ├─ /api, /hubs ► api:8080   (JwtBearer valida tokens de Keycloak)
               └─ /auth/      ► keycloak:8080 (KC_HTTP_RELATIVE_PATH=/auth, tema keycloakify)
keycloak ──► keycloak-db (postgres)
api ──(JWKS interno)──► http://keycloak:8080/auth/realms/camguard
```

- **Mismo origen.** Keycloak se publica en `${PUBLIC_URL}/auth`, así que no hace falta CORS. La SPA calcula el issuer en tiempo de ejecución como `${location.origin}/auth/realms/camguard` y por eso no necesita `VITE_*` en el build. La ruta `/auth/` queda libre porque se elimina `/api/auth/login`.
- **Hostname de Keycloak.** Se configura con `KC_HOSTNAME=${PUBLIC_URL}/auth`, `KC_HOSTNAME_BACKCHANNEL_DYNAMIC=true`, `KC_PROXY_HEADERS=xforwarded` y `KC_HTTP_ENABLED=true`. Con eso el `iss` es siempre el público y la API puede leer metadata y JWKS por la red interna.
- **Base de datos.** Postgres dedicado (`postgres:17-alpine`, volumen `kcdata`) en lugar de compartir SQL Server, porque el soporte MSSQL de Keycloak exige XA y configuración extra.

## 1. Keycloak: realm y servicio

**Nuevo directorio `keycloak/`:**
- `realm-camguard.json`, que se importa con `start --import-realm`. Keycloak solo lo importa si el realm no existe y sustituye los placeholders `${ENV}`. Define:
  - Realm `camguard` con `registrationAllowed: false`, `resetPasswordAllowed: true`, `loginWithEmailAllowed: true`, i18n con `es` por defecto, `loginTheme` y `emailTheme` = `camguard`, y `bruteForceProtected: true` (sustituye el rate limiter del login).
  - Cliente público `camguard-web` con `standardFlowEnabled`, PKCE `S256`, `redirectUris: ["${PUBLIC_URL}/*"]`, `webOrigins: ["+"]` y `post.logout.redirect.uris`.
  - Client scope con un **audience mapper** que añade `camguard-api` al `aud` del access token.
  - Usuario inicial `${CAMGUARD_ADMIN_USER}` / `${CAMGUARD_ADMIN_PASSWORD}`, con la credencial marcada como temporal para obligar a cambiarla en el primer login.
  - `smtpServer` con `${SMTP_HOST}`, `${SMTP_PORT}`, `${SMTP_FROM}`, `${SMTP_USER}` y `${SMTP_PASSWORD}`, para los correos de reset de contraseña.
- `Dockerfile`, en varias etapas:
  1. `node:24` + Maven/JDK 21 compila el tema con `npx keycloakify build`, que genera un `.jar`.
  2. `quay.io/keycloak/keycloak:26.x` copia el jar a `/opt/keycloak/providers/` y ejecuta `kc.sh build --db=postgres --http-relative-path=/auth --health-enabled=true`.
  3. La imagen final arranca con `start --optimized --import-realm`.

**[docker-compose.yml](docker-compose.yml):**
- Nuevo servicio `keycloak-db` (postgres) con healthcheck.
- Nuevo servicio `keycloak` (`build: ./keycloak`), con `depends_on` sobre el db healthy, healthcheck en `/auth/health/ready` (puerto de gestión 9000) y el realm montado `:ro` en `/opt/keycloak/data/import`. Recibe `KC_BOOTSTRAP_ADMIN_USERNAME/PASSWORD`, que es el admin de la consola del realm master.
- Para desarrollo se publica `127.0.0.1:${KEYCLOAK_DEV_PORT:-8180}:8080`.
- `web` pasa a depender de `keycloak` y `api`.
- La api cambia `Auth__Username/Password/JwtKey` por `Auth__Authority`, `Auth__MetadataAddress` (interno) y `Auth__Audience`.

**[frontend/nginx.conf](frontend/nginx.conf):** se añade `location /auth/` con `set $kc_upstream http://keycloak:8080`, usando el mismo patrón de `resolver` y variable (ver CLAUDE.md). Lleva `X-Forwarded-Host/Proto/Port` y `proxy_buffer_size 128k`, porque las cookies y cabeceras de Keycloak son grandes.

**`.env.example` y [scripts/up.ps1](scripts/up.ps1):**
- Se quitan `ADMIN_USER`, `ADMIN_PASSWORD` y `JWT_KEY`.
- Se añaden `KC_DB_PASSWORD`, `KC_ADMIN_PASSWORD`, `CAMGUARD_ADMIN_USER`, `CAMGUARD_ADMIN_PASSWORD` y `SMTP_*`.
- `up.ps1` valida las nuevas variables obligatorias.

## 2. Tema Keycloakify (`keycloak/theme/`)

- Se parte de `keycloakify-starter` (Vite + React + TS, Keycloakify v11) y se nombra el tema `camguard` en `vite.config.ts` (`keycloakify({ themeName: "camguard", accountThemeImplementation: "none" })`).
- **Login:** se aplican los tokens CSS y el logo de CamGuard, copiando variables de la hoja de estilos del frontend, a `Template.tsx`. Con `npx keycloakify eject-page` se personalizan `login.ftl`, `login-reset-password.ftl`, `login-update-password.ftl` (primer login con credencial temporal), `info.ftl` y `error.ftl`. Las demás páginas usan el `DefaultPage` con el `Template` propio.
- **Emails:** se ejecuta `npx keycloakify initialize-email-theme` y se ajustan las plantillas FTL (HTML y texto) de `password-reset`, `executeActions` y `email-verification` en español con la marca CamGuard.
- **Mensajes:** textos en español mediante `i18n` (`.withCustomTranslations({ es: … })`).
- **Desarrollo del tema:** Storybook (`npm run storybook`) y `npx keycloakify start-keycloak`. Se documenta en el README.

## 3. Backend

**[Common/Auth/AuthSetup.cs](backend/src/CamGuard.Api/Common/Auth/AuthSetup.cs):**
- `AuthOptions` pasa a tener `Authority` (issuer público), `MetadataAddress` (opcional, la URL interna `.well-known`), `Audience = "camguard-api"` y `RequireHttpsMetadata = false`.
- `AddJwtAuth` usa `jwt.Authority` y `jwt.MetadataAddress`, y fija `ValidIssuer = Authority` y `ValidAudience = Audience`.
- La parte pura se extrae a `static TokenValidationParameters ValidationParameters(AuthOptions)` para poder probarla.
- Se conservan `MapInboundClaims = false`, el `OnMessageReceived` de `/hubs` y la fallback policy.
- Se valida al arrancar que `Authority` no esté vacío, igual que ahora se valida `JwtKey`.

**Se eliminan:**
- `Features/Auth/Login.cs`.
- El rate limiter de login en [Program.cs](backend/src/CamGuard.Api/Program.cs) (`AddRateLimiter`/`UseRateLimiter`), si no queda otro uso.
- Las claves `Auth` viejas en `appsettings*.json`. `appsettings.Development.json` pasa a `Authority: http://localhost:5173/auth/realms/camguard` y `MetadataAddress: http://localhost:8180/auth/realms/camguard/.well-known/openid-configuration`.

**Pruebas:**
- Se reemplaza `tests/CamGuard.Tests/Features/Auth/LoginTests.cs` por `Common/Auth/AuthSetupTests.cs`.
- Cada prueba firma un token con una `RsaSecurityKey` local y lo valida con `ValidationParameters(...)` mediante `JsonWebTokenHandler`:
  - `Validate_TokenFromAuthorityWithAudience_Valid`
  - `Validate_WrongAudience_Invalid`
  - `Validate_WrongIssuer_Invalid`
  - `Validate_Expired_Invalid`

## 4. Frontend

- **Dependencia:** `oidc-spa`, la librería del autor de Keycloakify. Al implementar se comprueba su API actual (`createReactOidc` / `oidcSpa`).
- **`src/auth/oidc.ts`:**
  - Se configura con `issuerUri: ${location.origin}/auth/realms/camguard`, `clientId: "camguard-web"` y `homeUrl: import.meta.env.BASE_URL`.
  - Exporta `OidcProvider`, `useOidc` y `getOidc`.
  - Añade `public/silent-sso.html` si la versión de oidc-spa lo requiere.
- **[client.ts](frontend/src/api/client.ts):**
  - Se borran `loadAuth`, `saveAuth`, `setToken` y la clave de localStorage.
  - `request` obtiene el token con `await getOidc()` → `getTokens().accessToken`, que oidc-spa renueva solo.
  - Un 401 llama a `logout`.
  - Se eliminan `api.login` y el mensaje de "Usuario o contraseña incorrectos".
- **[types.ts](frontend/src/api/types.ts):** se quita `LoginResponse`.
- **[useAuth.tsx](frontend/src/hooks/useAuth.tsx):** queda como una capa fina sobre `useOidc`, con `{ isLoggedIn, username (preferred_username), login, logout }`, para no tocar a los consumidores. Se elimina el timer de expiración.
- **[App.tsx](frontend/src/App.tsx):**
  - `RequireAuth` llama a `login({ doesCurrentHrefRequiresAuth: true })`, que redirige a Keycloak, en lugar de navegar a `/login`.
  - Se elimina la ruta `/login` y `features/auth/LoginPage.tsx`.
  - `OidcProvider` envuelve a `AuthProvider`.
- **[useEventsHub.tsx](frontend/src/hooks/useEventsHub.tsx):** `accessTokenFactory: async () => (await getOidc()).getTokens()...accessToken` y la conexión depende de `isLoggedIn`.
- **[Layout.tsx](frontend/src/components/Layout.tsx):** muestra el `username`. "Salir" ejecuta `logout({ redirectTo: "home" })`, que pasa por el end-session de Keycloak.
- **[vite.config.ts](frontend/vite.config.ts):** se añade un proxy `/auth` hacia `http://localhost:8180` (con `xfwd`).

## 5. Documentación

- **CLAUDE.md:**
  - Se actualiza la línea de JWT: la API valida tokens de Keycloak (realm `camguard`, audience `camguard-api`).
  - Se describe la estructura `keycloak/` (realm y tema).
  - Se cambia `client.ts` para reflejar que el token viene de oidc-spa.
  - Se añaden a "Detalles que ya causaron bugs" las notas de `KC_HOSTNAME` y de la ruta `/auth`.
- **README:**
  - Primer arranque: login con `CAMGUARD_ADMIN_*` y cambio obligatorio de contraseña.
  - Consola de Keycloak en `${PUBLIC_URL}/auth/admin`.
  - Desarrollo del tema.
- **[scripts/backup-db.sh](scripts/backup-db.sh):** se añade un `pg_dump` de `keycloak-db`.

## Verificación

1. `cd backend && dotnet build && dotnet test`: deben pasar las pruebas nuevas de `AuthSetupTests`.
2. `cd frontend && npm run build && npm run lint`, y `cd keycloak/theme && npm run build-keycloak-theme`: debe generarse el jar.
3. `docker compose up -d --build`, abrir `${PUBLIC_URL}` y comprobar en orden:
   1. Redirige a `/auth/realms/camguard/...` y se ve el tema CamGuard en español.
   2. El login con `CAMGUARD_ADMIN_*` pide cambiar la contraseña.
   3. Vuelve a la SPA.
   4. Cargan las cámaras y las grabaciones.
   5. SignalR conecta: `CameraAwake` llega en vivo.
   6. Funciona la vista en vivo por WebRTC.
4. `curl /api/cameras` sin token devuelve 401. Un token del realm `master` también devuelve 401, porque falla la validación de issuer/audience.
5. "Salir" cierra la sesión también en Keycloak: el siguiente acceso vuelve a pedir credenciales.
6. "¿Olvidaste tu contraseña?" debe enviar el correo con la plantilla CamGuard. Se prueba en local con un SMTP de pruebas (p. ej. mailpit como servicio opcional de compose).
7. Esperar a que caduque el access token (5 min por defecto) y seguir navegando: debe refrescarse sin recargar.

## Notas de implementación (desvíos del plan)

- **Frontend:** se usa `oidc-spa/core` v10 con `autoLogin: true` en vez del adaptador de React con `RequireAuth`. `main.tsx` llama a `oidcEarlyInit` y carga `bootstrap.tsx`, que espera a `initOidc()` antes de montar. No hay ruta pública, así que la app solo se monta con sesión.
- **Tema:** no se expulsan páginas. Se expulsó `Template.tsx` (marca CamGuard, sin selector de idioma) y `main.css` sobrescribe el CSS de Keycloak con los tokens de la web. Así todas las páginas quedan con la marca.
- **Correos:** se usa `@keycloakify/email-native`, el equivalente no interactivo de `initialize-email-theme`. Con `keycloakify own` se adaptaron `template.ftl`, `password-reset.ftl`, `executeActions.ftl` y `messages_es`.
- **Realm:** va copiado en la imagen, no montado.
- **Usuario inicial:** hace falta `requiredActions: ["UPDATE_PASSWORD"]`, porque Keycloak ignora `temporary` al importar.
- **SMTP:** `SMTP_FROM` lleva un valor por defecto. Si queda vacío, el import falla.
- **Verificado contra Keycloak 26.8 en un stack aislado:**
  - Login con tema y cambio obligatorio de contraseña.
  - Token con `iss` público y `aud=camguard-api`.
  - API: `200` con token válido (REST y negotiate del hub) y `401` sin token, con token alterado y con token de `master`.
  - Proxy `/auth` de Vite funcionando.
