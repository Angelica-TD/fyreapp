// Property → Routines → "Add routine services" (Views/Property/_AddRoutineServices.cshtml), as Uptick's modal:
// ticking a routine type shows its levels with the default ones ticked; the button counts the routine types
// with a level ticked; search and the standard filter narrow the list, and the header box ticks what's shown.
(function () {
    const form = document.querySelector('.js-add-routines');
    if (!form) return;

    const rows = Array.from(form.querySelectorAll('.js-routine-row'));
    const all = form.querySelector('.js-routine-all');
    const submit = form.querySelector('.js-routine-submit');
    const search = form.querySelector('.js-routine-search');
    const standard = form.querySelector('.js-routine-standard');

    const typeBox = row => row.querySelector('.js-routine-type');
    const levelBoxes = row => Array.from(row.querySelectorAll('.js-routine-level'));
    const visible = () => rows.filter(r => !r.hidden && !typeBox(r).disabled);

    function setType(row, on) {
        const box = typeBox(row);
        if (box.disabled) return;
        box.checked = on;
        row.querySelector('.js-routine-levels').hidden = !on;
        row.classList.toggle('table-active', on);
        const levels = levelBoxes(row);
        if (on) {
            // Uptick ticks the levels that are on by default (or every level when none is)
            const defaults = levels.filter(l => l.dataset.default === 'true');
            (defaults.length ? defaults : levels).forEach(l => { l.checked = true; });
        } else {
            levels.forEach(l => { l.checked = false; });
        }
    }

    function refresh() {
        const count = rows.filter(r => levelBoxes(r).some(l => l.checked)).length;
        submit.textContent = `Add ${count} routine service${count === 1 ? '' : 's'}`;
        submit.disabled = count === 0;

        const shown = visible();
        const ticked = shown.filter(r => typeBox(r).checked).length;
        all.checked = shown.length > 0 && ticked === shown.length;
        all.indeterminate = ticked > 0 && ticked < shown.length;
    }

    rows.forEach(row => {
        typeBox(row).addEventListener('change', e => { setType(row, e.target.checked); refresh(); });
        // Unticking a type's last level unticks the type; ticking a level ticks its type
        levelBoxes(row).forEach(l => l.addEventListener('change', () => {
            if (!levelBoxes(row).some(x => x.checked)) {
                setType(row, false);
            } else {
                typeBox(row).checked = true;
                row.querySelector('.js-routine-levels').hidden = false;
                row.classList.add('table-active');
            }
            refresh();
        }));
    });

    all.addEventListener('change', () => {
        visible().forEach(row => setType(row, all.checked));
        refresh();
    });

    function filter() {
        const term = search.value.trim().toLowerCase();
        const std = standard.value;
        rows.forEach(row => {
            row.hidden = (term && !row.dataset.name.includes(term)) || (std && row.dataset.standard !== std);
        });
        refresh();
    }
    search.addEventListener('input', filter);
    standard.addEventListener('change', filter);

    refresh();

    // Linked straight to (…?tab=routines#add-routine-services): open it
    if (window.location.hash === '#add-routine-services' && window.bootstrap) {
        window.bootstrap.Modal.getOrCreateInstance(document.getElementById('addRoutineServices')).show();
    }
})();
