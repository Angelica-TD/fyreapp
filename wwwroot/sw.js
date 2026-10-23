// The field app's service worker (registered by Views/Shared/_LayoutField.cshtml). It keeps the field app working
// with poor signal:
//  - field pages (/Field...) come from the network when there's signal and are kept on the phone, so My day and the
//    day's jobs open without it (My day asks for its jobs to be kept as soon as it loads);
//  - anything sent from a field page (a result, the clock, a remark with photos, …) that can't reach the server is held
//    in the outbox (field-outbox.js) with the time it was made, and sent in order once there's signal;
//  - the app's own files (styles, scripts, icons) and photos already seen are kept too.
// Other pages pass straight through. Signing out clears what's kept (field-sync.js).
importScripts('/js/field-outbox.js');

const Version = 'v1';
const Static = `fyre-static-${Version}`, Pages = 'fyre-pages', Files = 'fyre-files';
const Precache = [
    '/offline.html',
    '/manifest.webmanifest',
    '/css/bootstrap.custom.css',
    '/css/site.css',
    '/lib/bootstrap-icons/font/bootstrap-icons.css',
    '/lib/bootstrap-icons/font/fonts/bootstrap-icons.woff2',
    '/lib/bootstrap/dist/js/bootstrap.bundle.min.js',
    '/js/field.js',
    '/js/field-sync.js',
    '/js/field-outbox.js',
    '/logo.png',
    '/pwa/icon-192.png'
];

