(() => {
    const toolbar = document.querySelector('[data-mt-filters]');
    if (!toolbar) return;
    const search = toolbar.querySelector('#member-search');
    const status = toolbar.querySelector('#member-status');
    const reset = toolbar.querySelector('[data-mt-reset]');
    const count = toolbar.querySelector('[data-mt-count]');
    const empty = document.querySelector('[data-mt-empty]');
    const rows = Array.from(document.querySelectorAll('[data-mt-member]'), row => ({
        row,
        text: Array.from(row.querySelectorAll('[data-mt-search]'), node => node.textContent)
            .join(' ').toLowerCase(),
        status: row.dataset.membership
    }));

    function filter() {
        const words = search.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
        let visible = 0;
        for (const row of rows) {
            const matches = (!status.value || row.status === status.value) &&
                words.every(word => row.text.includes(word));
            row.row.hidden = !matches;
            if (matches) visible++;
        }
        empty.hidden = visible > 0;
        count.textContent = `Showing ${visible} of ${rows.length} members`;
    }

    search.addEventListener('input', filter);
    status.addEventListener('change', filter);
    reset.addEventListener('click', () => {
        search.value = '';
        status.value = '';
        filter();
        search.focus();
    });
    filter();
    toolbar.hidden = false;
})();
