// The field app's task page (Views/Field/Visit.cshtml)
(function () {
    // Starting and finishing time: note where the phone is, if it says within a few seconds (never blocks)
    document.querySelectorAll('form.js-geo').forEach(form => {
        form.addEventListener('submit', e => {
            if (e.defaultPrevented || form.dataset.located) return;
            // Asked once, before waiting on the location (sending it on doesn't ask again)
            if (form.dataset.confirm && !confirm(form.dataset.confirm)) { e.preventDefault(); return; }
            if (!navigator.geolocation) return;
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

    // Photos (raising and resolving remarks): shrunk on the phone to at most 1600px JPEGs before sending, so they go
    // up quickly on poor signal; any the browser can't read go as they are
    const MaxSide = 1600;
    const shrink = async file => {
        if (!file.type.startsWith('image/')) return file;
        try {
            const bitmap = await createImageBitmap(file);
            const scale = Math.min(1, MaxSide / Math.max(bitmap.width, bitmap.height));
            const canvas = document.createElement('canvas');
            canvas.width = Math.round(bitmap.width * scale);
            canvas.height = Math.round(bitmap.height * scale);
            canvas.getContext('2d').drawImage(bitmap, 0, 0, canvas.width, canvas.height);
            bitmap.close();
            const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.85));
            if (!blob || blob.size >= file.size) return file;
            return new File([blob], file.name.replace(/\.[^.]+$/, '') + '.jpg', { type: 'image/jpeg', lastModified: file.lastModified });
        } catch {
            return file;
        }
    };

    document.querySelectorAll('form.js-photos').forEach(form => {
        const input = form.querySelector('.js-photo-input');
        const previews = form.querySelector('.js-photo-previews');
        const status = form.querySelector('.js-photo-status');
        const submit = form.querySelector('button[type=submit]');

        input.addEventListener('change', () => {
            (async () => {
                submit.disabled = true;
                status.textContent = 'Preparing photos…';
                const files = await Promise.all([...input.files].map(shrink));
                if (typeof DataTransfer !== 'undefined') {
                    const dt = new DataTransfer();
                    files.forEach(f => dt.items.add(f));
                    input.files = dt.files;
                }
                previews.replaceChildren(...files.map(f => {
                    const img = document.createElement('img');
                    img.src = URL.createObjectURL(f);
                    img.className = 'rounded border';
                    img.style.cssText = 'width: 5rem; height: 5rem; object-fit: cover';
                    img.alt = f.name;
                    return img;
                }));
                const mb = files.reduce((n, f) => n + f.size, 0) / 1024 / 1024;
                status.textContent = files.length ? `${files.length} photo${files.length === 1 ? '' : 's'}, ${mb.toFixed(1)} MB` : '';
                submit.disabled = false;
            })();
        });

        form.addEventListener('submit', () => {
            submit.disabled = true;
            submit.textContent = input.files.length ? 'Sending photos…' : 'Saving…';
        });
    });

    // Signature pads (_SignaturePad): drawn with a finger or mouse, kept in the hidden input as a PNG data: URL
    document.querySelectorAll('.js-signature').forEach(pad => {
        const canvas = pad.querySelector('canvas');
        const input = pad.querySelector('input[type=hidden]');
        const ctx = canvas.getContext('2d');
        let drawing = false, last = null;

        const size = () => {
            const ratio = window.devicePixelRatio || 1;
            canvas.width = canvas.clientWidth * ratio;
            canvas.height = canvas.clientHeight * ratio;
            ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
            ctx.lineWidth = 2.5;
            ctx.lineCap = 'round';
            ctx.lineJoin = 'round';
            ctx.strokeStyle = '#111';
            input.value = '';
        };
        const at = e => {
            const r = canvas.getBoundingClientRect();
            return { x: e.clientX - r.left, y: e.clientY - r.top };
        };

        canvas.addEventListener('pointerdown', e => {
            drawing = true;
            last = at(e);
            canvas.setPointerCapture(e.pointerId);
            ctx.beginPath();
            ctx.arc(last.x, last.y, 1, 0, Math.PI * 2);
            ctx.fill();
        });
        canvas.addEventListener('pointermove', e => {
            if (!drawing) return;
            const p = at(e);
            ctx.beginPath();
            ctx.moveTo(last.x, last.y);
            ctx.lineTo(p.x, p.y);
            ctx.stroke();
            last = p;
        });
        const end = () => {
            if (!drawing) return;
            drawing = false;
            input.value = canvas.toDataURL('image/png');
        };
        canvas.addEventListener('pointerup', end);
        canvas.addEventListener('pointercancel', end);
        pad.querySelector('.js-signature-clear').addEventListener('click', () => {
            ctx.clearRect(0, 0, canvas.width, canvas.height);
            input.value = '';
        });
        // Sized once shown (a closed <details> has no size yet)
        const details = pad.closest('details');
        if (details && !details.open) details.addEventListener('toggle', size, { once: true });
        else size();
    });

    // A task line saves as it's ticked
    document.querySelectorAll('form.js-line').forEach(form => {
        const box = form.querySelector('input[type=checkbox]');
        const status = form.querySelector('.js-status');
        box.addEventListener('change', async () => {
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
                status.textContent = '';
                const card = form.closest('.js-lines');
                card.querySelector('.js-done').textContent = card.querySelectorAll('input[type=checkbox]:checked').length;
            } catch (err) {
                box.checked = !box.checked;
                status.className = 'small js-status text-danger';
                status.textContent = (err.message || 'Not saved.') + ' Try again.';
            }
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
                setTimeout(() => { if (status.textContent === 'Saved') status.textContent = ''; }, 1500);
                clear.classList.toggle('d-none', !form.querySelector('input[name=result]:checked'));
                recount();
            } catch (err) {
                status.className = 'small js-status text-danger';
                status.textContent = (err.message || 'Not saved.') + ' Check your signal and try again.';
            }
        };

        // A note and "Raise a remark" open when it fails, or on asking
        const more = form.querySelector('.js-more');
        const showMore = form.querySelector('.js-show-more');
        const reveal = focus => {
            more.classList.remove('d-none');
            showMore.classList.add('d-none');
            if (focus) form.elements.notes.focus();
        };
        showMore.addEventListener('click', () => reveal(true));

        form.querySelectorAll('input[name=result]').forEach(r => r.addEventListener('change', () => {
            save();
            if (r.value === 'Fail' && r.checked) reveal(false);
        }));
        form.elements.notes.addEventListener('change', save);
        // Raising a remark leaves the page: the note goes first
        form.querySelector('.js-remark').addEventListener('click', async e => {
            e.preventDefault();
            const href = e.currentTarget.href;
            await save();
            window.location.href = href;
        });
        clear.addEventListener('click', () => {
            form.querySelectorAll('input[name=result]').forEach(r => r.checked = false);
            save();
        });
        form.addEventListener('submit', e => { e.preventDefault(); save(); });
    });
})();
