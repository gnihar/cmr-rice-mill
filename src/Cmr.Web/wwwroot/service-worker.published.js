// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

// Shell cache v1.1.2. Navigations are network-first so a broken index.html cannot stick.
self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => {
    self.skipWaiting();
    event.waitUntil(onInstall(event));
});
self.addEventListener('activate', event => {
    event.waitUntil((async () => {
        await onActivate(event);
        await self.clients.claim();
    })());
});
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-v112-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/, /index\.html$/ ];

async function onInstall(event) {
    console.info('Service worker: Install');

    // Fetch and cache all matching items from the assets manifest
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Delete unused caches
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith('offline-cache-') && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    const url = new URL(event.request.url);
    const navigation = event.request.mode === 'navigate'
        || url.pathname === '/'
        || url.pathname.endsWith('/index.html');
    if (event.request.method === 'GET' && navigation) {
        try {
            return await fetch(event.request);
        } catch {
            const cache = await caches.open(cacheName);
            return (await cache.match('index.html'))
                || (await cache.match(event.request))
                || new Response('offline', { status: 503 });
        }
    }

    if (event.request.method !== 'GET')
        return fetch(event.request);

    const cache = await caches.open(cacheName);
    return (await cache.match(event.request)) || fetch(event.request);
}
