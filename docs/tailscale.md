# Acceso remoto con Tailscale

[Tailscale](https://tailscale.com) arma una red privada (*tailnet*) entre el equipo de
CamGuard (p. ej. la Raspberry Pi) y tus dispositivos. No hace falta abrir puertos del
router ni tener IP pública. Con **Tailscale Serve** además se obtiene HTTPS con un
certificado válido, que es lo que pide el navegador para instalar CamGuard como app (PWA).

Resultado final:

| Qué | Dónde |
|-----|-------|
| Web y app | `https://<equipo>.<tailnet>.ts.net` (Tailscale Serve → nginx en `WEB_PORT`) |
| Video en vivo (WebRTC) | IP de Tailscale del equipo, puerto `8555` (go2rtc) |

Todo queda accesible solo para los dispositivos de tu tailnet; nada se publica en internet.

## 1. Cuenta y consola

1. Crea una cuenta en <https://login.tailscale.com> (Google, Microsoft, GitHub…). El
   plan Personal gratuito alcanza.
2. En la consola, pestaña **DNS**:
   - Activa **MagicDNS** (nombres como `camguard.<tailnet>.ts.net`).
   - En **HTTPS Certificates**, pulsa *Enable HTTPS*.

   El nombre de la tailnet (`<tailnet>.ts.net`) aparece en esa misma página.

> Los certificados HTTPS quedan en los registros públicos de *Certificate Transparency*:
> el nombre `camguard.<tailnet>.ts.net` será visible, aunque nadie fuera de tu tailnet
> pueda conectarse a él.

## 2. Instalar en el equipo de CamGuard (Raspberry Pi / Linux)

```sh
curl -fsSL https://tailscale.com/install.sh | sh
sudo tailscale up --hostname=camguard
```

`tailscale up` imprime un enlace: ábrelo y autoriza el equipo con tu cuenta. Después:

```sh
tailscale ip -4      # IP de Tailscale (100.x.y.z): va en HOST_IP
tailscale status     # equipos de la tailnet
```

En la consola (**Machines** → `camguard` → `…`), elige **Disable key expiry**. Si no lo
haces, la llave del equipo caduca a los 180 días y CamGuard deja de ser accesible hasta
que vuelvas a autorizarlo, algo difícil de hacer a distancia.

Para usar `tailscale serve` sin `sudo` (opcional):

```sh
sudo tailscale set --operator=$USER
```

## 3. HTTPS con Tailscale Serve

```sh
sudo tailscale serve --bg 8090      # el WEB_PORT de .env
tailscale serve status              # muestra https://camguard.<tailnet>.ts.net → http://127.0.0.1:8090
```

Con `--bg`, la configuración se guarda y vuelve a levantarse sola tras un reinicio. El
certificado lo pide y renueva Tailscale. La primera visita puede tardar unos segundos
mientras se emite. Para quitarlo: `sudo tailscale serve reset`.

nginx respeta el `X-Forwarded-Proto: https` que agrega Tailscale Serve, así que Keycloak
sabe que el navegador entra por https.

## 4. Configurar CamGuard

En `.env`:

```sh
HOST_IP=100.x.y.z                                 # tailscale ip -4
PUBLIC_URL=https://camguard.<tailnet>.ts.net      # sin puerto ni / final
WEB_PORT=8090                                     # al que apunta tailscale serve
```

```sh
docker compose up -d
```

- **`PUBLIC_URL`** es el issuer de los tokens de Keycloak y la base de los enlaces de ntfy.
  Abre la web siempre con esa URL: por la IP o por `http://` el login falla.
- **`HOST_IP`** es la dirección que go2rtc anuncia para el video en vivo (WebRTC, puerto
  8555). El video no pasa por Tailscale Serve: va directo del navegador a go2rtc por la
  tailnet. Por eso **el celular debe tener Tailscale conectado incluso en casa**. Con
  Tailscale apagado la web no abre y el vivo no conecta.

### Si el realm de Keycloak ya existía

El realm se importa con `PUBLIC_URL` solo la primera vez. Si CamGuard ya estaba instalado
con otra URL (p. ej. `http://…:8090`), hay que actualizar el cliente `camguard-web`. Si no,
Keycloak rechaza el login con *Invalid parameter: redirect_uri*.

**Desde la consola web:** la consola de administración (realm *master*) sigue sola a
`KC_HOSTNAME`, así que funciona con la URL nueva: `https://camguard.<tailnet>.ts.net/auth/admin`.
En realm `camguard` → *Clients* → `camguard-web`, cambia *Root URL*, *Home URL*,
*Valid redirect URIs* (`https://camguard.<tailnet>.ts.net/*`) y
*Valid post logout redirect URIs*.

**Desde la terminal** (toma `PUBLIC_URL` del contenedor, que ya tiene el valor nuevo):

```sh
docker compose exec keycloak sh -c '
  /opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080/auth --realm master --user "$KC_BOOTSTRAP_ADMIN_USERNAME" --password "$KC_BOOTSTRAP_ADMIN_PASSWORD" &&
  ID=$(/opt/keycloak/bin/kcadm.sh get clients -r camguard -q clientId=camguard-web --fields id --format csv --noquotes) &&
  /opt/keycloak/bin/kcadm.sh update clients/$ID -r camguard -s rootUrl="$PUBLIC_URL" -s baseUrl="$PUBLIC_URL/" \
    -s "redirectUris=[\"$PUBLIC_URL/*\"]" -s "attributes.\"post.logout.redirect.uris\"=$PUBLIC_URL/*"'
```

Si cambiaste la contraseña del admin después de crear Keycloak,
`KC_BOOTSTRAP_ADMIN_PASSWORD` ya no sirve y hay que usar la actual. En Windows (Git Bash),
antepón `MSYS_NO_PATHCONV=1` para que no reescriba las rutas.

## 5. Celular y otros equipos

1. Instala la app **Tailscale** (Play Store / App Store; en Windows o macOS, desde
   <https://tailscale.com/download>) y entra con la **misma cuenta**.
2. Deja Tailscale activado. En Android, para que no se corte, quita la optimización de
   batería de la app y activa *VPN siempre activa* en los ajustes del sistema.
3. Abre `https://camguard.<tailnet>.ts.net` e inicia sesión.
4. Instala CamGuard como app:
   - **Android (Chrome):** menú `⋮` → *Instalar app*. Al mantener presionado el ícono
     aparecen los atajos **En vivo** y **Último clip**.
   - **iPhone (Safari):** Compartir → *Agregar a pantalla de inicio*.

Para dar acceso a un familiar sin compartir tu cuenta, invítalo a la tailnet desde la
consola (**Users** → *Invite users*) o comparte solo el equipo `camguard` (**Machines** →
`…` → *Share*). Además necesita su propio usuario en Keycloak (ver "Usuarios y login" en
el README).

## Verificación

En el equipo:

```sh
tailscale status                                         # camguard en línea
tailscale serve status                                   # https://… → 127.0.0.1:8090
curl -sI https://camguard.<tailnet>.ts.net | head -1     # HTTP/2 200
```

Desde el celular (con Tailscale conectado y el wifi apagado, para probar fuera de casa):
la web carga con candado, el login vuelve a CamGuard y en **Vivo** una cámara despierta
muestra video.

## Problemas comunes

| Síntoma | Causa probable |
|---------|----------------|
| La URL `.ts.net` no carga | Tailscale apagado en el celular, MagicDNS desactivado o `tailscale serve` sin configurar (`tailscale serve status`). |
| Error de certificado o la primera carga tarda | HTTPS Certificates sin activar en la consola, o el certificado se está emitiendo (espera un minuto). |
| *Invalid parameter: redirect_uri* al iniciar sesión | El cliente `camguard-web` sigue con la URL anterior (ver "Si el realm de Keycloak ya existía"). |
| El login vuelve a una URL `http://` o con puerto | `PUBLIC_URL` en `.env` no coincide con la URL de Tailscale Serve, o falta `docker compose up -d` tras cambiarla. |
| La web funciona pero el vivo dice "No se pudo conectar con el video" | `HOST_IP` no es la IP de Tailscale (`tailscale ip -4`), o el celular no tiene Tailscale activo. |
| No aparece "Instalar app" | La página no se abrió por https, o Chrome ya la tiene instalada. |
| Dejó de responder tras meses | Caducó la llave del equipo: vuelve a autorizarlo con `sudo tailscale up` y desactiva *key expiry*. |
