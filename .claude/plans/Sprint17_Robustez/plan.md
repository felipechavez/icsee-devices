# Sprint 17 — Robustez

## Context
Dos huecos antes del Sprint 18 (integraciones, que suman más salidas a internet):

1. **Respaldos sin probar.** `scripts/backup-db.sh` genera `backups/camguard-*.sql.gz` y `keycloak-*.sql.gz`, pero nadie verifica que se puedan restaurar. Con `sh` sin `pipefail`, un `pg_dump` que falla deja igual un `.sql.gz` diminuto y el script dice "Respaldo creado". Si el cron deja de correr, nadie se entera.
2. **Sin internet no hay tope.** La grabación ya es local (FFmpeg → go2rtc) y `UploadService` reintenta sin fin, pero los clips en `Uploading` se acumulan en `{DataDirectory}/clips` sin límite hasta llenar el disco (en la Pi, el mismo de Postgres). Además: cada trabajo vencido reintenta por su cuenta (con R2 caído, N clips = N timeouts por ciclo); ntfy usa el timeout por defecto de `HttpClient` (100 s) y se llama en línea desde `UploadService`, así que un ntfy colgado frena las subidas; y un aviso que falla se pierde (movimiento Tuya, clip subido).

Decisiones tomadas con el usuario: borrado **por prioridad**, avisos en **bandeja con caducidad** y verificación del respaldo **visible en el panel de Estado**.

## 1. Verificación periódica del respaldo

### `scripts/verify-backup.sh` (nuevo, POSIX sh, se invoca con `sh`)
- Uso: `sh scripts/verify-backup.sh [camguard-xxx.sql.gz]`; sin argumento toma el `camguard-*.sql.gz` y el `keycloak-*.sql.gz` más nuevos de `backups/`.
- Pasos:
  1. `gzip -t` de ambos archivos.
  2. Postgres temporal: `docker run -d --rm --name camguard-verify-$$ --network none -e POSTGRES_PASSWORD=… postgres:17-alpine` (misma imagen que `db`; sin volumen ni red). `trap` que hace `docker rm -f` al salir. Espera `pg_isready` (tope 60 s).
  3. Crea los roles `camguard` y `keycloak` y las bases `camguard` y `keycloak` (el dump lleva `OWNER TO`).
  4. Restaura con `gunzip -c … | docker exec -i … psql -v ON_ERROR_STOP=1 -q`.
  5. Revisiones (`psql -tAc`): `__EFMigrationsHistory` con filas; `AppSettings` con 1 fila; `Cameras` ≥ 1; informa el conteo de `Recordings`. Compara la última migración con la de la base en vivo (`docker compose exec -T db …`): si difiere, **aviso** (hubo un deploy después del respaldo), no falla. Keycloak: `realm` con `name = 'camguard'` y al menos un `user_entity` de ese realm.
  6. Escribe `backups/last-verify.json` (temporal + `mv`): `{"verifiedAt":"<UTC ISO>","ok":true|false,"file":"camguard-….sql.gz","detail":"…"}` (en `detail`, comillas y saltos de línea saneados). Una línea en la salida estándar; código de salida ≠ 0 si falla.
- Cron recomendado (README): encadenar después del respaldo diario, `0 3 * * * cd … && sh scripts/backup-db.sh && sh scripts/verify-backup.sh >> backups/backup.log 2>&1`. Restaurar dura segundos con una base de este tamaño.
- `backup-db.sh`: corregir el comentario "Restaurar" (también Keycloak) y mencionar la verificación. Sin otros cambios: el fallo silencioso de `pg_dump` lo atrapa la verificación.

### Health lee el resultado
- `docker-compose.yml`, servicio `api`: volumen `./backups:/backups:ro` y `Health__BackupDirectory: /backups`. `scripts/deploy.sh` hace `mkdir -p backups` antes de `docker compose up` (si Docker crea la carpeta del bind mount, queda de root y `backup-db.sh` no puede escribir).
- `HealthOptions`: `BackupDirectory` (vacío = desactivado, como en desarrollo), `BackupMaxAgeHours = 36`, `BackupVerifyMaxAgeDays = 8`.
- `HealthMonitorService.ReadBackups()`: fecha de modificación del `camguard-*.sql.gz` más nuevo + `last-verify.json` deserializado (errores de E/S o JSON → `null` y log de depuración, como `ReadDisk`).
- `HealthEvaluator`: `HealthFacts` suma `BackupHealth? Backups` (`LatestAt`, `VerifiedAt`, `VerifyOk`, `VerifyDetail`). Problemas nuevos con clave estable:
  - `backup` (Critical): "No hay respaldo reciente": ninguno, o el último tiene más de `BackupMaxAgeHours`. También detecta que el cron dejó de correr.
  - `backup-verify` (Warning): "Falló la verificación del respaldo" (`ok = false`, con `detail`) o "El respaldo no se ha verificado" (nunca verificado, o hace más de `BackupVerifyMaxAgeDays`). El título no lleva números, para el "Resuelto: …".
