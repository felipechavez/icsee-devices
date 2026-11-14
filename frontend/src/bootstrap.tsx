import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '@fontsource-variable/geist'
import '@fontsource-variable/geist-mono'
import './index.css'
import App from './App.tsx'
import { initOidc } from './auth/oidc'

// Sin sesión, initOidc redirige a Keycloak y nunca resuelve.
await initOidc()

// App instalable: el service worker solo da la página "Sin conexión" (public/sw.js). Se registra con la
// sesión ya iniciada para no cruzarse con el regreso de Keycloak (?code=…). En desarrollo no hace falta.
if (import.meta.env.PROD && 'serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js').catch((e: unknown) => {
    console.warn('No se pudo registrar el service worker', e)
  })
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
