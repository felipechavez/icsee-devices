---
description: Revisión de seguridad del código de un sprint terminado; deja el informe en su carpeta de plan
argument-hint: [número de sprint, p. ej. 17] (vacío = los terminados sin revisar)
allowed-tools: Bash(git diff:*), Bash(git log:*), Bash(git status:*), Bash(git ls-files:*), Bash(git rev-parse:*), Bash(git show:*), Bash(dotnet list:*), Bash(npm audit:*), Bash(powershell.exe -NoProfile -ExecutionPolicy Bypass -File:*)
---

# /cerrar-sprint

Objetivo: buscar código vulnerable introducido en uno o más sprints y dejar el resultado en `.claude/plans/Sprint<N>_<Nombre>/seguridad.md`. **No corrige nada**: informa y ofrece los arreglos.

Argumento: `$ARGUMENTS`

## 1. Qué revisar

Ejecuta `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .claude/hooks/check-sprint-security.ps1 -List`. Devuelve:
- `base:` commit desde el que se revisa (el último revisado, o `baseCommit` de `.claude/hooks/security-baseline.json`).
- `pendientes:` sprints terminados sin `seguridad.md`.

Si `$ARGUMENTS` trae un número, revisa ese sprint (aunque sea el en curso: así se cierra a mano). Si no, todos los pendientes; si no hay ninguno, dilo y termina.

Varios sprints sin commits entre medio comparten el mismo diff: haz **una** revisión y escribe el mismo informe en la carpeta de cada uno (indicando los sprints cubiertos).

## 2. Reunir el cambio

En paralelo:
- `git rev-parse --short HEAD` (será el `commit:` del informe).
- `git diff --stat <base>` y `git diff <base>` (incluye lo no commiteado).
- `git ls-files --others --exclude-standard` (archivos nuevos sin seguimiento: léelos, no salen en el diff).
- Lee el `plan.md` de cada sprint para entender la intención.

Revisa solo lo que cambió, pero lee el código vecino que haga falta para confirmar un hallazgo (quién llama, de dónde viene el dato).

## 3. Herramientas automáticas

En paralelo (si una falla por falta de red o herramienta, anótalo en el informe y sigue):
- `dotnet list backend/src/CamGuard.Api package --vulnerable --include-transitive`
- `npm audit --omit=dev` en `frontend/` y en `keycloak/theme/` (solo si cambió su `package-lock.json` o pasaron más de 30 días desde la última revisión).

## 4. Revisión manual

Lista base, con lo que este proyecto ya sabe que es delicado (ver `CLAUDE.md`):

**Backend (.NET)**
- Autorización: endpoints nuevos con `.AllowAnonymous()` sin justificación; endpoints solo de Development (`dvrip-probe`) que puedan quedar expuestos en producción.
- Secretos en logs, excepciones o respuestas: origen `dvrip://` (lleva contraseña), URL RTSPS de Tuya, `Tuya:*`, credenciales R2, tokens ntfy. Contraseñas DVRIP que salgan sin `CameraSecretProtector` o en un DTO (debe ser `HasPassword`).
- Inyección de comandos o argumentos en FFmpeg (`ProcessStartInfo`, `ArgumentList` vs. string concatenado) y en cualquier `Process.Start`.
- Path traversal: rutas bajo `{DataDirectory}` (clips, queue, notify-queue, keys) o claves de R2 armadas con datos del usuario o de la cámara; `StorageKeys.IsManaged` respetado antes de borrar.
- SSRF: IPs/hosts que el usuario controla (`POST /api/cameras/test`, orígenes de go2rtc vía `PATCH /api/streams`, URLs de ntfy).
- Deserialización de mensajes externos (Tuya Pulsar, JSON en disco) sin validar; descifrado AES sin verificar.
- SQL crudo (`FromSqlRaw`, `ExecuteSqlRaw`) con interpolación.
- Validación de entrada en request (`Validate(...)`), límites de tamaño, enteros fuera de rango.
- SignalR: hubs que acepten mensajes del cliente o emitan datos de más.
- Comparaciones de tokens o firmas no constantes; `TuyaSigner`.

**Frontend (React)**
- `dangerouslySetInnerHTML`, `href`/`src` con datos sin sanear (`javascript:`), URLs prefirmadas guardadas en caché o localStorage.
- Tokens en localStorage o en logs de consola.

**Despliegue y scripts**
- `docker-compose.yml`: puertos nuevos publicados en `0.0.0.0`, contenedores `privileged`, montajes de escritura innecesarios (`go2rtc.yaml` debe seguir `:ro`), secretos fuera de `.env`.
- nginx: cabeceras, `proxy_pass` nuevos, rutas sin auth.
- Scripts `sh`/`ps1`: variables sin comillas, `eval`, contraseñas en la línea de comandos o en archivos de respaldo sin permisos.
- Keycloak: cambios en `realm-camguard.json` (redirect URIs con comodín, `directAccessGrants`, clientes públicos nuevos).

**Secretos commiteados**: busca en el diff claves, tokens, contraseñas o IPs públicas con credenciales.

## 5. Verificar cada hallazgo

Para cada candidato, confirma que es explotable: de dónde viene el dato, si alguien sin privilegios lo controla, qué lo mitiga ya. Descarta lo que no se sostenga y no listes problemas teóricos sin camino de ataque ni estilo. Clasifica:
- **Alta**: explotable sin autenticación o expone secretos/video.
- **Media**: requiere sesión válida o una condición extra.
- **Baja**: endurecimiento recomendable.

## 6. Informe

Escribe `.claude/plans/Sprint<N>_<Nombre>/seguridad.md`:

```markdown
---
commit: <hash corto de HEAD>
base: <base>
fecha: <AAAA-MM-DD>
sprints: [<N>, ...]
---

# Revisión de seguridad — Sprint <N> <Nombre>

Resumen: <n> altas, <n> medias, <n> bajas. <una línea de conclusión>

## Hallazgos

### [Alta] <título>
- Dónde: `ruta:línea`
- Problema: <qué y cómo se explota>
- Arreglo: <propuesta concreta>

## Dependencias
<salida resumida de dotnet/npm, o "sin vulnerabilidades conocidas" / "no se pudo ejecutar: …">

## Revisado sin hallazgos
<áreas del checklist que se miraron y están bien>
```

Si quedó código sin commitear, anótalo: el `commit:` es HEAD y la próxima revisión volverá a ver esos cambios si cambian.

## 7. Cierre

Muestra al usuario el resumen y los hallazgos Alta/Media con su ubicación, y pregunta si quiere que los corrija. No hagas commit del informe salvo que lo pida.
