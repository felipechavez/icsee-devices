# 1. Tema de login y correos (Keycloakify): genera dist_keycloak/camguard-theme.jar (necesita Maven).
FROM node:24-bookworm AS theme
RUN apt-get update \
    && apt-get install -y --no-install-recommends maven openjdk-17-jdk-headless \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /theme
COPY theme/package.json theme/package-lock.json ./
# El postinstall (sync-extensions) necesita src/: se ejecuta después de copiarlo.
RUN npm ci --ignore-scripts
COPY theme/ .
RUN npx keycloakify sync-extensions && npm run build-keycloak-theme

# 2. Keycloak optimizado con el tema y las opciones de build fijas.
FROM quay.io/keycloak/keycloak:26.8 AS builder
ENV KC_DB=postgres \
    KC_HEALTH_ENABLED=true \
    KC_HTTP_RELATIVE_PATH=/auth
COPY --from=theme /theme/dist_keycloak/camguard-theme.jar /opt/keycloak/providers/
RUN /opt/keycloak/bin/kc.sh build

FROM quay.io/keycloak/keycloak:26.8
COPY --from=builder /opt/keycloak/ /opt/keycloak/
# Se importa solo si el realm no existe; los ${VAR} se toman del entorno.
COPY realm-camguard.json /opt/keycloak/data/import/realm-camguard.json
ENTRYPOINT ["/opt/keycloak/bin/kc.sh"]
CMD ["start", "--optimized", "--import-realm"]
