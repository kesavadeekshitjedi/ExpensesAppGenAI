// Minimal, safe service worker: offline app shell + fast static-asset loads. It only ever touches
// same-origin GET requests, so the cross-origin API (auth, expenses, receipts) is never intercepted
// or cached — authentication and fresh data are unaffected.
const CACHE = 'home-expenses-v1'

self.addEventListener('install', () => self.skipWaiting())

self.addEventListener('activate', (event) => {
  event.waitUntil(
    (async () => {
      const keys = await caches.keys()
      await Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k)))
      await self.clients.claim()
    })(),
  )
})

self.addEventListener('fetch', (event) => {
  const req = event.request
  if (req.method !== 'GET') return

  const url = new URL(req.url)
  if (url.origin !== self.location.origin) return // leave the API (different origin) alone

  // Navigations: network-first, falling back to the cached app shell when offline.
  if (req.mode === 'navigate') {
    event.respondWith(
      (async () => {
        try {
          const res = await fetch(req)
          const cache = await caches.open(CACHE)
          cache.put('/index.html', res.clone())
          return res
        } catch {
          return (await caches.match('/index.html')) || Response.error()
        }
      })(),
    )
    return
  }

  // Build assets and icons: cache-first, then network (and cache for next time).
  if (url.pathname.startsWith('/assets/') || /\.(?:js|css|png|svg|ico|json|woff2?)$/.test(url.pathname)) {
    event.respondWith(
      (async () => {
        const cached = await caches.match(req)
        if (cached) return cached
        try {
          const res = await fetch(req)
          const cache = await caches.open(CACHE)
          cache.put(req, res.clone())
          return res
        } catch {
          return cached || Response.error()
        }
      })(),
    )
  }
})
