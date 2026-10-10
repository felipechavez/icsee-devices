import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

const api = process.env.API_URL ?? 'http://localhost:5253'
const keycloak = process.env.KEYCLOAK_URL ?? 'http://localhost:8180'

// En desarrollo Vite hace de proxy hacia la API y Keycloak, igual que nginx en producción.
export default defineConfig({
  plugins: [react()],
  build: {
    // bootstrap.tsx usa top-level await.
    target: 'es2022',
  },
  server: {
    proxy: {
      '/api': api,
      '/hubs': { target: api, ws: true },
      '/auth': { target: keycloak, xfwd: true },
    },
  },
})
