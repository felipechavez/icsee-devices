import { getOidc } from '../auth/oidc'

/** La sesión no cambia sin recargar la página: con autoLogin siempre hay usuario. */
export function useAuth() {
  const oidc = getOidc()
  const { preferred_username, name, sub } = oidc.getDecodedIdToken()
  return {
    username: preferred_username ?? name ?? sub,
    // Cierra también la sesión de Keycloak (end_session) y vuelve al inicio.
    logout: () => oidc.logout({ redirectTo: 'home' }),
  }
}
