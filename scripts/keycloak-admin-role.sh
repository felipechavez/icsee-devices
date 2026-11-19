#!/usr/bin/env sh
# Da el rol camguard-admin (p. ej. "Buscar en la red" en Configuración) a un usuario de Keycloak.
# El realm se importa una sola vez: en una instalación anterior al Sprint 21 el rol no existe y este script
# lo crea. El usuario debe cerrar sesión y volver a entrar para que su token traiga el rol.
#   sh scripts/keycloak-admin-role.sh admin
# Usa el administrador del realm master (KC_ADMIN_USER/KC_ADMIN_PASSWORD de .env, los del primer arranque).
# Las credenciales se leen dentro del contenedor: no pasan por la línea de comandos del equipo.
set -eu

if [ "$#" -ne 1 ] || [ -z "$1" ]; then
    echo "Uso: sh scripts/keycloak-admin-role.sh <usuario>" >&2
    exit 1
fi

docker compose exec -T -e CG_USER="$1" keycloak sh -c '
    set -eu
    KCADM="/opt/keycloak/bin/kcadm.sh"
    CONFIG="$(mktemp)"
    trap "rm -f \"$CONFIG\"" EXIT
    "$KCADM" config credentials --config "$CONFIG" --server http://localhost:8080/auth --realm master \
        --user "$KC_BOOTSTRAP_ADMIN_USERNAME" --password "$KC_BOOTSTRAP_ADMIN_PASSWORD" >/dev/null
    if ! "$KCADM" get roles/camguard-admin --config "$CONFIG" -r camguard >/dev/null 2>&1; then
        "$KCADM" create roles --config "$CONFIG" -r camguard -s name=camguard-admin \
            -s "description=Administra CamGuard (p. ej. buscar cámaras nuevas en la red)"
        echo "Rol camguard-admin creado."
    fi
    "$KCADM" add-roles --config "$CONFIG" -r camguard --uusername "$CG_USER" --rolename camguard-admin
    echo "Rol camguard-admin asignado a $CG_USER. Cierra sesión y vuelve a entrar para usarlo."
'
