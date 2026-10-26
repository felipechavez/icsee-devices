# Sprint 6 — Deteccion IA

> Al aprobarse se mueve a `.claude/plans/Sprint6_DeteccionIA/plan.md`.

## Contexto

Las iCSee a batería despiertan por PIR y graban cualquier cambio de calor: sol, viento, autos, sombras. La cerradura Tuya graba con el PIR asociado. Resultado: muchos clips vacíos que se suben a R2, gastan cuota y generan avisos ntfy inútiles. El usuario quiere que **solo se guarden los clips con personas o animales**.

Decisiones del usuario:
- **Motor local**: detector COCO en ONNX dentro de la API, sin nube ni costo por clip.
- **Hardware de producción: Raspberry Pi (ARM64)**. El modelo debe ser pequeño, se analizan pocos cuadros por clip, un análisis a la vez, y la imagen debe compilar para `linux/arm64`.

## Fase 0 — De SQL Server a Postgres (requisito para la Pi)

`mcr.microsoft.com/mssql/server` no tiene imagen ARM64, así que CamGuard no arranca en la Raspberry Pi. Se cambia a **PostgreSQL 17**, que es multi-arquitectura y ya se usa para Keycloak. Decisión del usuario: **migrar los datos existentes**, no empezar de cero.

### Backend
- `CamGuard.Api.csproj`: `Npgsql.EntityFrameworkCore.PostgreSQL` en lugar de `Microsoft.EntityFrameworkCore.SqlServer`. `Program.cs`: `UseNpgsql(...)`.
- `AppDbContext`: se quita `HasFilter("[TuyaDeviceId] IS NOT NULL")`, que es sintaxis de SQL Server. En Postgres un índice único ya admite varios `NULL`.
- **Migraciones**: se borran las de SQL Server y se genera una sola `InitialCreate` para Postgres. Las bases existentes se pasan con la herramienta de abajo, no con migraciones.
- `timestamptz` en Npgsql solo acepta `DateTimeOffset` en UTC. Lo que se guarda ya sale de `TimeProvider.GetUtcNow()` o de epoch, que vienen en UTC, pero `from`/`to` de `ListRecordings` y `RecordingTimeline` vienen del cliente y se pasan a UTC (`ToUniversalTime()`) antes de consultar.
- Las comparaciones de texto en Postgres distinguen mayúsculas. Afecta solo la unicidad del stream y del Device ID, que en go2rtc y Tuya también las distinguen, así que no cambia nada.
- `DatabaseInitializer`: se actualizan los comentarios (Postgres en vez de SQL Server); los reintentos siguen igual.

### Docker y scripts
- `docker-compose.yml`: el servicio `sqlserver` se reemplaza por **`db`** (`postgres:17-alpine`), una instancia propia de CamGuard separada de `keycloak-db`, con volumen `pgdata`, healthcheck `pg_isready` y `DB_PASSWORD`. El puerto queda publicado solo en `127.0.0.1:${DB_DEV_PORT:-5433}`, para desarrollo y para la migración.
  - Conexión de la API: `Host=db;Database=camguard;Username=camguard;Password=${DB_PASSWORD}`.
- **SQL Server legado** bajo el perfil `legacy-sqlserver`: el mismo servicio y volumen `sqldata` de antes, en `127.0.0.1:14330`. No arranca salvo con `--profile legacy-sqlserver` y se quita en un sprint posterior, una vez migrado.
- `scripts/backup-db.sh`: `pg_dump` de `camguard` (`camguard-*.sql.gz`) en lugar del `.bak`.
- `.env.example` y `scripts/up.ps1`: `DB_PASSWORD` en lugar de `SQL_PASSWORD`.
- `README.md`: el requisito "x86 (SQL Server no corre en ARM)" pasa a "x86 o ARM64 (Raspberry Pi 4/5)". Se suman la base local de desarrollo y los pasos de migración.
- `appsettings.Development.json`: conexión a `localhost:5433`.

### Migración de datos — `backend/tools/CamGuard.DbMigrator` (consola, una sola vez)
- Enlaza (`<Compile Include … Link>`) `AppDbContext`, `Entities.cs` y las migraciones de la API, sin referenciar el proyecto web, y suma `Microsoft.EntityFrameworkCore.SqlServer` **solo en la herramienta**. Abre un contexto contra SQL Server (origen) y otro contra Postgres (destino).
- Pasos:
  1. Aplica `MigrateAsync` en el destino.
  2. Aborta si el destino ya tiene cámaras o grabaciones; `--force` vacía el destino primero.
  3. Copia `Cameras`, `Recordings`, `CameraEvents` y `Settings` conservando los Ids, en lotes, con las fechas en UTC. `Settings` se actualiza, porque la migración ya siembra la fila.
  4. Ajusta las secuencias de identidad con `setval(pg_get_serial_sequence(...), max(Id))`.
  5. Muestra conteos de origen y destino y falla si no coinciden.
