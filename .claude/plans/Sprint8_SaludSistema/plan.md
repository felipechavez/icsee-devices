# Salud del sistema

## Contexto

Hoy CamGuard solo avisa cuando hay movimiento. Si una cámara a batería se agota, la cola de subida se atasca (R2 caído o con credenciales vencidas), el Message Service de Tuya se cae o el disco local se llena, nadie se entera hasta que falta un clip. Este sprint agrega:

1. Un **panel "Estado"** en la web con la batería de cada cámara (historial y días restantes estimados), cuándo despertó por última vez, cuántas activaciones tiene por día y el estado de go2rtc, Tuya, R2, la cola y el disco.
2. **Avisos de problemas por ntfy**: se manda uno al abrirse cada problema, se repite si persiste y se manda otro al resolverse.

El estado ya existe en partes: `Camera.BatteryPercent`/`DeviceInfoAt` (los escriben `DeviceInfoService` por DVRIP y `TuyaEventService.UpdateBatteryAsync`), los `CameraEvent` `Awake`, `Recording.Status`/`SizeBytes` y `UploadQueue`. Falta juntarlo, guardar el historial de batería y evaluarlo.

## Diseño: slice `Features/Health/`

Un `HealthMonitorService` (BackgroundService) corre cada `Health:CheckMinutes` (5) y también al minuto de arrancar. En cada pasada:
1. reúne los datos (snapshot),
2. guarda las lecturas de batería nuevas,
3. evalúa los problemas con lógica pura,
4. decide qué avisar con lógica pura,
5. guarda el snapshot en memoria (`HealthSnapshotStore`) y emite `HealthUpdated` por SignalR.

El endpoint devuelve el último snapshot y no consulta nada en vivo.

### Datos que reúne (sin tocar los slices que los producen, salvo Tuya)

- **Batería**: lee `Camera.BatteryPercent` y `DeviceInfoAt`. Si `DeviceInfoAt` es más nuevo que la última lectura guardada, inserta un `BatteryReading`. Así no se modifican `DeviceInfoService` ni `TuyaEventService`.
- **Última activación y activaciones por día**: salen de los `CameraEvent` `Awake`: el máximo por cámara y el conteo de los últimos 7 días dividido por 7.
- **Cola**: `Recording` con `Status` `Uploading` (cuántos hay y el `StartedAt` más viejo) y `Analyzing` (cuántos hay). No hace falta cambiar `UploadQueue`.
- **R2**: suma de `SizeBytes` de los `Uploaded`, más el costo mensual estimado con `Health:R2PricePerGbMonth` (0.015) y `Health:R2FreeGb` (10). También el último resultado de subida: `UploadService` lo informa a un `UploadHealth` singleton (ok/error y hora), que es un cambio mínimo.
- **Disco**: `DriveInfo` de `App:DataDirectory` (espacio libre y total).
- **go2rtc**: `GET api` con un `HttpClient` configurado por `Go2rtcOptions.ConfigureHttpClient` ([AppOptions.cs](backend/src/CamGuard.Api/Common/AppOptions.cs)), con timeout corto.
- **Tuya**: nuevo `TuyaConnectionState` singleton en `Features/Tuya/`. `ListenAsync` lo marca conectado al abrir el WebSocket y `ListenLoopAsync` lo marca caído con el mensaje de error. Si Tuya no está configurado (`TuyaOptions.IsConfigured`), se omite. Health lo lee igual que Cameras lee `CameraStateStore`.

### Lógica pura (probada con TUnit)

