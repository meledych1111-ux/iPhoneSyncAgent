const CACHE_NAME = 'iphonesync-v1';
const ASSETS = ['index.html', 'manifest.json'];

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(CACHE_NAME).then((cache) => cache.addAll(ASSETS)));
});

self.addEventListener('fetch', (event) => {
  // Перехват Share Target (когда жмем "Поделиться")
  if (event.request.method === 'POST' && event.request.url.includes('index.html')) {
    event.respondWith(Response.redirect('index.html', 303));
    event.waitUntil(async function() {
      const data = await event.request.formData();
      const files = data.getAll('files');
      const clients = await self.clients.matchAll({ type: 'window' });
      for (const client of clients) {
        client.postMessage({ type: 'SHARE_TARGET_FILES', files: files });
      }
    }());
    return;
  }
  event.respondWith(caches.match(event.request).then((response) => response || fetch(event.request)));
});