- Las contraseñas DVRIP cifradas se copian tal cual: las llaves de Data Protection siguen en el volumen `apidata` (`/data/keys`), que no cambia.
- Uso (en el equipo que tiene la SQL Server actual):
  ```sh
  docker compose stop api
  docker compose --profile legacy-sqlserver up -d sqlserver db
  dotnet run --project tools/CamGuard.DbMigrator -- \
    --from "Server=localhost,14330;Database=CamGuard;User Id=sa;Password=…;TrustServerCertificate=True" \
    --to   "Host=localhost;Port=5433;Database=camguard;Username=camguard;Password=…"
  docker compose up -d --build api
  ```
  Para mudarse a la Pi: `pg_dump` en el equipo actual, `psql` en la Pi, y copiar también el volumen `apidata`, que tiene las llaves y la cola de subida.

### Verificación de la Fase 0
- `dotnet build && dotnet test`. La nueva migración genera un esquema válido: la API arranca contra el `db` vacío y siembra la cámara.
- Migración real con la SQL Server actual: los conteos coinciden; en la web se ven las cámaras (y "Probar conexión" funciona, lo que prueba que las contraseñas se descifran), el historial de clips se reproduce desde R2 y se puede crear una cámara nueva sin chocar de Id.
- `docker buildx build --platform linux/arm64 ./backend` compila.

## Enfoque

```
RecordingService ─► clip MP4 + miniatura ─► DetectionQueue ─► DetectionService
                                                               │  FFmpeg: 1 cuadro/s, 416×416 RGB por stdout
                                                               │  ONNX (YOLOX-nano) ─► detecciones por cuadro
                                                               │  DetectionPolicy (pura) ─► Keep / Discard
                                                               ├─ Keep    ─► Uploading ─► UploadQueue (igual que hoy)
                                                               └─ Discard ─► borra archivos, Status = Discarded
```

- **Se analiza después de grabar**, no en vivo. El PIR sigue despertando y FFmpeg sigue grabando igual que hoy. Así, en la Pi, la IA nunca compite con la grabación.
- **Modo por cámara**, para no perder eventos reales mientras se calibra:
  - `Off`: sin análisis.
  - `Tag` (**por defecto**): analiza y etiqueta, pero sube todo.
  - `Filter`: descarta lo que no tenga personas ni animales.
- **Ante cualquier fallo, se sube el clip** (fail-open): modelo ausente, FFmpeg con error o excepción de ONNX. El clip queda con `Detection = null` y se registra un warning.
- **Timbre y alarmas Tuya nunca se descartan.** Si hubo un `CameraEvent` `Doorbell`/`Alarm`/`LockEvent` de esa cámara durante la grabación, el clip se conserva sin importar lo que diga el detector.
- **Modelo: YOLOX-nano** (Megvii, **Apache-2.0**; Ultralytics es AGPL y se evita), entrada 416×416, 80 clases COCO:
  - `person` = 0;
  - animales = 14–23 (bird, cat, dog, horse, sheep, cow, elephant, bear, zebra, giraffe).
  - Tarda ~150–400 ms por cuadro en una Pi 4/5. Con un tope de ~20 cuadros por clip y corte en el primer positivo, son unos pocos segundos por clip.
  - `Detection:ModelPath` permite cambiar a `yolox_tiny.onnx`, más preciso y más lento.

## Backend

### Modelo (`Common/Data/Entities.cs`, `AppDbContext.cs`) — migración `AddDetection`
- `enum DetectionMode { Off, Tag, Filter }` (string). En `Camera`:
  - `DetectionMode` (por defecto `Tag`);
  - `DetectPeople` (bool, `true`);
  - `DetectAnimals` (bool, `true`).
- `RecordingStatus` suma `Analyzing` (entre `Recording` y `Uploading`) y `Discarded`.
- En `Recording`:
  - `Detection` (string?, máx. 50): etiqueta legible, p. ej. `"persona"`, `"perro"` o `"nada"`; null = sin analizar.
  - `DetectionScore` (double?).
- `AppSettings.DetectionMinConfidence` (double, 0.5). Se edita en Ajustes, igual que `CooldownSeconds`.

### Nuevo slice `Features/Detection/` (`AddDetectionFeature`)
- `DetectionOptions` (`Detection:`):
  - `ModelPath` (`/app/models/yolox_nano.onnx`), `InputSize` (416);
  - `FramesPerSecond` (1), `MaxFrames` (20), `IntraOpThreads` (2: deja núcleos libres a FFmpeg en la Pi).
