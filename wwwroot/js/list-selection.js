// Uptick-style selection on the list pages (Routines, Properties, Assets, Remarks):
// tick rows (.js-select-row), tick the page (.js-select-page), or "Select all N" (.js-select-all), which
// sets the bulk form's allMatching so the server applies the action to every row matching the filters.
// Once anything is selected, Edit / Clear selection replace the count (see _SelectionToolbar.cshtml).
(function () {
    const toolbar = document.querySelector('[data-list-selection]');
    if (!toolbar) return;

    const total = Number(toolbar.dataset.total);
    const boxes = Array.from(document.querySelectorAll('.js-select-row'));
    const selectPage = document.querySelector('.js-select-page');
    const selectAll = toolbar.querySelector('.js-select-all');
    const allMatching = document.querySelector('.js-all-matching');
    const edit = toolbar.querySelector('.js-edit');
    const clear = toolbar.querySelector('.js-clear');
    const count = toolbar.querySelector('.js-count');

    function refresh() {
        const ticked = boxes.filter(b => b.checked).length;
        const all = allMatching?.value === 'true';
        const selected = all ? total : ticked;

        document.querySelectorAll('.js-selected-count').forEach(el => el.textContent = selected.toLocaleString());
        edit?.classList.toggle('d-none', selected === 0);
        clear?.classList.toggle('d-none', selected === 0);
        count?.classList.toggle('d-none', selected > 0);
        selectAll?.classList.toggle('d-none', all || boxes.length === 0 || ticked !== boxes.length || total <= boxes.length);
        if (selectPage) {
            selectPage.checked = boxes.length > 0 && ticked === boxes.length;
            selectPage.indeterminate = ticked > 0 && ticked < boxes.length;
        }
    }

    function setAll(value) {
        if (allMatching) allMatching.value = value ? 'true' : 'false';
    }

    boxes.forEach(b => b.addEventListener('change', () => { setAll(false); refresh(); }));
    selectPage?.addEventListener('change', () => {
        boxes.forEach(b => b.checked = selectPage.checked);
        setAll(false);
        refresh();
    });
    selectAll?.addEventListener('click', () => { setAll(true); refresh(); });
    clear?.addEventListener('click', () => {
        boxes.forEach(b => b.checked = false);
        setAll(false);
        refresh();
    });

    // Bulk forms: fields for the chosen action only
    document.querySelectorAll('.js-bulk-action').forEach(radio => radio.addEventListener('change', showActionFields));
    function showActionFields() {
        const chosen = document.querySelector('.js-bulk-action:checked')?.value;
        document.querySelectorAll('[data-for-action]').forEach(el => {
            const on = el.dataset.forAction === chosen;
            el.classList.toggle('d-none', !on);
            el.querySelectorAll('input, select, textarea').forEach(i => i.disabled = !on);
        });
    }

    refresh();
    showActionFields();
})();
