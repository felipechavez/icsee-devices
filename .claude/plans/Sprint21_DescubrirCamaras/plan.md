# Descubrir cámaras en la red

> Sprint 21 (`Sprint21_DescubrirCamaras`). El hook lo dejará en el primer sprint reservado (Sprint 9): tras aprobarlo se mueve a mano a `Sprint21_DescubrirCamaras/plan.md` y se restaura `Sprint9_AvisosEnriquecidos` sin `plan.md`.

## Contexto

Hoy una cámara nueva se agrega a mano en Configuración: hay que saber su IP (iCSee/ONVIF) o su Device ID (Tuya). El usuario quiere un botón, **solo para administradores**, que busque cámaras que aún no están en CamGuard y deje el alta lista para completar.

Decisiones tomadas:
- **Rol mínimo `camguard-admin`** en Keycloak (adelanta parte del Sprint 16). Por ahora solo protege el descubrimiento; el resto de los endpoints sigue igual hasta el Sprint 16.
- Busca **iCSee, ONVIF y Tuya**.
- El resultado **precarga el formulario de alta** (`CameraPanel` con la clave `new`); el usuario escribe la contraseña, usa "Probar conexión" y guarda. Una Tuya no pide contraseña, así que basta con guardar.

Restricciones técnicas:
- La API corre en la red bridge de Docker: **no puede usar multicast/broadcast** hacia la LAN (WS-Discovery UDP 3702, búsqueda XM UDP 34569). Se usa un **barrido TCP** de la subred (unicast, pasa por el NAT), confirmando cada puerto abierto con el protocolo real.
- Una iCSee a batería dormida no abre el 34567: solo aparece si está despierta. La UI lo avisa ("despiértala antes de buscar").
- Tuya no necesita la red: lista los dispositivos del proyecto por la Cloud API.

## Backend

### Rol de administrador (`Common/Auth/AuthSetup.cs`)
- Constantes `AdminRole = "camguard-admin"` y `AdminPolicy = "Admin"`.
- `static bool HasRealmRole(ClaimsPrincipal user, string role)`, pura: con `MapInboundClaims = false`, Keycloak manda `realm_access` como JSON (`{"roles":[...]}`). Se parsea con `JsonDocument`; un JSON inválido o ausente da `false`.
- `AddAuthorizationBuilder().AddPolicy(AdminPolicy, p => p.RequireAuthenticatedUser().RequireAssertion(c => HasRealmRole(c.User, AdminRole)))`.
- Nuevo `Features/Account/GetMe.cs`: `GET /api/me` → `{ username, isAdmin }`. El frontend lo usa para mostrar u ocultar el botón; la seguridad real la da la policy.

### Keycloak (`keycloak/realm-camguard.json`)
- `"roles": { "realm": [{ "name": "camguard-admin", "description": "Administra CamGuard" }] }`, y `"realmRoles": ["camguard-admin"]` en el primer usuario.
- El realm se importa una sola vez, así que en las instalaciones existentes hay que crear y asignar el rol a mano. Nuevo `scripts/keycloak-admin-role.sh <usuario>`: `docker compose exec keycloak /opt/keycloak/bin/kcadm.sh` (login con `KC_ADMIN_*`, `create roles` si falta, `add-roles --uusername`). Se documenta en README.
- El scope `roles` (por defecto en Keycloak) ya pone `realm_access` en el access token. Se verifica en el token real.