- **`BatteryForecast.DaysLeft(readings, now)`**: toma solo el tramo de descarga desde la última carga (una subida de ≥ 10 puntos marca una carga). Calcula una regresión lineal de % contra tiempo y devuelve los días hasta 0 %. Devuelve `null` si el tramo cubre menos de 24 h, tiene menos de 3 lecturas o la pendiente es ≥ 0.
- **`BatteryHistory.ShouldRecord(last, percent, at)`**: guarda la lectura si cambió el % o si pasaron ≥ 6 h desde la anterior. Así el gráfico tiene puntos aunque no cambie.
- **`HealthEvaluator.Evaluate(snapshot, settings, openKeys, now)`** devuelve una lista de `HealthIssue(Key, Severity, Title, Detail, CameraId?, Since)`. Claves estables:
  - `battery:{id}`: batería < `LowBatteryPercent` (20). Se cierra recién con ≥ umbral + 5 (histéresis con `openKeys`). Si quedan ≤ 3 días según el pronóstico, también se abre aunque la batería siga sobre el umbral.
  - `silent:{id}`: cámara habilitada sin `Awake` en `SilentCameraHours` (48). Para una cámara sin eventos nunca, se cuenta desde que arrancó la API.
  - `queue`: `Uploading` más viejo que `Health:UploadStuckMinutes` (30).
  - `upload`: la última subida falló hace más de 10 min y no hubo un éxito después.
  - `disk`: espacio libre < `Health:MinFreeDiskMb` (1024).
  - `go2rtc` / `tuya`: caídos por más de `Health:ServiceGraceMinutes` (10). El monitor recuerda en memoria desde cuándo falla go2rtc; para Tuya lo da `TuyaConnectionState`.
- **`HealthAlertPolicy.Decide(issues, openAlerts, now, repeatHours)`** devuelve `(ToOpen, ToRepeat, ToResolve)`:
  - abre lo nuevo,
  - repite lo que lleva más de `Health:RepeatHours` (24) sin aviso,
  - resuelve lo que estaba abierto y ya no aparece.

### Persistencia (migración `AddHealth`)

- `BatteryReading { long Id, int CameraId, int Percent, DateTimeOffset At }`, con índice `(CameraId, At)`. Se borra en cascada con la cámara y se poda a 90 días en la misma pasada diaria del monitor. No depende de `RetentionDays`: el pronóstico necesita historia aunque la retención de clips sea corta.
- `HealthAlert { string Key (PK), DateTimeOffset OpenedAt, DateTimeOffset LastNotifiedAt }`: los avisos abiertos. Persisten para que un reinicio de la API no repita todos los avisos. Al resolverse, la fila se borra.
- `AppSettings` suma `HealthAlertsEnabled` (true), `LowBatteryPercent` (20) y `SilentCameraHours` (48), que se editan en Ajustes. Los valores operativos (`MinFreeDiskMb`, `UploadStuckMinutes`, `ServiceGraceMinutes`, `RepeatHours`, `CheckMinutes`, precio de R2) van en `HealthOptions` (`Health:*` en appsettings).

### Avisos

- `Notification` ([Notifier.cs](backend/src/CamGuard.Api/Common/Notifications/Notifier.cs)) suma `int? Priority`, que `NtfyNotifier` manda como `priority`.
- Al abrir o repetir: título `"Problema: Batería baja en Entrada (15 %)"`, prioridad 4, tag `warning` y enlace a `/status`.
- Al resolver: `"Resuelto: …"`, prioridad 2, tag `white_check_mark`.
- Con `HealthAlertsEnabled = false` no se envía nada, pero el panel sigue mostrando los problemas.
- Si ntfy falla: se registra un warning y no se actualiza `LastNotifiedAt`, así se reintenta en la próxima pasada.

### API

- `GET /api/status` (`GetSystemStatus.Endpoint`) devuelve el snapshot:
  - `checkedAt` e `issues[]`;
  - `cameras[]`: `id`, `name`, `kind`, `enabled`, `batteryPercent`, `batteryDaysLeft`, `batteryHistory` de 14 días (`at`, `percent`), `lastAwakeAt`, `activationsPerDay`;
  - `services[]`: `name`, `ok`, `since`, `detail`;
  - `uploads`: `pending`, `analyzing`, `oldestPendingAt`;
  - `disk` y `storage`: `usedBytes`, `clips` y `estimatedMonthlyUsd`.
- No se usa `/api/health` para no confundirlo con `/health`, el health check anónimo de Docker.
- SignalR: `IEventsClient.HealthUpdated(int issueCount)` se emite en cada pasada en que cambie el conjunto de problemas.
- `SettingsDto` y `UpdateSettings.Validate`: los 3 campos nuevos, con `LowBatteryPercent` entre 5 y 50 y `SilentCameraHours` entre 6 y 336.
- `AddHealthFeature(config)` en [Program.cs](backend/src/CamGuard.Api/Program.cs).

