// The field app's task page (Views/Field/Visit.cshtml)
(function () {
    // Starting and finishing time: note where the phone is, if it says within a few seconds (never blocks)
    document.querySelectorAll('form.js-geo').forEach(form => {
        form.addEventListener('submit', e => {
            if (form.dataset.located || !navigator.geolocation) return;
            e.preventDefault();
            const submitter = e.submitter;
            form.querySelectorAll('button').forEach(b => b.disabled = true);
            let sent = false;
            const send = () => {
                if (sent) return;
                sent = true;
                form.dataset.located = '1';
                form.querySelectorAll('button').forEach(b => b.disabled = false);
                form.requestSubmit(submitter);
            };
            navigator.geolocation.getCurrentPosition(pos => {
                form.elements.lat.value = pos.coords.latitude;
                form.elements.lng.value = pos.coords.longitude;
                send();
            }, send, { timeout: 8000, maximumAge: 60000 });
            setTimeout(send, 10000);
        });
    });

    // An asset's result and notes save as they change, without reloading the page
    document.querySelectorAll('form.js-result').forEach(form => {
        const status = form.querySelector('.js-status');
        const clear = form.querySelector('.js-clear');
        form.querySelector('.js-save').classList.add('d-none');

        const recount = () => {
            const card = form.closest('.js-routine');
            card.querySelector('.js-done').textContent = card.querySelectorAll('input[name=result]:checked').length;
        };

        const save = async () => {
            status.className = 'small js-status text-muted';
            status.textContent = 'Saving…';
            try {
                const res = await fetch(form.action, {
                    method: 'POST',
                    body: new FormData(form),
                    headers: { 'X-Requested-With': 'fetch' }
                });
                if (!res.ok) {
                    const body = await res.json().catch(() => ({}));
                    throw new Error(body.error || (res.status === 403 ? 'Not allowed.' : 'Not saved.'));
                }
                status.textContent = 'Saved';
                clear.classList.toggle('d-none', !form.querySelector('input[name=result]:checked'));
                recount();
            } catch (err) {
                status.className = 'small js-status text-danger';
                status.textContent = (err.message || 'Not saved.') + ' Check your signal and try again.';
            }
        };

        form.querySelectorAll('input[name=result]').forEach(r => r.addEventListener('change', save));
        form.elements.notes.addEventListener('change', save);
        clear.addEventListener('click', () => {
            form.querySelectorAll('input[name=result]').forEach(r => r.checked = false);
            save();
        });
        form.addEventListener('submit', e => { e.preventDefault(); save(); });
    });
})();