### Slice `Features/Discovery/` (`AddDiscoveryFeature`)
- **`DiscoveryOptions`** (`Discovery:*`): `Subnets` (texto con CIDR separados por coma, p. ej. `192.168.1.0/24`), `ConnectTimeoutMs` = 400, `MaxConcurrency` = 64, `MaxHosts` = 1024, `DvripPort` = 34567, `OnvifPorts` = `[80, 8000, 2020, 8080]` y `TuyaCameraCategories` = `["sp", "videolock"]`.
- **`SubnetPlanner`** (pura): `Plan(string? configured, IEnumerable<string> cameraIps)` → subredes y lista de hosts. Sin `Subnets` configuradas, deriva los /24 de las IPs de las cámaras iCSee/ONVIF ya guardadas. Solo acepta IPv4 privadas (10/8, 172.16/12, 192.168/16), excluye la dirección de red y la de broadcast y rechaza más de `MaxHosts` con un aviso. Sin subredes posibles, aviso "Configura LAN_SUBNETS" y se sigue solo con Tuya.
- **`LanScanner`**: por cada host × puerto, `IPortProbe.IsOpenAsync` (se reutiliza `TcpPortProbe` de Monitoring), con `SemaphoreSlim(MaxConcurrency)`. Para un /24 con 5 puertos son ~1.270 conexiones, unos 10–15 s.
- **Confirmación** (que un puerto abierto no sea un router o una impresora):
  - DVRIP: `DvripClient.ConnectAsync` más **un** `LoginAsync("admin", "")`. Una respuesta Sofia válida confirma que es una iCSee: OK, o `DvripException` con `Ret` no nulo. Si el login entra (cámara sin clave), se lee el modelo con `DeviceInfoParser.ParseModel`. Es un solo intento por escaneo, para no bloquear la cuenta.
  - ONVIF: `OnvifClient.ConnectAsync` (`GetSystemDateAndTime`, **sin autenticación**). Responde SOAP, así que es ONVIF. No se prueban credenciales: una Dahua bloquea la cuenta tras varios logins fallidos.
- **Tuya**: `TuyaApiClient.ListDevicesAsync` usa `GET /v1.0/iot-01/associated-users/devices` (paginado con `last_row_key`/`has_more`). Si falla, cae a `/v1.0/users/{uid}/devices` con el `uid` de una cámara Tuya ya guardada (`GetDeviceAsync` agrega `Uid`). El parseo va en `static ParseDeviceList(JsonNode)` (pura). Se filtra por `TuyaCameraCategories`. Sin credenciales Tuya, se omite con un aviso.
- **`DiscoveryMerge`** (pura): recibe los hallazgos y las cámaras guardadas y devuelve los `DiscoveredCamera` nuevos:
  - Excluye las IPs que ya son de alguna cámara (sin volver a tocarlas) y los Device ID ya registrados.
  - Si una IP responde DVRIP y ONVIF, queda como **iCSee**, con el detalle "también ONVIF".
  - Nombre sugerido: el `name` de Tuya, o "iCSee 192.168.1.23" / "ONVIF 192.168.1.40".
  - Orden: tipo y luego IP.
- **`DiscoverCameras.Endpoint`**: `POST /api/cameras/discover`, `.RequireAuthorization(AuthSetup.AdminPolicy)`. Respuesta `{ found: DiscoveredCamera[], subnets: string[], warnings: string[], elapsedMs }`, con `DiscoveredCamera(Kind, Ip?, Port?, TuyaDeviceId?, SuggestedName, Model?, Detail?)`. Admite un solo escaneo a la vez (singleton `SemaphoreSlim`; ocupado → 409 "Ya hay una búsqueda en curso"). Respeta el `CancellationToken` de la petición.
- Logs en español: subredes, cantidad de hallazgos y duración. Nunca credenciales.

### Despliegue
- `docker-compose.yml` (api): `Discovery__Subnets: ${LAN_SUBNETS:-}`. `.env.example`: `LAN_SUBNETS=192.168.1.0/24` con un comentario.
- `scripts/up.ps1`: si `LAN_SUBNETS` está vacía, sugiere el /24 de la IP de la LAN que ya detecta (solo aviso; no escribe).

