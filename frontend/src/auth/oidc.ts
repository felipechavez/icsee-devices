import { createOidc } from 'oidc-spa/core'
import type { Oidc } from 'oidc-spa/core'

export const OIDC_CLIENT_ID = 'camguard-web'

/** Keycloak se sirve en el mismo origen bajo /auth (nginx en producción, Vite en desarrollo). */
export const issuerUri = () => `${window.location.origin}/auth/realms/camguard`

type DecodedIdToken = {
  sub: string
  preferred_username?: string
  name?: string
  email?: string
}

let oidc: Oidc.LoggedIn<DecodedIdToken> | null = null

/**
 * Inicia la sesión OIDC (Authorization Code + PKCE). Con autoLogin toda la app
 * exige sesión: si no la hay, redirige al login de Keycloak.
 */
export async function initOidc() {
  oidc = await createOidc({
    issuerUri: issuerUri(),
    clientId: OIDC_CLIENT_ID,
    autoLogin: true,
    decodedIdTokenSchema: {
      parse: (decoded: unknown) => decoded as DecodedIdToken,
    },
  })
  return oidc
}

export function getOidc() {
  if (!oidc) throw new Error('OIDC no inicializado: llama a initOidc() antes de montar la app')
  return oidc
}

/** Access token vigente; oidc-spa lo renueva antes de que caduque. */
export async function getAccessToken() {
  const { accessToken } = await getOidc().getTokens()
  return accessToken
}
