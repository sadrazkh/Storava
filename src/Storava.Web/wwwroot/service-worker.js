const cacheName = 'storava-shell-v4';
const shellAssets = [
  '/',
  '/privacy',
  '/scan',
  '/site.webmanifest',
  '/icons/favicon.svg',
  '/icons/app-icon.svg',
  '/dist/assets/app.css',
  '/dist/assets/scan.css',
  '/dist/pages/landing.js',
  '/dist/pages/privacy.js',
  '/dist/pages/scan.js',
  '/dist/chunks/pwaService.js',
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(cacheName)
      .then((cache) => cache.addAll(shellAssets))
      .then(() => self.skipWaiting()),
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((key) => key !== cacheName).map((key) => caches.delete(key))))
      .then(() => self.clients.claim()),
  );
});

self.addEventListener('fetch', (event) => {
  if (event.request.method !== 'GET' || new URL(event.request.url).origin !== self.location.origin) {
    return;
  }

  event.respondWith(
    fetch(event.request)
      .then((response) => {
        if (!response.ok) return response;
        const copy = response.clone();
        void caches.open(cacheName).then((cache) => cache.put(event.request, copy));
        return response;
      })
      .catch(() => caches.match(event.request).then((response) => response ?? caches.match('/'))),
  );
});