## Frontend
- `api/types.ts`: `Me`, `DiscoveredCamera` y `DiscoveryResult`. `api/client.ts`: `me()` y `discoverCameras()`.
- `features/account/useMe.ts`: `useQuery(['me'], api.me, { staleTime: Infinity })`.
- `SettingsPage.tsx`, sección Cámaras:
  - Junto a "+ Agregar cámara", botón **"Buscar en la red"** (`btn btn-ghost`), visible solo si `me.isAdmin`. Abre `DiscoveryPanel`.
  - El texto de introducción agrega ONVIF.
- `features/settings/DiscoveryPanel.tsx`:
  - Al buscar, un `useMutation` muestra "Revisando 192.168.1.0/24… puede tardar ~20 s" y el aviso de despertar las iCSee a batería.
  - Resultados: insignia de tipo, IP:puerto o nombre Tuya, modelo/detalle y botón **"Agregar"**. También avisos (`warnings`) y "No se encontraron cámaras nuevas". Botón "Buscar de nuevo".
- `cameraDraft.ts`: `draftFromDiscovery(found)` → `emptyDraft` más kind, ip, `dvripPort`/`onvifPort`, `tuyaDeviceId`, name y model, con `streamEdited: false` (el stream sigue al nombre).
- "Agregar" llama a `addCamera(draft)`: si ya hay un alta `new` con cambios, pide confirmar que se reemplace. Luego abre el panel. Al terminar, el foco va a la contraseña (iCSee/ONVIF).
- Estilos en `index.css` con los tokens existentes (`.discovery`, `.discovery-row`), sin librerías.

## Pruebas (TUnit, carpetas espejo)
- `Common/Auth/AuthSetupTests`: `HasRealmRole` con el rol, sin él, sin claim y con JSON inválido.
- `Features/Discovery/SubnetPlannerTests`: CIDR válidos e inválidos, varias subredes, tope `MaxHosts`, IP pública rechazada, /24 derivados de cámaras sin repetir y exclusión de red/broadcast.
- `Features/Discovery/DiscoveryMergeTests`: excluye IP y Device ID registrados, iCSee gana a ONVIF en la misma IP, nombres sugeridos y orden.
- `Features/Tuya/TuyaDeviceListTests`: `ParseDeviceList` (paginación y filtro de categorías).

## Documentación
- `CLAUDE.md`: viñeta de Discovery (barrido TCP porque la API está en bridge, confirmación sin credenciales, un solo intento DVRIP, `AdminPolicy`/`camguard-admin`) y `Discovery` en la lista de relaciones entre slices (lee Devices, Onvif, Tuya y Monitoring/`IPortProbe`).
- README: `LAN_SUBNETS`, cómo dar el rol en una instalación existente (`scripts/keycloak-admin-role.sh`) y la limitación de las iCSee dormidas.

## Fuera de alcance
- Proteger con el rol el resto de los endpoints de escritura (Sprint 16).
- WS-Discovery/broadcast (exigiría `network_mode: host` en la API).
- Probar contraseñas por defecto en ONVIF.

## Verificación
1. `cd backend && dotnet build && dotnet test`, y `cd frontend && npm run build && npm run lint`.
2. `docker compose up -d --build`. Instalación existente: `scripts/keycloak-admin-role.sh admin`, cerrar sesión y entrar de nuevo.
3. Con `LAN_SUBNETS` configurada, una iCSee despierta (timbre o movimiento) sin agregar y la Imou Cue 2 eliminada temporalmente (o una segunda ONVIF): "Buscar en la red" debe mostrar ambas, y no las ya guardadas. La XOOMI no debe aparecer porque ya está registrada.
4. "Agregar" → el formulario queda precargado. Escribir la contraseña, "Probar conexión" → OK con modelo, Guardar → aparece en la lista y go2rtc registra el stream.
5. Un usuario sin `camguard-admin`: no ve el botón, y `POST /api/cameras/discover` responde 403.
6. Dos búsquedas a la vez → la segunda responde 409.
7. Tuya sin credenciales → aviso, y el escaneo de la LAN funciona igual.
