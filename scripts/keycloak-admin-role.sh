#!/usr/bin/env sh
# Da un rol de CamGuard a un usuario de Keycloak:
#   admin  → camguard-admin (p. ej. "Buscar en la red" en Configuración). Es el de por defecto.
#   viewer → camguard-viewer (solo lectura: ver cámaras en vivo y grabaciones; sin Estado ni Configuración).
# El realm se importa una sola vez: en una instalación anterior el rol puede no existir y este script lo crea.
# El usuario debe cerrar sesión y volver a entrar para que su token traiga el rol.
#   sh scripts/keycloak-admin-role.sh admin
#   sh scripts/keycloak-admin-role.sh mama viewer
# Usa el administrador del realm master (KC_ADMIN_USER/KC_ADMIN_PASSWORD de .env, los del primer arranque).
# Las credenciales se leen dentro del contenedor: no pasan por la línea de comandos del equipo.
set -eu

if [ "$#" -lt 1 ] || [ "$#" -gt 2 ] || [ -z "$1" ]; then
    echo "Uso: sh scripts/keycloak-admin-role.sh <usuario> [admin|viewer]" >&2
    exit 1
fi

case "${2:-admin}" in
    admin)
        ROLE=camguard-admin
        DESCRIPTION="Administra CamGuard (p. ej. buscar cámaras nuevas en la red)"
        ;;
    viewer)
        ROLE=camguard-viewer
        DESCRIPTION="Solo lectura: ver cámaras en vivo y grabaciones"
        ;;
    *)
        echo "Rol desconocido: $2 (usa admin o viewer)" >&2
        exit 1
        ;;
esac

docker compose exec -T -e CG_USER="$1" -e CG_ROLE="$ROLE" -e CG_DESCRIPTION="$DESCRIPTION" keycloak sh -c '
    set -eu
    KCADM="/opt/keycloak/bin/kcadm.sh"
    CONFIG="$(mktemp)"
    trap "rm -f \"$CONFIG\"" EXIT
    "$KCADM" config credentials --config "$CONFIG" --server http://localhost:8080/auth --realm master \
        --user "$KC_BOOTSTRAP_ADMIN_USERNAME" --password "$KC_BOOTSTRAP_ADMIN_PASSWORD" >/dev/null
    if ! "$KCADM" get "roles/$CG_ROLE" --config "$CONFIG" -r camguard >/dev/null 2>&1; then
        "$KCADM" create roles --config "$CONFIG" -r camguard -s "name=$CG_ROLE" -s "description=$CG_DESCRIPTION"
        echo "Rol $CG_ROLE creado."
    fi
    "$KCADM" add-roles --config "$CONFIG" -r camguard --uusername "$CG_USER" --rolename "$CG_ROLE"
    echo "Rol $CG_ROLE asignado a $CG_USER. Cierra sesión y vuelve a entrar para usarlo."
'