self.addEventListener('install', event => {
    event.waitUntil(caches.open(Static).then(c => c.addAll(Precache)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', event => {
    event.waitUntil((async () => {
        for (const name of await caches.keys())
            if (name.startsWith('fyre-static-') && name !== Static) await caches.delete(name);
        await self.clients.claim();
    })());
});

const isStatic = path => /^\/(css|js|lib|pwa)\//.test(path) || ['/logo.png', '/offline.html', '/manifest.webmanifest'].includes(path);

self.addEventListener('fetch', event => {
    const request = event.request;
    const url = new URL(request.url);
    if (url.origin !== self.location.origin) return;
    const path = url.pathname;

    if (request.method === 'POST' && path.startsWith('/Field/')) {
        event.respondWith(send(request));
    } else if (request.method !== 'GET' || path === '/Field/Token') {
        return;
    } else if (path === '/Field' || path.startsWith('/Field/')) {
        if (request.mode === 'navigate' || (request.headers.get('Accept') || '').includes('text/html'))
            event.respondWith(page(request));
    } else if (isStatic(path)) {
        event.respondWith(asset(request, url));
    } else if (path.startsWith('/Files/')) {
        event.respondWith(file(request));
    }
});

// A field page: fresh when there's signal (and kept), else as last kept, else the offline page
async function page(request) {
    try {
        const res = await fetch(request);
        if (res.ok && res.type === 'basic' && new URL(res.url).pathname.startsWith('/Field')) {
            const cache = await caches.open(Pages);
            await cache.put(request.url.split('#')[0], res.clone());
        }
        return res;
    } catch {
        const cache = await caches.open(Pages);
        const url = new URL(request.url);
        return (await cache.match(url.href.split('#')[0]))
            // A remark on an asset: the job's remark form (field-sync.js puts the asset in)
            ?? (/^\/Field\/Task\/\d+\/Remark$/.test(url.pathname) ? await cache.match(url.origin + url.pathname) : undefined)
            ?? (await caches.match('/offline.html'))
            ?? new Response('No signal', { status: 503 });
    }
}

// Versioned files (?v=…) never change: kept. Others are served as kept and refreshed for next time.
async function asset(request, url) {
    const cache = await caches.open(Static);
    const kept = await cache.match(request);
    if (kept && url.searchParams.has('v')) return kept;
    const fresh = fetch(request).then(res => {
        if (res.ok) cache.put(request, res.clone());
        return res;
    });
    if (kept) {
        fresh.catch(() => { });
        return kept;
    }
    try {
        return await fresh;
    } catch {
        // Offline with a newer version asked for: the one kept will do
        return (await cache.match(request, { ignoreSearch: true })) ?? Response.error();
    }
}

// Photos and signatures: a file's content never changes
async function file(request) {
    const cache = await caches.open(Files);
    const kept = await cache.match(request);
    if (kept) return kept;
    const res = await fetch(request);
    if (res.ok) cache.put(request, res.clone());
    return res;
}

// Something sent from a field page. Changes already waiting go first, so this waits behind them.
async function send(request) {
    const copy = request.clone();
    if (await FieldOutbox.count() === 0) {
        try {
            return await fetch(request);
        } catch {
            // no signal: held below
        }
    }
    return hold(copy);
}

async function hold(request) {
    const fields = [];
    for (const [name, value] of await request.formData()) fields.push([name, value]);
    // When it was made: as the page stamped it on pressing (field-sync.js), else now
    const stamped = +(fields.find(f => f[0] === 'doneAt')?.[1] || 0);
    const at = stamped > 0 ? stamped : Date.now();
    if (!stamped) fields.push(['doneAt', String(at)]);
    await FieldOutbox.add({ url: request.url, fields, at });
    tell();
    try { await self.registration.sync?.register('outbox'); } catch { /* not on this phone: field-sync.js retries */ }
    flush();

    // A save as it changes is told it's held; a page goes back to its job, which shows what's waiting
    if (request.headers.get('X-Requested-With') === 'fetch')
        return new Response(JSON.stringify({ held: true }), { status: 202, headers: { 'Content-Type': 'application/json' } });
    const job = new URL(request.url).pathname.match(/^\/Field\/Task\/\d+/);
    return Response.redirect((job ? job[0] : '/Field') + '#held', 303);
}

// Sends what's waiting, in order, stopping at the first that can't get through (no signal, signed out)
let flushing = null;
function flush() {
    return flushing ??= sendAll().finally(() => { flushing = null; });
}

async function sendAll() {
    const items = await FieldOutbox.all();
    if (items.length === 0) return;

    // The page's token may be for a cookie that's gone since: a fresh one
    let token;
    try {
        const res = await fetch('/Field/Token', { credentials: 'same-origin', headers: { Accept: 'application/json' } });
        if (res.status === 401 || res.redirected) { await FieldOutbox.setState('signin'); tell(); return; }
        if (!res.ok) return;
        token = (await res.json()).token;
    } catch {
        return;
    }
    if (await FieldOutbox.state()) await FieldOutbox.setState(null);

    for (const item of items) {
        const body = new FormData();
        for (const [name, value] of item.fields)
            body.append(name, name === '__RequestVerificationToken' ? token : value);
        let res;
        try {
            res = await fetch(item.url, {
                method: 'POST', body, credentials: 'same-origin',
                headers: { 'X-Requested-With': 'fetch', Accept: 'application/json' }
            });
        } catch {
            break;
        }
        if (res.status === 401) { await FieldOutbox.setState('signin'); break; }
        if (res.ok) await FieldOutbox.remove(item.id);
        else if (res.status >= 500) break;
        else {
            const answer = await res.json().catch(() => ({}));
            await FieldOutbox.fail(item, answer.error || `Not saved (${res.status}).`);
        }
        tell();
    }
    tell();
}

async function tell() {
    for (const client of await self.clients.matchAll({ includeUncontrolled: true }))
        client.postMessage({ type: 'outbox' });
}

self.addEventListener('sync', event => {
    if (event.tag === 'outbox') event.waitUntil(flush());
});

self.addEventListener('message', event => {
    const data = event.data || {};
    if (data.type === 'flush') event.waitUntil(flush());
    if (data.type === 'keep') event.waitUntil(keep(data.urls || []));
});

// My day's jobs, kept so they open without signal later (a few at a time)
async function keep(urls) {
    const cache = await caches.open(Pages);
    const queue = urls.filter(u => typeof u === 'string' && /^\/Field([/?]|$)/.test(u)).slice(0, 80);
    const worker = async () => {
        for (let u = queue.shift(); u; u = queue.shift()) {
            try {
                const res = await fetch(u, { credentials: 'same-origin', headers: { Accept: 'text/html' } });
                if (res.ok && !res.redirected) await cache.put(new URL(u, self.location.origin).href, res);
            } catch {
                return;
            }
        }
    };
    await Promise.all([worker(), worker(), worker()]);
}
