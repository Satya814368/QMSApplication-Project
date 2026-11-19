(() => {
    const dot = document.querySelector('[data-guidelines-unread-url]');
    if (!dot) return;
    let checking = false;
    async function check() {
        if (checking || document.hidden) return;
        checking = true;
        try {
            const response = await fetch(dot.dataset.guidelinesUnreadUrl, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
            if (response.ok && (response.headers.get('content-type') || '').includes('application/json')) {
                const result = await response.json();
                dot.hidden = !result.unread;
            }
        } catch (_) { /* Retain the last known indicator until the next successful check. */ }
        finally { checking = false; }
    }
    window.setInterval(check, 30000);
    document.addEventListener('visibilitychange', check);
    window.addEventListener('pageshow', check);
})();
