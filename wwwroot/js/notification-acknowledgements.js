(() => {
    const dialog = document.getElementById('acknowledgements-dialog');
    const body = document.getElementById('acknowledgements-body');
    if (!dialog || !body) return;
    let request, opener;
    async function load(url) {
        if (request) request.abort();
        request = new AbortController();
        const current = request;
        if (!dialog.open) dialog.showModal();
        body.textContent = 'Loading acknowledgements…';
        body.setAttribute('aria-busy', 'true');
        try {
            const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', signal: current.signal });
            if (response.redirected && new URL(response.url).pathname.toLowerCase().endsWith('/accessdenied')) throw new Error('You do not have access to these recipients.');
            if (response.redirected && new URL(response.url).pathname.toLowerCase().endsWith('/notifications')) throw new Error('Open Notifications and acknowledge your pending notices before tracking responses.');
            if (response.redirected || response.status === 401) throw new Error('Your session expired. Close this popup and sign in again.');
            if (response.status === 403) throw new Error('You do not have access to these recipients.');
            if (!response.ok) throw new Error('Unable to load acknowledgements. Close this popup and try again.');
            const html = await response.text();
            if (!current.signal.aborted) body.innerHTML = html;
        } catch (error) {
            if (error.name !== 'AbortError') body.textContent = error.message || 'Unable to load acknowledgements.';
        } finally {
            if (current === request) body.removeAttribute('aria-busy');
        }
    }
    document.addEventListener('click', event => {
        const button = event.target.closest('[data-acknowledgements-url]');
        if (!button) return;
        event.preventDefault();
        if (!dialog.open) opener = button;
        load(button.dataset.acknowledgementsUrl);
    });
    dialog.addEventListener('submit', event => {
        const form = event.target.closest('[data-acknowledgement-filters]');
        if (!form) return;
        event.preventDefault();
        const url = new URL(form.action, window.location.href);
        for (const [name, value] of new FormData(form)) {
            if (value !== '') url.searchParams.set(name, value);
            else url.searchParams.delete(name);
        }
        load(url.toString());
    });
    dialog.querySelector('[data-close-acknowledgements]').addEventListener('click', () => dialog.close());
    dialog.addEventListener('close', () => {
        if (request) request.abort();
        body.textContent = '';
        if (opener && opener.isConnected) opener.focus();
    });
})();
