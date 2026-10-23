// The field app's outbox: changes a technician made while there was no signal, held on the phone (IndexedDB) until they
// can be sent, in the order they were made. Shared by the service worker (sw.js, which fills and sends it) and the
// field pages (field-sync.js, which shows it).
//   outbox: { id, url, fields: [[name, value | File]], at }   waiting to send
//   failed: { id, url, fields, at, error }                    the server said no (shown until dismissed)
//   meta:   { key: 'state', value: 'signin' | null }          sending stopped until they sign in again
(function (root) {
    const Name = 'fyreapp-field', Version = 1;
    let opening = null;

    const open = () => opening ??= new Promise((resolve, reject) => {
        const req = indexedDB.open(Name, Version);
        req.onupgradeneeded = () => {
            const db = req.result;
            if (!db.objectStoreNames.contains('outbox')) db.createObjectStore('outbox', { keyPath: 'id', autoIncrement: true });
            if (!db.objectStoreNames.contains('failed')) db.createObjectStore('failed', { keyPath: 'id' });
            if (!db.objectStoreNames.contains('meta')) db.createObjectStore('meta', { keyPath: 'key' });
        };
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => { opening = null; reject(req.error); };
    });

    const run = async (stores, mode, work) => {
        const db = await open();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(stores, mode);
            let result;
            Promise.resolve(work(tx)).then(r => { result = r; });
            tx.oncomplete = () => resolve(result);
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error);
        });
    };
    const req = r => new Promise((resolve, reject) => { r.onsuccess = () => resolve(r.result); r.onerror = () => reject(r.error); });

    root.FieldOutbox = {
        add: item => run(['outbox'], 'readwrite', tx => req(tx.objectStore('outbox').add(item))),
        all: () => run(['outbox'], 'readonly', tx => req(tx.objectStore('outbox').getAll())),
        count: () => run(['outbox'], 'readonly', tx => req(tx.objectStore('outbox').count())),
        remove: id => run(['outbox'], 'readwrite', tx => req(tx.objectStore('outbox').delete(id))),
        // Moves a change the server turned down to the failed list, with why
        fail: (item, error) => run(['outbox', 'failed'], 'readwrite', tx => {
            tx.objectStore('outbox').delete(item.id);
            return req(tx.objectStore('failed').put({ ...item, error }));
        }),
        failures: () => run(['failed'], 'readonly', tx => req(tx.objectStore('failed').getAll())),
        dismiss: id => run(['failed'], 'readwrite', tx => req(tx.objectStore('failed').delete(id))),
        state: () => run(['meta'], 'readonly', tx => req(tx.objectStore('meta').get('state'))).then(r => r?.value ?? null),
        setState: value => run(['meta'], 'readwrite', tx => req(tx.objectStore('meta').put({ key: 'state', value }))),
        // Signing out: nothing of this login's is left on the phone
        clear: () => run(['outbox', 'failed', 'meta'], 'readwrite', tx => {
            tx.objectStore('outbox').clear();
            tx.objectStore('failed').clear();
            tx.objectStore('meta').clear();
        })
    };
})(self);