- `SystemStatus` y `GET /api/status` suman `backups`; `StatusPage.tsx` agrega la fila "Respaldo: hace X h · verificado hace Y" con el estado de esas claves; `types.ts` va en espejo.

## 2. Modo sin internet

### 2a. Tope de disco local y orden de borrado (slice Upload)
- `AppSettings.LocalMaxGb` (por defecto 5), editable en Ajustes (`SettingsEndpoints` + validación 1–500 + formulario). Migración `AddLocalStorageCap`.
- `UploadOptions` (sección `Upload`): `MinFreeDiskMb = 512`, por debajo del aviso de Health (1024), así primero llega el aviso y después se borra.
- `RecordingStatus.Evicted` (nuevo, al final del enum: "se borró del equipo por falta de espacio antes de subirse"). `StorageKey` y `ThumbnailKey` quedan en null, como en `Discarded`.
- Lógica pura `LocalEvictionPolicy` (`Features/Upload/LocalEvictionPolicy.cs`):
  - `record EvictionCandidate(Guid Id, DateTimeOffset StartedAt, long Bytes, EvictionTier Tier)`.
  - `Tier(string? detection, bool hasPriorityEvent)`: `nada` → 0, sin analizar (`null`) → 1, animal → 2, `persona` → 3, timbre/alarma/`LockEvent` Tuya → 4 (gana a la etiqueta).
  - `Select(candidates, usedBytes, freeBytes, maxBytes, minFreeBytes)`: ordena por (tier, `StartedAt`) y toma hasta que `used ≤ max` **y** `free ≥ minFree`. Si no alcanza ni borrándolos todos, los devuelve todos (el aviso de disco de Health ya está abierto).
- Solo son candidatos los `Uploading`: los `Recording` están abiertos por FFmpeg y los `Analyzing` pasan a `Uploading` en segundos (la IA no depende de internet).
- Se ejecuta **dentro del ciclo de `UploadService`**, a lo más una vez por minuto y antes de tomar trabajos: es el único hilo que toca la cola, así que no compite con una subida en curso. Pasos: suma el tamaño de `{DataDirectory}/clips` y lee `DriveInfo`; si se pasa, carga los `Uploading` (con la consulta de evento prioritario que hoy está en `DetectionService.HasPriorityEventAsync`, que se mueve a un helper compartido en Detection usando `DetectionPolicy.PriorityEventLookback`), aplica `Select`, borra el archivo, la miniatura y el JSON de la cola (`UploadQueue.Remove`), y marca `Evicted`. Un `LogWarning` por clip y `RecordingCompleted` por SignalR para refrescar la lista.
- `RetentionService`: incluir `Evicted` en los estados que borra (hoy `Uploaded | Failed | Discarded`).
- Frontend: `RecordingStatus` suma `'Evicted'`, etiqueta "Borrado sin subir (sin espacio)" en `RecordingCard` y opción en el filtro de `RecordingsPage` junto a fallidos/descartados.

### 2b. Subidas: una pausa compartida en vez de N timeouts
- En `UploadService`, tras un fallo se sale del lote y se guarda `_pausedUntil = now + RetryPolicy.Delay(intentos)` (el del trabajo que falló); mientras dure la pausa no se toma ningún trabajo. El primer éxito la limpia. Cada trabajo conserva su `Attempts`, así el backoff sigue creciendo (5 s → 10 min).
- La regla queda pura: `RetryPolicy.Delay` ya existe; la decisión "¿proceso el lote?" es una comparación simple.

### 2c. ntfy: bandeja con caducidad (Common/Notifications)
- `RetryPolicy` pasa de `Features/Upload/UploadQueue.cs` a `Common/RetryPolicy.cs` (la usan Upload y la bandeja).
- `NtfyNotifier`: `HttpClient.Timeout = 10 s` en el registro (`AddHttpClient<NtfyNotifier>`).
- `NotificationOutbox`: un JSON por aviso en `{DataDirectory}/notify-queue` (`{CreatedAt, Notification, Attempts, NextAttemptAt}`), escrito con temporal + rename como `UploadQueue`. Tope de 500 archivos: si se pasa, se descartan los más viejos.
- `OutboxNotifier : INotifier` (el que se inyecta en todos lados): si la bandeja está vacía intenta enviar al instante; si falla, o si ya hay avisos esperando (para no desordenarlos), lo guarda y vuelve **sin lanzar error**. `UploadService`, `TuyaEventService` y `HealthMonitorService` no cambian: Health marcará `LastNotifiedAt` y el aviso saldrá al volver ntfy.
- `NotificationOutboxService` (BackgroundService registrado en `Program.cs`): envía en orden de creación con backoff; uno de más de `Ntfy:MaxDelayHours` (6) se descarta con log. Lógica pura `DelayedNotification.Prepare(n, createdAt, now, maxDelay)` → `null` si caducó; si se retrasó más de 2 min, antepone al mensaje "Retrasado (hh:mm) · " (hora local de `createdAt`, `InvariantCulture`). Prioridad, imagen y enlace se mantienen (la miniatura prefirmada dura 24 h).
- `NotifierHealth` (como `UploadHealth`): Health lo muestra como servicio `ntfy` en el panel, **sin abrir problema** (avisar por ntfy que ntfy no funciona solo produce un "Problema/Resuelto" atrasado).

