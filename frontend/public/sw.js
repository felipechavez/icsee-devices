// Service worker de CamGuard: solo da una página "Sin conexión" cuando una navegación falla.
// No guarda nada más: /api, /hubs, /auth (tokens) y los assets van siempre a la red, así no hay datos
// viejos, URLs prefirmadas caducadas ni sesiones en caché. Para publicar un cambio, sube CACHE.
const CACHE = 'camguard-offline-v1'
const OFFLINE_URL = '/offline.html'

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches
      .open(CACHE)
      .then((cache) => cache.add(new Request(OFFLINE_URL, { cache: 'reload' })))
      .then(() => self.skipWaiting()),
  )
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
      .then(() => self.clients.claim()),
  )
})

self.addEventListener('fetch', (event) => {
  const { request } = event
  if (request.mode !== 'navigate') return
  // Keycloak (/auth) navega con redirecciones propias: se deja pasar sin tocar.
  if (new URL(request.url).pathname.startsWith('/auth/')) return

  event.respondWith(
    fetch(request).catch(async () => (await caches.match(OFFLINE_URL)) ?? Response.error()),
  )
})
