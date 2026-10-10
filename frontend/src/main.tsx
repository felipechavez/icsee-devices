import { oidcEarlyInit } from 'oidc-spa/core'

// Debe ejecutarse antes que cualquier otro módulo: procesa la respuesta de
// Keycloak (?code=…) y protege los tokens antes de que cargue la app.
const { shouldLoadApp } = oidcEarlyInit({ BASE_URL: import.meta.env.BASE_URL })

if (shouldLoadApp) {
  import('./bootstrap')
}
