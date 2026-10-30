# Retención: barrido de huérfanos en R2

## Contexto

Los clips en R2 deben durar como máximo la retención (30 días por defecto, editable en Ajustes) y borrarse solos. Esto **ya existe** en parte: `RetentionService` ([RetentionService.cs](backend/src/CamGuard.Api/Features/Retention/RetentionService.cs)) corre al minuto de arrancar y luego una vez al día; borra de R2 y de la BD los `Recording` con `StartedAt < corte`, además de los `CameraEvent` viejos.

El hueco: solo borra lo que tiene fila en la BD. Un objeto en R2 sin fila (migración desde SQL Server, BD recreada, fila borrada a mano) queda para siempre. Se agrega al mismo servicio un barrido del bucket que borra todo objeto de CamGuard más viejo que el corte, tenga o no fila.

## Cambios

1. **`IVideoStorage` / `S3VideoStorage`** ([VideoStorage.cs](backend/src/CamGuard.Api/Common/Storage/VideoStorage.cs))
   - Nuevo `IAsyncEnumerable<StoredObject> ListAsync(CancellationToken ct)` con `record StoredObject(string Key, DateTimeOffset LastModified)`.
   - Implementación con `ListObjectsV2Async` paginando por `ContinuationToken` (R2 lo admite; tope de 1000 por página). `LastModified` del SDK v4 es `DateTime?`: se convierte a UTC y se omite el objeto si viene nulo.
   - El borrado sigue siendo `DeleteAsync` por objeto (no `DeleteObjects`: es la operación que exige checksum y con R2 + `WHEN_REQUIRED` es frágil; el volumen diario es chico).

2. **`StorageKeys`**: `public static bool IsManaged(string key)` con una regex del formato `{cámara}/{yyyy}/{MM}/{dd}/{guid N}.mp4|.jpg`. Así el barrido nunca toca objetos ajenos si el bucket se comparte o se configura mal.

3. **`RetentionPolicy`** (pura): `public static bool IsOrphanExpired(string key, DateTimeOffset lastModified, DateTimeOffset cutoff) => StorageKeys.IsManaged(key) && lastModified < cutoff;`
   - Se usa `LastModified` (hora de subida, siempre ≥ `StartedAt`): un clip subido tarde por el backoff nunca se borra antes que su fila.

4. **`RetentionService.RunOnceAsync`**: después del borrado por BD y antes de los eventos, llama a `SweepStorageAsync(cutoff, ct)`:
   - Recorre `storage.ListAsync`, borra los que cumplan `IsOrphanExpired`, cuenta y sigue ante el error de un objeto (warning con la clave, sin cortar el barrido).
   - El log final queda: `"Retención ({Days} días): {Clips} clips, {Orphans} objetos huérfanos y {Events} eventos borrados"`.
   - Un fallo del listado (credenciales, red) cae en el `catch` existente de `ExecuteAsync` y se reintenta al día siguiente.

5. **CLAUDE.md**: una línea en la sección de backend sobre la retención (BD + barrido de R2 por `LastModified`, solo claves de `StorageKeys`).

## Pruebas (TUnit)

En [RetentionPolicyTests.cs](backend/tests/CamGuard.Tests/Features/Retention/RetentionPolicyTests.cs):
- `IsOrphanExpired_OldManagedKey_True` (`.mp4` y `.jpg`).
- `IsOrphanExpired_RecentKey_False`.
- `IsOrphanExpired_ForeignKey_False` (`otra/cosa.mp4`, `1/2026/10/04/no-guid.mp4`, `1/2026/10/04/{guid}.txt`).

Nuevo `tests/CamGuard.Tests/Common/Storage/StorageKeysTests.cs`: `IsManaged` acepta lo que generan `Clip`/`Thumbnail` (ida y vuelta) y rechaza otras formas.

## Verificación

1. `cd backend && dotnet build && dotnet test`.
2. Contra R2 real: subir a mano al bucket un objeto con clave válida sin fila en la BD y otro ajeno (p. ej. `1/2020/01/01/<guid>.mp4` y `nota.txt`). Como `LastModified` es la hora de subida y la retención mínima es 1 día, esperar a que tengan más de 1 día, poner Retención = 1 en Ajustes, reiniciar la API (`docker compose -f docker-compose.yml -f docker-compose.windows.yml up -d --build api`) y revisar en el log del minuto 1 la línea de retención con el conteo de huérfanos; comprobar en el panel de R2 que el `.mp4` se borró y `nota.txt` sigue. Volver la retención a 30.