## Frontend

- **Nueva ruta `/status` ("Estado")** en [Layout.tsx](frontend/src/components/Layout.tsx) y [App.tsx](frontend/src/App.tsx). La pestaña muestra un punto o contador con la cantidad de problemas.
- `src/features/status/`:
  - `useSystemStatus` (TanStack Query `['status']`, refresco cada 60 s; `EventsHubProvider` lo invalida con `HealthUpdated`);
  - `StatusPage` con:
    - **Problemas activos** arriba, con severidad, desde cuándo y enlace a la cámara. Si no hay: "Todo en orden".
    - **Cámaras**: una tarjeta por cámara con la batería (reutilizar [BatteryBadge.tsx](frontend/src/components/BatteryBadge.tsx)), un `BatterySparkline` de 14 días en SVG propio, sin librería, con los tokens de color de `index.css`, "Quedan ~9 días", "Despertó hace 3 h" y "~12 activaciones/día".
    - **Servicios**: go2rtc, Tuya, R2 (última subida), cola de subida y disco, cada uno con un indicador ok/error y su detalle. Uso de R2 en GB y costo estimado al mes.
- `api/types.ts`: los tipos `SystemStatus` y relacionados, más los 3 campos nuevos de `Settings`.
- [SettingsPage.tsx](frontend/src/features/settings/SettingsPage.tsx): sección "Alertas del sistema" con un switch de avisos y steppers para el umbral de batería y las horas sin despertar (reutilizar `Switch` y `Stepper`).

## Pruebas (TUnit, `tests/CamGuard.Tests/Features/Health/`)

- `BatteryForecastTests`:
  - descarga lineal → días correctos;
  - una carga a mitad de la serie corta el tramo;
  - menos de 24 h o menos de 3 lecturas → `null`;
  - pendiente plana o positiva → `null`.
- `BatteryHistoryTests`: cambio de % → guarda; mismo % antes de 6 h → no guarda; mismo % después de 6 h → guarda.
- `HealthEvaluatorTests`:
  - batería bajo el umbral → abre; batería entre el umbral y el umbral + 5 con el problema ya abierto → se mantiene;
  - pronóstico ≤ 3 días → abre;
  - cámara deshabilitada → no hay `silent`;
  - go2rtc caído menos que la gracia → nada; más que la gracia → abre;
  - Tuya sin configurar → nada.
- `HealthAlertPolicyTests`: abrir, repetir a las 24 h, no repetir antes y resolver.
- `UpdateSettings.Validate` con los nuevos rangos, en los tests de Settings existentes.

## Documentación

En CLAUDE.md, un bullet de backend para `Health`: qué reúne, la lógica pura, `HealthAlert` y que no despierta cámaras. En el flujo entre slices: Upload → Health por `UploadHealth` y Tuya → Health por `TuyaConnectionState`.

## Verificación

1. `cd backend && dotnet build && dotnet test`; `cd frontend && npm run build && npm run lint`.
2. Levantar con `docker compose -f docker-compose.yml -f docker-compose.windows.yml up -d --build`, abrir `/status` y revisar que aparezcan las cámaras con batería y los servicios en verde.
3. Provocar problemas:
   - `docker compose stop go2rtc` → a los ~10 min llega por ntfy "Problema: go2rtc no responde"; `start` → llega "Resuelto".
   - Subir `LowBatteryPercent` en Ajustes por encima de la batería actual → aviso en la próxima pasada; bajarlo → "Resuelto".
   - Poner una credencial inválida de R2 en `.env` y provocar un clip → a los 10 min llega el aviso `upload`; a los 30 min, el de `queue`.
4. Reiniciar la API con un problema abierto → no se repite el aviso (lo impide `HealthAlert` en la BD).
5. Tras un par de días de uso, comprobar que el sparkline tenga puntos y aparezca "Quedan ~N días".