### Lo que ya funciona sin internet (solo se documenta)
"Sin internet" quiere decir que el router perdió la salida a internet (o que R2/ntfy están caídos), no que se cayó el Wi-Fi: las iCSee siguen conectadas al router y la Pi les habla por la red local (`IP:34567`, sin pasar por internet). Si se corta el router o la luz, no graba nada; eso queda fuera de alcance (UPS para router y Pi).
Grabación iCSee, IA y vivo por la red local; Keycloak valida con JWKS interno. Tuya depende de la nube: sin internet sus dispositivos no avisan ni graban (ya aparece como "Sin conexión con Tuya"). Los clips en espera no se pueden reproducir desde la web (se ven por URL prefirmada de R2): queda fuera de alcance.

## Archivos
- Nuevos: `scripts/verify-backup.sh`, `Features/Upload/LocalEvictionPolicy.cs`, `Features/Upload/UploadOptions.cs`, `Common/RetryPolicy.cs`, `Common/Notifications/NotificationOutbox.cs` (bandeja, `OutboxNotifier`, servicio, `DelayedNotification`, `NotifierHealth`), migración `AddLocalStorageCap`.
- Cambios: `scripts/backup-db.sh` (comentarios), `scripts/deploy.sh` (`mkdir -p backups`), `docker-compose.yml`, `Program.cs`, `Common/Data/Entities.cs`, `Features/Upload/UploadService.cs` y `UploadQueue.cs`, `Features/Detection/DetectionService.cs` (helper de evento prioritario), `Features/Retention/RetentionService.cs`, `Features/Health/{HealthOptions,HealthEvaluator,HealthMonitorService}.cs`, `Features/Settings/SettingsEndpoints.cs`, `appsettings.json` (secciones `Upload`, `Ntfy:MaxDelayHours`, `Health:Backup*`), frontend `api/types.ts`, `features/status/StatusPage.tsx`, `features/recordings/{RecordingCard,RecordingsPage}.tsx`, formulario de Ajustes. `README.md` (cron, verificación, modo sin internet) y `CLAUDE.md` (Upload: tope y orden de borrado; Notifications: bandeja; Health: respaldos).

## Pruebas (TUnit, carpetas espejo)
- `LocalEvictionPolicyTests`: orden por tier y antigüedad; se detiene al cumplir ambos límites; el timbre gana a `nada`; nada que borrar si está bajo el tope; si no alcanza, devuelve todos.
- `DelayedNotificationTests`: caducado → null; sin prefijo bajo 2 min; prefijo con hora en cultura invariante.
- `NotificationOutboxTests` (carpeta temporal): orden de creación, tope de 500 descartando los viejos, escritura atómica.
- `HealthEvaluatorTests`: `backup` sin respaldo y con uno viejo; `backup-verify` fallido, viejo y sano; desactivado si `Backups` es null.
- `RetryPolicy`: mover las pruebas existentes al nuevo namespace.

## Verificación
1. `cd backend && dotnet build && dotnet test`; `cd frontend && npm run build && npm run lint`.
2. Respaldo: `sh -n scripts/verify-backup.sh`; en el equipo con Docker, `sh scripts/backup-db.sh && sh scripts/verify-backup.sh` → OK y `backups/last-verify.json` con `ok: true`. Con un `.sql.gz` truncado (`head -c 1000`) debe fallar con `ok: false`, y en el panel aparecer "Falló la verificación del respaldo". Al cambiar la fecha del último respaldo (`touch -d '3 days ago'`), "No hay respaldo reciente".
3. Sin internet: `Storage__Endpoint` y `Ntfy__Url` apuntando a un host inalcanzable (o cortar la WAN). Despertar la cámara varias veces: los clips quedan en `Uploading`, el log muestra un solo fallo por ciclo de pausa (no N), los avisos se acumulan en `notify-queue`. Con `LocalMaxGb` en 1 (o `MinFreeDiskMb` alto), se borran primero los `nada` más antiguos, quedan en `Evicted` y la web lo muestra. Al volver la red se suben los que quedan y llegan los avisos "Retrasado (hh:mm) · …"; los de más de 6 h no.
