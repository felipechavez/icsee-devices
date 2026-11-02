---
description: Respalda lo que git no guarda (.env, bases de datos, llaves, configuración de Claude). Úsalo cuando el usuario diga que va a formatear o cambiar de equipo.
argument-hint: [carpeta de destino, p. ej. E:\Respaldos]
---

# /formatear

Destino indicado por el usuario: `$ARGUMENTS`

1. **Destino** — Si no viene arriba, pregúntalo. Debe estar fuera del disco que se va a formatear: un disco externo o una carpeta que se sincronice con la nube (esta última necesita `-PermitirDiscoDelSistema`). No elijas un destino por tu cuenta.
2. **Ejecutar** — `.\scripts\backup-local.ps1 -Destino "<destino>"`. Si falla porque Docker no está corriendo, pide al usuario abrir Docker Desktop y vuelve a ejecutarlo.
3. **Informar** — Di claramente si puede formatear:
   - Con salida distinta de 0 no ha terminado: lista los `FALLO` y dile que no formatee todavía.
   - Si hay un `AVISO` de commits sin subir, archivos sin commit o `git stash`, ofrece resolverlo (por ejemplo, commit y push a una rama aparte). No subas nada sin que lo confirme.
4. **Recordar** — El respaldo lleva secretos en claro y `LEEME.txt` explica cómo restaurarlo. El script solo cubre este repositorio: otros proyectos, descargas y documentos del equipo quedan fuera.