- `CocoClasses` (estático): nombres en español de las clases que interesan y `IsAnimal(classId)`.
- `YoloxDecoder` (puro, `static`): tensor `[1, N, 85]` → `Detection(ClassId, Score)`.
  - Usa grillas con strides 8/16/32 y `score = obj × cls`.
  - No necesita NMS: solo importa si algún objeto supera el umbral, no las cajas.
- `FrameSampler`: FFmpeg emite cuadros RGB crudos por stdout, sin librería de imágenes.
  - Comando: `-i clip -vf fps=1,scale=416:416:force_original_aspect_ratio=decrease,pad=416:416:-1:-1:color=0x727272 -frames:v 20 -f rawvideo -pix_fmt bgr24 pipe:1` (relleno 114 y BGR, como entrena YOLOX).
  - FFmpeg aplica la rotación del metadato al decodificar, así que los cuadros llegan derechos.
  - Los argumentos van en un método estático `SampleArguments(...)` para probarlos, igual que `FfmpegRecorder.RecordArguments`.
  - Matar este FFmpeg al cortar es seguro: solo lee.
- `IObjectDetector` / `OnnxObjectDetector` (singleton):
  - Usa `Microsoft.ML.OnnxRuntime`, que trae nativos para linux-x64 y linux-arm64.
  - Carga perezosa del modelo. Si el archivo no existe, `IsAvailable = false` y se avisa una vez en el log.
  - Convierte el cuadro BGR a tensor CHW float.
- `DetectionPolicy` (pura):
  - `Evaluate(IEnumerable<Detection>, people, animals, minConfidence) → DetectionVerdict(Label, Score, Found)`. Si aparecen ambos, gana la persona.
  - `Decide(mode, verdict?, hasPriorityEvent) → Keep | Discard`:
    - `Off` o `Tag` → Keep;
    - `Filter` con verdict null (falló) → Keep;
    - `Filter` con evento prioritario → Keep;
    - `Filter` sin hallazgo → Discard.
- `DetectionQueue`: `Channel<Guid>` sin límite. El estado persistente es `Status = Analyzing` en la BD, así que no hace falta otra cola en disco.
- `DetectionService` (`BackgroundService`, **un clip a la vez**):
  - Al arrancar, reencola las grabaciones en `Analyzing` cuyos archivos siguen en `{DataDirectory}/clips`.
  - Por cada clip:
    1. muestrea cuadros y corta en el primer positivo;
    2. aplica `Evaluate` y luego `Decide`;
    3. Keep: guarda `Detection` y `DetectionScore`, `Status = Uploading` y `UploadQueue.EnqueueAsync(...)`, el mismo `UploadJob` de hoy;
    4. Discard: borra MP4 y JPG, `Status = Discarded` y emite `RecordingCompleted`, que refresca la lista.
  - Log: `"Clip {Id} de {Camera}: persona (0.82) → se sube"` / `"… nada → descartado"`.

### Cambios en slices existentes
- **Capture** (`Features/Capture/RecordingService.cs:186-189`): en vez de `Uploading` + `UploadQueue`, deja `Status = Analyzing` y llama `DetectionQueue.Enqueue(recordingId)`. La lógica de rutas locales sale de `LocalPaths` y queda compartida con Detection (p. ej. `ClipPaths` estático en `Common/`).
- **Upload** (`UploadService.NotifyAsync`): el título usa la detección:
  - `"Persona: Entrada"` / `"Animal (perro): Patio"`;
  - sin detección queda `"Movimiento: …"`.
- **Tuya** (`TuyaEventService.HandleSignalAsync`): si la cámara está en `Filter`, el aviso **inmediato** de `Motion` se omite y queda solo el del clip confirmado. Timbre, alarmas y aperturas siguen avisando al instante.
- **Cameras** (`CameraInput`, `ListCameras.CameraResponse`): suman `DetectionMode`, `DetectPeople` y `DetectAnimals`. Validación: `Enum.IsDefined`, y en `Filter` al menos una clase activa.
- **Recordings**:
  - `ListRecordings` excluye `Discarded`, salvo que el filtro `status=discarded` lo pida, y devuelve `Detection`.
  - `RecordingTimeline` y `ListCameras` no cambian, porque ya filtran por `Uploaded`.
  - `DeleteRecording` bloquea también `Analyzing`.
- **Retention** (`RetentionService.cs:15`): borra también las filas `Discarded` vencidas (sin archivo remoto).
- **Settings**: `DetectionMinConfidence` en `UpdateSettings` (validación 0.2–0.95).
- `Program.cs`: `AddDetectionFeature(builder.Configuration)`.

