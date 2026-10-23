// The field app as an installed app with poor signal (every field page, Views/Shared/_LayoutField.cshtml): registers the
// service worker (sw.js), shows in the bar what's waiting to send (field-outbox.js), shows on a job the changes made
// there that haven't gone yet, keeps My day's jobs on the phone, offers to install the app, and clears it all on
// signing out.
(function () {
    const worker = 'serviceWorker' in navigator ? navigator.serviceWorker : null;
    const ask = message => worker?.ready.then(reg => reg.active?.postMessage(message));
    if (worker) worker.register('/sw.js').catch(() => { });

    const time = ms => new Date(ms).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }).toLowerCase();
    const field = (item, name) => item.fields.find(f => f[0] === name)?.[1];
    const taskOf = item => +(new URL(item.url, location.origin).pathname.match(/^\/Field\/Task\/(\d+)/)?.[1] ?? 0);

    // Jobs' names, remembered as they're opened, to say what a waiting change is for
    const names = (() => { try { return JSON.parse(localStorage.getItem('field-jobs') || '{}'); } catch { return {}; } })();
    const here = document.querySelector('[data-field-task]');
    if (here) {
        names[here.dataset.fieldTask] = here.dataset.fieldTitle;
        try { localStorage.setItem('field-jobs', JSON.stringify(names)); } catch { /* private mode */ }
    }

    // What a waiting change is, in the technician's words
    const describe = item => {
        const path = new URL(item.url, location.origin).pathname;
        const what =
            /\/Start$/.test(path) ? (field(item, 'type') === 'Travelling' ? 'Started travelling' : 'Started work')
            : /\/Finish$/.test(path) ? 'Finished work'
            : /\/Record$/.test(path) ? (field(item, 'result') ? `${field(item, 'result')} on an asset` : 'Asset result cleared')
            : /\/Line$/.test(path) ? (item.fields.some(f => f[0] === 'done' && f[1] === 'true') ? 'Line ticked off' : 'Line unticked')
            : /\/Stock\/\d+\/Remove$/.test(path) ? 'Stock taken off'
            : /\/Stock$/.test(path) ? 'Stock used'
            : /\/Resolve$/.test(path) ? 'Remark resolved'
            : /\/Remark$/.test(path) ? 'Remark raised'
            : /\/Form$/.test(path) ? 'Form filled in'
            : /\/SignOff$/.test(path) ? 'Customer sign-off'
            : /\/Complete$/.test(path) ? 'Job completed'
            : 'A change';
        const photos = item.fields.filter(f => f[0] === 'photos' && f[1] instanceof Blob && f[1].size > 0).length;
        return what + (photos ? ` (${photos} photo${photos === 1 ? '' : 's'})` : '');
    };

    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    // The bar: offline, what's waiting, what the server turned down
    const box = document.getElementById('field-sync');
    const button = document.getElementById('field-sync-button');
    const menu = document.getElementById('field-sync-menu');

    const render = async () => {
        if (!box || !window.FieldOutbox) return;
        let waiting = [], failed = [], state = null;
        try {
            [waiting, failed, state] = await Promise.all([FieldOutbox.all(), FieldOutbox.failures(), FieldOutbox.state()]);
        } catch { return; }
        const offline = !navigator.onLine;
        box.classList.toggle('d-none', !offline && waiting.length === 0 && failed.length === 0);

        button.className = 'btn btn-sm ' + (failed.length ? 'btn-danger' : waiting.length ? 'btn-warning' : 'btn-outline-light');
        button.innerHTML = failed.length ? `<i class="bi bi-exclamation-triangle"></i> ${failed.length} not saved`
            : waiting.length ? `<i class="bi bi-cloud-arrow-up"></i> ${waiting.length} to send`
            : '<i class="bi bi-wifi-off"></i> Offline';

        const job = item => names[taskOf(item)] ? `<span class="text-muted">· ${esc(names[taskOf(item)])}</span>` : '';
        menu.innerHTML =
            (state === 'signin'
                ? `<div class="alert alert-warning py-2 small mb-2">You're signed out. <a href="/">Sign in</a> to send these: they're safe on this phone.</div>`
                : offline ? '<div class="small text-muted mb-2"><i class="bi bi-wifi-off"></i> No signal. What you do is kept on this phone and sent when there\'s signal.</div>' : '')
            + (waiting.length ? `<div class="small fw-semibold">Waiting to send</div><ul class="list-unstyled small mb-2">${waiting.map(i =>
                `<li>${time(i.at)} ${esc(describe(i))} ${job(i)}</li>`).join('')}</ul>
                <button type="button" class="btn btn-sm btn-primary w-100 mb-2" data-sync-now>Send now</button>` : '')
            + (failed.length ? `<div class="small fw-semibold text-danger">Not saved</div><ul class="list-unstyled small mb-0">${failed.map(i =>
                `<li class="mb-1">${time(i.at)} ${esc(describe(i))} ${job(i)}<div class="text-danger">${esc(i.error)}</div>
                 <button type="button" class="btn btn-link btn-sm p-0" data-dismiss-failed="${i.id}">OK</button></li>`).join('')}</ul>` : '');
        held(waiting);
    };

    menu?.addEventListener('click', async e => {
        const dismiss = e.target.closest('[data-dismiss-failed]');
        if (dismiss) {
            e.stopPropagation();
            await FieldOutbox.dismiss(+dismiss.dataset.dismissFailed);
            render();
        }
        if (e.target.closest('[data-sync-now]')) ask({ type: 'flush' });
    });

    // On a job: its changes still on the phone, shown as made (a cached page is from before them)
    const held = waiting => {
        if (!here) return;
        const mine = waiting.filter(i => taskOf(i) === +here.dataset.fieldTask);
        // Sent since: no longer "on your phone"
        document.querySelectorAll('.js-status').forEach(s => { if (s.textContent.startsWith('On your phone')) s.textContent = ''; });
        for (const i of mine) {
            const path = new URL(i.url, location.origin).pathname;
            if (/\/Record$/.test(path)) {
                const form = [...document.querySelectorAll('form.js-result')].find(f =>
                    f.elements.assetId?.value === field(i, 'assetId') && f.elements.routine?.value === field(i, 'routine'));
                if (!form) continue;
                form.querySelectorAll('input[name=result]').forEach(r => r.checked = r.value === field(i, 'result'));
                if (form.elements.notes) form.elements.notes.value = field(i, 'notes') ?? '';
                const status = form.querySelector('.js-status');
                if (status) { status.className = 'small js-status text-warning-emphasis'; status.textContent = 'On your phone, not sent yet'; }
            } else if (/\/Line$/.test(path)) {
                const box = document.getElementById('line-' + field(i, 'subtaskId'));
                if (box) box.checked = i.fields.some(f => f[0] === 'done' && f[1] === 'true');
            }
        }
        // The rest (the clock, remarks, sign-off, …) listed at the top of the job
        let card = document.getElementById('field-held');
        const others = mine.filter(i => !/\/(Record|Line)$/.test(new URL(i.url, location.origin).pathname));
        if (others.length === 0) { card?.remove(); return; }
        if (!card) {
            card = document.createElement('div');
            card.id = 'field-held';
            card.className = 'alert alert-warning py-2 small';
            document.querySelector('.field-main')?.prepend(card);
        }
        card.innerHTML = `<div class="fw-semibold"><i class="bi bi-cloud-arrow-up"></i> On your phone, sending when there's signal</div>`
            + `<ul class="list-unstyled mb-0">${others.map(i => `<li>${time(i.at)} ${esc(describe(i))}</li>`).join('')}</ul>`;
    };

    // A remark on an asset, opened without signal: the job's remark form stands in, so put the asset in it
    const remarkFor = new URLSearchParams(location.search).get('assetId');
    const remarkAsset = document.querySelector('input[name="Input.AssetId"]');
    if (remarkFor && remarkAsset && !remarkAsset.value) {
        remarkAsset.value = remarkFor;
        const on = document.querySelector('#remark-on .card-body');
        let label = '';
        try { label = sessionStorage.getItem('remark-asset') || ''; } catch { /* private mode */ }
        if (on) on.innerHTML = `<div class="fw-semibold">${esc(label || 'The asset you picked')}</div>`
            + '<div class="small text-muted">No signal: remark types for this asset show once there\'s signal</div>';
    }

    // Just held (no signal when it was sent): say so
    if (location.hash === '#held') {
        history.replaceState(null, '', location.pathname + location.search);
        const note = document.createElement('div');
        note.className = 'alert alert-warning py-2';
        note.innerHTML = '<i class="bi bi-wifi-off"></i> No signal: saved on your phone. It sends when there\'s signal.';
        document.querySelector('.field-main')?.prepend(note);
    }

    worker?.addEventListener('message', e => { if (e.data?.type === 'outbox') render(); });
    const retry = () => { render(); if (navigator.onLine) ask({ type: 'flush' }); };
    window.addEventListener('online', retry);
    window.addEventListener('offline', render);
    document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') retry(); });
    // Phones without background sync: keep trying while the app is open
    setInterval(async () => {
        if (navigator.onLine && window.FieldOutbox && await FieldOutbox.count() > 0) ask({ type: 'flush' });
    }, 30000);
    retry();

    // When it was pressed, sent with every change: one that has to wait for signal keeps its time (the clock's buttons
    // first wait a few seconds for the location, so the time is taken here). Restamped if pressed again later.
    document.addEventListener('submit', e => {
        const form = e.target;
        if (form.method.toLowerCase() !== 'post' || form.classList.contains('js-sign-out')) return;
        let stamp = form.querySelector('input[name=doneAt]');
        if (!stamp) {
            stamp = Object.assign(document.createElement('input'), { type: 'hidden', name: 'doneAt' });
            form.append(stamp);
        }
        if (!stamp.value || Date.now() - +stamp.value > 30000) stamp.value = Date.now();
    }, true);

    // Kept on the phone for later without signal: this page (the first one opened, before the service worker was
    // looking after them), and on My day, its jobs
    const keep = [];
    if (worker && !worker.controller) keep.push(location.pathname + location.search);
    const jobs = document.querySelector('[data-field-keep]');
    if (jobs) {
        try { keep.push(...JSON.parse(jobs.dataset.fieldKeep)); } catch { /* nothing to keep */ }
    }
    if (keep.length && navigator.onLine) ask({ type: 'keep', urls: keep });

    // Installing: Android offers it (shown in the menu); iPhones need Share, Add to Home Screen
    const install = document.getElementById('field-install');
    const standalone = matchMedia('(display-mode: standalone)').matches || navigator.standalone;
    let offer = null;
    window.addEventListener('beforeinstallprompt', e => {
        e.preventDefault();
        offer = e;
        install?.classList.remove('d-none');
    });
    install?.addEventListener('click', async () => {
        if (offer) {
            offer.prompt();
            await offer.userChoice;
            offer = null;
            install.classList.add('d-none');
        } else {
            alert('To put FyreApp on your home screen: tap Share, then "Add to Home Screen".');
        }
    });
    if (install && !standalone && /iPhone|iPad|iPod/.test(navigator.userAgent)) install.classList.remove('d-none');

    // Signing out leaves nothing of theirs on the phone (another technician may sign in on it)
    document.querySelectorAll('form.js-sign-out').forEach(form => form.addEventListener('submit', async e => {
        if (form.dataset.cleared) return;
        e.preventDefault();
        const waiting = window.FieldOutbox ? await FieldOutbox.count().catch(() => 0) : 0;
        if (waiting > 0 && !confirm(`${waiting} change${waiting === 1 ? " hasn't" : "s haven't"} been sent yet. Signing out throws ${waiting === 1 ? 'it' : 'them'} away. Sign out anyway?`))
            return;
        try {
            await FieldOutbox?.clear();
            for (const name of await caches.keys())
                if (name === 'fyre-pages' || name === 'fyre-files') await caches.delete(name);
        } catch { /* sign out regardless */ }
        form.dataset.cleared = '1';
        form.submit();
    }));
})();
