# Notificaciones con ntfy

CamGuard avisa por [ntfy](https://ntfy.sh) cada vez que sube un clip: título
"Movimiento: <cámara>", hora y duración, la miniatura (URL prefirmada de R2, válida 24 h)
y un enlace que abre la grabación en la web. Si la notificación falla, el clip ya está a
salvo en R2: solo queda un `warn` en el log de la API.

Configuración en `.env` (la API las lee como `Ntfy__Url` y `Ntfy__Token`):

| Variable     | Valor                                                          |
|--------------|----------------------------------------------------------------|
| `NTFY_URL`   | URL completa del tópico, p. ej. `https://ntfy.domuspay.cl/camguard`. Vacía = sin notificaciones. |
| `NTFY_TOKEN` | Token de acceso (`tk_...`) si el servidor tiene autenticación. Vacío = sin token. |

## Diagnóstico (2026-10-12)

`NTFY_URL=https://ntfy.domuspay.cl/camguard` sin `NTFY_TOKEN`. La API registraba:

```
No se pudo enviar la notificación de <id>
HttpRequestException: Response status code does not indicate success: 403 (Forbidden)
```

El servidor está sano (`/v1/health` → `{"healthy":true}`), pero tiene control de
acceso: sin credenciales responde 403 tanto al leer como al publicar en `camguard`.
Hay dos salidas: crear un usuario y un token en ese servidor (opción A) o usar el
servidor público `ntfy.sh` (opción B).

## Opción A: servidor propio (`ntfy.domuspay.cl`)

### Servidor compartido con otras apps

`ntfy.domuspay.cl` corre en Railway y ya lo usa el sensor de movimiento (Sensor Movement GO).
No hace falta otro servidor: ntfy aísla por tópico.

- **Tópico propio:** CamGuard publica en `camguard`; el sensor sigue en el suyo.
- **Usuario y token propios para CamGuard**, con permiso de solo escritura sobre `camguard`. Un
  token filtrado no puede publicar en el tópico del sensor y se revoca sin tocarlo.
- **Usuario del celular** con solo lectura en ambos tópicos (`ro`), suscrito a cada uno por separado.

**Cómo está montado:** imagen `binwiederhier/ntfy:latest` con un `server.yml` propio
(`base-url: https://ntfy.domuspay.cl`, `auth-default-access: deny-all`,
`upstream-base-url: https://ntfy.sh`). Usuarios y permisos viven en
`auth-file: /var/lib/ntfy/user.db`. Las variables `Ntfy__*` / `Tuya__*` que se ven en
Railway son del sensor (cliente que publica en `sensor_movimiento`), no del servidor.

**Requisito:** el servicio de ntfy debe tener un volumen montado en `/var/lib/ntfy`
(Railway → servicio → Settings → Volumes). Sin él, cada deploy borra `user.db` con todos los
usuarios, también el del sensor.

Para crear el acceso de CamGuard, abre una terminal en el contenedor (pestaña *Console* del
servicio o `railway ssh`) y sigue los pasos 2 y 3 de abajo; la CLI lee `/etc/ntfy/server.yml`
y escribe en ese `user.db`. Usa `ntfy user list` para ver el nombre de tu usuario del celular.

### Con la CLI de ntfy

Requiere acceso de administrador al servidor (SSH o el host de Docker donde corre ntfy).

### 1. Verificar la configuración del servidor

En `server.yml` de ntfy (normalmente `/etc/ntfy/server.yml`):

```yaml
base-url: "https://ntfy.domuspay.cl"
auth-file: "/var/lib/ntfy/user.db"
auth-default-access: "deny-all"     # nadie lee ni publica sin permiso explícito
behind-proxy: true                   # si está detrás de nginx/Caddy/Cloudflare
upstream-base-url: "https://ntfy.sh" # necesario para que la app de iPhone reciba al instante
```

Si cambias algo, reinicia ntfy (`systemctl restart ntfy` o `docker restart <contenedor>`).

### 2. Crear el usuario de CamGuard (solo publica)

Si ntfy corre en Docker, antepón `docker exec -it <contenedor-ntfy>` a cada comando.

```sh
ntfy user add camguard                # pide una contraseña; no se usará, pero es obligatoria
ntfy access camguard camguard wo      # usuario camguard: solo escritura en el tópico camguard
ntfy token add camguard               # imprime un token tk_xxxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

Guarda el token: es lo que va en `NTFY_TOKEN`.

### 3. Crear tu usuario para el celular (solo lectura)

```sh
ntfy user add felipe
ntfy access felipe camguard ro
ntfy access                           # lista usuarios y permisos para comprobar
```

### 4. Configurar CamGuard

En `.env`:

```sh
NTFY_URL=https://ntfy.domuspay.cl/camguard
NTFY_TOKEN=tk_xxxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

Sigue en el paso [Aplicar y probar](#aplicar-y-probar).

## Opción B: servidor público `ntfy.sh`

Sin servidor propio ni cuentas. El tópico es público: **cualquiera que conozca el
nombre puede leer los avisos**, incluida la miniatura durante 24 h. Por eso el
nombre tiene que ser largo e imposible de adivinar.

1. Genera un nombre aleatorio (PowerShell):

   ```powershell
   "camguard-" + [guid]::NewGuid().ToString('N')
   ```

2. En `.env`:

   ```sh
   NTFY_URL=https://ntfy.sh/camguard-<lo-generado>
   NTFY_TOKEN=
   ```

3. Sigue en el paso [Aplicar y probar](#aplicar-y-probar).

## Suscribirse desde el celular

1. Instala **ntfy** (Google Play, F-Droid o App Store).
2. Opción A: en *Ajustes → Usuarios* agrega el servidor `https://ntfy.domuspay.cl` con
   el usuario y la contraseña del paso 3. Luego *Suscribirse a un tópico* → `camguard`,
   marcando "Usar otro servidor" con `https://ntfy.domuspay.cl`.
3. Opción B: *Suscribirse a un tópico* → el nombre generado, en el servidor por
   defecto (`ntfy.sh`).

## Aplicar y probar

1. Recrea la API para que lea el `.env` (en Windows, siempre con el override):

   ```powershell
   docker compose -f docker-compose.yml -f docker-compose.windows.yml up -d api
   ```

2. Publica un mensaje de prueba con los mismos datos del `.env` (PowerShell):

   ```powershell
   $cfg = @{}; Get-Content .env | ? { $_ -match '^(NTFY_URL|NTFY_TOKEN)=(.*)$' } | % { $cfg[$matches[1]] = $matches[2] }
   $headers = @{}; if ($cfg.NTFY_TOKEN) { $headers.Authorization = "Bearer $($cfg.NTFY_TOKEN)" }
   Invoke-RestMethod -Method Post -Uri $cfg.NTFY_URL -Headers $headers -Body "Prueba de CamGuard"
   ```

   Debe responder con un JSON (`id`, `time`, `event: message`) y llegar el aviso al celular.

3. Espera a que la cámara grabe un clip y revisa que no aparezca el error:

   ```powershell
   docker compose -f docker-compose.yml -f docker-compose.windows.yml logs --since 10m api | Select-String notificación
   ```

## Errores comunes

| Respuesta | Causa probable |
|-----------|----------------|
| `403 Forbidden` | Falta `NTFY_TOKEN`, o el usuario del token no tiene permiso de escritura (`wo`/`rw`) en el tópico. |
| `401 Unauthorized` | Token mal copiado o revocado (`ntfy token list camguard`). |
| `429 Too Many Requests` | Límite de publicaciones (sobre todo en `ntfy.sh`). |
| Llega el aviso sin imagen | La URL de la miniatura caducó (24 h) o el celular no alcanza R2. |
| En iPhone llega tarde | Falta `upstream-base-url: "https://ntfy.sh"` en el servidor propio. |