### Docker
- `backend/Dockerfile`:
  - descarga el modelo en la etapa de build con `ADD --checksum=sha256:<hash>`, desde `https://github.com/Megvii-BaseDetection/YOLOX/releases/download/0.1.1rc0/yolox_nano.onnx`, a `/app/models/` (el hash se fija al implementar);
  - la imagen debe compilar en `linux/arm64`. `aspnet:10.0` y `sdk:10.0` son multi-arquitectura, y ffmpeg de Debian también.
- `docker-compose.yml`: nada obligatorio. `Detection__ModelPath` se documenta en `.env.example` como opcional.
- La falta de imagen ARM64 de SQL Server se resuelve en la **Fase 0** (Postgres).

## Frontend
- `src/api/types.ts`:
  - `DetectionMode`;
  - campos nuevos en `Camera`, `Recording` y `Settings`;
  - `RecordingStatus` con `Analyzing` y `Discarded`.
- `features/settings/CameraPanel.tsx` + `cameraDraft.ts`: bloque "Detección IA" con:
  - un selector *Desactivada / Solo etiquetar / Filtrar*;
  - casillas *Personas* y *Animales*, deshabilitadas en `Off`;
  - un texto de ayuda: "Filtrar descarta los clips sin personas ni animales; timbre y alarmas siempre se guardan".
- `features/settings/SettingsPage.tsx`: deslizador "Confianza mínima de la IA" (50 %).
- `features/recordings/RecordingCard.tsx`:
  - insignia con la etiqueta (`persona`, `perro`…), y en `nada` una insignia apagada, útil en el modo `Tag` para calibrar;
  - estado `Analyzing` → "Analizando…".
- `features/recordings/RecordingsPage.tsx`: el filtro suma *Descartados* (`status=discarded`). Muestra hora y cámara para revisar si se perdió algo.
- Estilos en `src/index.css` (clases planas, tokens existentes).

## Pruebas (TUnit, `tests/CamGuard.Tests/Features/Detection/`)
- `YoloxDecoderTests`: un tensor sintético con un objeto en una celda conocida devuelve la clase y el score esperados, y un objeto bajo el umbral se ignora.
- `DetectionPolicyTests`:
  - `Evaluate`: persona y perro juntos → persona; animales desactivados → perro ignorado; bajo el umbral → nada.
  - `Decide`: todas las combinaciones de modo, falla y evento prioritario.
- `FrameSamplerTests`: `SampleArguments` (fps, tamaño, `-frames:v`, `InvariantCulture`).
- `CameraInputTests`: `Filter` sin clases activas → error.
- `RecordingsTests`: `Discarded` no aparece por defecto y sí con el filtro.
- Una prueba de integración opcional (`[Category("Model")]`, se salta si falta el modelo) con un JPG de una persona → `persona`.

## Fases
0. **SQL Server → Postgres** (ver la sección de arriba), con migración de datos. Va antes de todo porque sin ella no se puede desplegar en la Pi.
1. **Backend del slice + modo Tag**: modelo, migración, Detection, cambio en Capture, título del aviso y pruebas. Desplegar con todas las cámaras en `Tag` unos días y revisar las etiquetas, sobre todo de noche (IR).
2. **Filter + UI**: selector por cámara, filtro *Descartados*, retención y aviso Tuya. Activar `Filter` en las cámaras ya calibradas.
3. *(Opcional, otro sprint)* **Corte temprano para ahorrar batería**: analizar en paralelo desde `rtsp://go2rtc` y, si a los ~10 s no hay nadie, cerrar el clip con `q` para que la cámara se duerma antes. Otra opción es clasificar la foto de `movement_detect_pic` de Tuya antes de despertar el stream, lo que requiere descargar la imagen cifrada de Tuya.

## Verificación
- `cd backend && dotnet build && dotnet test`.
- `cd frontend && npm run build && npm run lint`.
- Local en Windows: `docker compose -f docker-compose.yml -f docker-compose.windows.yml up -d --build`.
  1. Activar una iCSee y pasar frente a ella → log `persona (0.xx) → se sube`, aviso ntfy "Persona: …" e insignia en la tarjeta.
  2. Activarla tapando el lente o con una mano rápida → `nada`. En `Tag` se sube con la insignia apagada; en `Filter` aparece en *Descartados* y no llega a R2 (verificar en el bucket).
  3. Tocar el timbre de la cerradura con `Filter` y sin nadie → el clip se conserva.
  4. Borrar el modelo del contenedor → el log avisa y los clips se suben igual (fail-open).
  5. Reiniciar la API con un clip en `Analyzing` → se reanaliza al arrancar.
- Pi: `docker buildx build --platform linux/arm64 ./backend` compila. Medir en el log el tiempo por clip, que debería ser de pocos segundos.
- Actualizar `CLAUDE.md`:
  - el slice Detection y el flujo Capture → Detection → Upload;
  - "Flujo de una activación";
  - el detalle fail-open.
