---
commit: 06e6c86
base: 06e6c86
fecha: 2026-11-08
sprints: [17, 19]
---

# Revisión de seguridad — Sprint 17 Robustez (y Sprint 19 Despliegue Raspberry)

Resumen: 0 altas, 0 medias, 1 baja. Los cambios no abren endpoints ni exponen secretos. Lo único recomendable es restringir los permisos de los respaldos.

Los dos sprints no tienen commits entre medio y comparten el mismo diff: todo el cambio está **sin commitear** sobre HEAD `06e6c86`. La próxima revisión volverá a verlo si cambia.

## Hallazgos

### [Baja] Los respaldos se crean legibles por cualquier usuario del equipo
- Dónde: `scripts/backup-db.sh:15-25` y `scripts/deploy.sh` (`mkdir -p backups`).
- Problema: sin `umask`, la carpeta queda en 755 y los `.sql.gz` en 644. Contienen los hashes de las contraseñas de Keycloak, los secretos del realm (SMTP, llaves) y las contraseñas DVRIP cifradas. Las llaves de Data Protection no van en el respaldo, por eso es Baja. Cualquier otro usuario local de la Pi puede leerlos.
- Arreglo: poner `umask 077` al inicio de `backup-db.sh` y `chmod 700 backups` donde se crea la carpeta (`backup-db.sh` y `deploy.sh`). La API los monta en `:ro` y corre como root en el contenedor, así que sigue pudiendo leerlos. Si la imagen corre sin root, hay que comprobar que todavía lee `last-verify.json` y la fecha de los archivos.

## Dependencias
- `dotnet list package --vulnerable --include-transitive`: sin paquetes vulnerables.
- `npm audit --omit=dev` (frontend, cambió `package-lock.json`): 0 vulnerabilidades.
- `keycloak/theme`: no cambió, no se auditó.

## Revisado sin hallazgos
- **Autorización**: no hay endpoints nuevos. `UpdateSettings` valida `LocalMaxGb` (1–500).
- **Path traversal**: el desalojo local (`UploadService.EvictIfNeededAsync`) arma las rutas con `ClipPaths.For(dataDirectory, Guid)` y nunca usa datos del usuario. `NotificationOutbox` nombra sus archivos con ticks y un `Guid`. `ReadBackups` lee una carpeta fija de la configuración.
- **Deserialización**: `last-verify.json` lo escribe un script del host y se lee con `JsonException` controlada. Un JSON ilegible de la bandeja de avisos se borra.
- **Secretos en logs y respuestas**: el panel muestra el `LastError` de ntfy, que es el mensaje de `HttpClient` (estado o host), sin el token, que viaja en la cabecera. El detalle de verificación sale escapado de `verify-backup.sh` y React lo muestra como texto.
- **Frontend**: no hay `dangerouslySetInnerHTML`, `href` dinámicos nuevos ni datos en `localStorage`.
- **docker-compose**: `./backups:/backups:ro`, sin puertos nuevos y `go2rtc.yaml` sigue en `:ro`.
- **Scripts**:
  - `verify-backup.sh` restaura en un contenedor `--network none`, borrado con `trap`, y entre comillas.
  - `deploy.sh` usa `git pull --ff-only` y `$@` sin comillas (argumentos del operador, sin riesgo).
  - No hay contraseñas en la línea de comandos: la de `POSTGRES_PASSWORD=verify` es de una base efímera sin red.
- **Secretos commiteados**: ninguno en el diff.
