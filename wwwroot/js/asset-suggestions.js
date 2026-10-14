// Asset name suggestions for the property page's Create asset form (Assets tab)
(function () {
    const input = document.getElementById('assetNameInput');
    const list  = document.getElementById('assetSuggestions');
    if (!input || !list) return;

    let activeIndex = -1;
    let lastQuery   = '';
    let debounceTimer;

    function showSuggestions(items) {
        list.innerHTML = '';
        activeIndex = -1;

        if (!items.length) { list.style.display = 'none'; return; }

        items.forEach((name, i) => {
            const li = document.createElement('li');
            li.className = 'list-group-item list-group-item-action py-2';
            li.textContent = name;
            li.addEventListener('mousedown', e => {
                e.preventDefault();
                input.value = name;
                list.style.display = 'none';
            });
            list.appendChild(li);
        });

        list.style.display = 'block';
    }

    function setActive(index) {
        const items = list.querySelectorAll('.list-group-item');
        items.forEach((el, i) => el.classList.toggle('active', i === index));
        activeIndex = index;
    }

    input.addEventListener('input', () => {
        clearTimeout(debounceTimer);
        const q = input.value.trim();
        if (q === lastQuery) return;
        lastQuery = q;

        if (!q) { list.style.display = 'none'; return; }

        debounceTimer = setTimeout(async () => {
            try {
                const res = await fetch(`/api/assets/suggestions?q=${encodeURIComponent(q)}`);
                const data = await res.json();
                showSuggestions(data);
            } catch { list.style.display = 'none'; }
        }, 150);
    });

    input.addEventListener('keydown', e => {
        const items = list.querySelectorAll('.list-group-item');
        if (!items.length) return;

        if (e.key === 'ArrowDown') {
            e.preventDefault();
            setActive(Math.min(activeIndex + 1, items.length - 1));
        } else if (e.key === 'ArrowUp') {
            e.preventDefault();
            setActive(Math.max(activeIndex - 1, 0));
        } else if (e.key === 'Enter' && activeIndex >= 0) {
            e.preventDefault();
            input.value = items[activeIndex].textContent;
            list.style.display = 'none';
        } else if (e.key === 'Escape') {
            list.style.display = 'none';
        }
    });

    document.addEventListener('click', e => {
        if (!input.contains(e.target) && !list.contains(e.target))
            list.style.display = 'none';
    });
})();
