---
description: Escribe y crea un commit leyendo los cambios en stage (o sin stagear si no hay nada en stage)
argument-hint: [contexto opcional / scope]
model: claude-haiku-4-5-20251001
allowed-tools: Bash(git status:*), Bash(git diff:*), Bash(git add:*), Bash(git log:*), Bash(git commit:*), Bash(cat:*), Bash(rm:*), SlashCommand(/model:*)
---

# /commit

Objetivo: analizar los cambios del working tree y crear un commit con un mensaje claro, siguiendo las convenciones del repo. **Nunca** incluir `Co-Authored-By` de Claude (ver memoria del proyecto).

Contexto opcional del usuario: `$ARGUMENTS`

## Pasos

1. **Inspeccionar el estado** — Ejecutar en paralelo:
   - `git status --porcelain` para ver qué hay en stage y qué sin stagear.
   - `git diff --staged` (cambios ya en stage).
   - `git diff` (cambios sin stagear).
   - `git log --oneline -10` para captar el estilo de los mensajes recientes.

2. **Decidir qué se commitea**:
   - Si **hay archivos en stage**, commitea SOLO lo que está en stage. No hagas `git add` de nada adicional salvo que el usuario lo pida.
   - Si **no hay nada en stage**, considera todos los archivos modificados/nuevos como el conjunto a commitear y haz `git add` de los relevantes (omite archivos claramente no relacionados, temporales o de configuración local; si dudas, pregunta).
   - Nunca stagees archivos que parezcan secretos (`.env`, claves, tokens).

3. **Redactar el mensaje** — Seguir el estilo observado en `git log` del repo:
   - Formato Conventional Commits en español: `tipo(scope): descripción en minúsculas`.
     Ejemplos del repo: `fix(landing): se elimina texto hardcodeado`, `feat(landingpage): se integra nuevo landing page`.
   - Tipos: `feat`, `fix`, `refactor`, `chore`, `docs`, `test`, `style`, `perf`.
   - El `scope` se infiere del área tocada (ej. `frontend`, `backend`, `gateway`, `landing`, `auth`, `transacciones`).
   - Descripción concisa que explique el **qué** y el **porqué**, no el cómo. Usar el `$ARGUMENTS` del usuario como contexto si lo aportó.
   - Si los cambios abarcan varias áreas no relacionadas, sugerir al usuario dividir en varios commits antes de continuar.
   - Si el cambio es grande, agregar un cuerpo con bullets describiendo los puntos principales.

4. **Crear el commit** — Usar `git commit` con el mensaje redactado. Para mensajes multilínea usar un heredoc:
   ```
   git commit -m "$(cat <<'EOF'
   tipo(scope): descripción

   - punto 1
   - punto 2
   EOF
   )"
   ```
   - **PROHIBIDO** añadir `Co-Authored-By: Claude ...` o cualquier firma de autoría de Claude. El commit es solo del usuario.
   - No usar `--no-verify` salvo que el usuario lo pida explícitamente; si un hook falla, reportar el error y arreglar la causa.

5. **Confirmar** — Mostrar el resultado de `git commit` (hash + resumen) y recordar al usuario que el commit NO se ha pusheado (no hacer `git push` salvo que lo pida).

6. **Restaurar el modelo previo** — Este comando corre en Haiku (ver frontmatter). Al terminar, volver al modelo que estaba activo **antes** de invocar `/commit`:
   - El hook `UserPromptSubmit` (`capture_model_before_commit.py`) guardó ese modelo en `.claude/hooks/.model_before_commit` justo antes del cambio.
   - Leerlo con `cat .claude/hooks/.model_before_commit`.
   - Si el archivo existe y tiene un id de modelo, ejecutar `/model <id>` para restaurarlo, y luego borrar el archivo con `rm .claude/hooks/.model_before_commit` para no dejar estado obsoleto.
   - Si el archivo **no existe o está vacío** (p. ej. `/commit` no se invocó como slash command, o el hook no capturó nada), **no hacer nada**: no adivinar ni forzar un modelo.

## Notas

- No hacer `git push` automáticamente.
- Si el working tree está limpio (nada en stage ni sin stagear), informarlo y no crear commit vacío.
