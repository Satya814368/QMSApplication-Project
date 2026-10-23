(() => {
    const sidebar = document.getElementById('portal-sidebar');
    const toggle = document.getElementById('portal-menu-toggle');
    const main = document.getElementById('portal-main-shell');
    const backdrop = document.querySelector('.qms-backdrop');
    const media = window.matchMedia('(max-width: 1023px)');
    let lastFocus = null;
    function setMenu(open, restoreFocus = true) {
        if (!sidebar || !toggle || !main || !backdrop) return;
        open = Boolean(open && media.matches);
        document.body.classList.toggle('qms-menu-open', open);
        toggle.setAttribute('aria-expanded', String(open));
        toggle.setAttribute('aria-label', open ? 'Close navigation' : 'Open navigation');
        backdrop.hidden = !open;
        sidebar.inert = media.matches && !open;
        if (open) {
            lastFocus = document.activeElement;
            sidebar.querySelector('[data-close-menu]').focus();
            main.inert = true;
        } else {
            main.inert = false;
            if (restoreFocus && lastFocus?.isConnected && media.matches) lastFocus.focus();
        }
    }
    toggle?.addEventListener('click', () => setMenu(!document.body.classList.contains('qms-menu-open')));
    document.querySelectorAll('[data-close-menu]').forEach(button => button.addEventListener('click', () => setMenu(false)));
    sidebar?.querySelectorAll('a').forEach(link => link.addEventListener('click', () => setMenu(false, false)));
    document.addEventListener('keydown', event => {
        const open = document.body.classList.contains('qms-menu-open');
        if (event.key === 'Escape') {
            if (open) { setMenu(false); event.preventDefault(); }
            document.querySelectorAll('.qms-notifications[open]').forEach(panel => panel.open = false);
        }
        if (open && event.key === 'Tab') {
            const nodes = [...sidebar.querySelectorAll('a[href], button:not([disabled])')].filter(x => x.getClientRects().length);
            const first = nodes[0], last = nodes[nodes.length - 1];
            if (event.shiftKey && document.activeElement === first) { last.focus(); event.preventDefault(); }
            else if (!event.shiftKey && document.activeElement === last) { first.focus(); event.preventDefault(); }
        }
    });
    const resize = () => setMenu(false, false);
    if (media.addEventListener) media.addEventListener('change', resize);
    else media.addListener(resize);
    setMenu(false, false);
    document.addEventListener('click', event => {
        document.querySelectorAll('.qms-notifications[open]').forEach(panel => {
            if (!panel.contains(event.target)) panel.open = false;
        });
    });
    // Keep data grids within their content region, including older screens.
    document.querySelectorAll('.qms-content table').forEach(table => {
        if (table.closest('.table-responsive, .md-table-container, .table-section, .qms-table-scroll')) return;
        const scroll = document.createElement('div');
        scroll.className = 'qms-table-scroll'; scroll.tabIndex = 0;
        scroll.setAttribute('role', 'region'); scroll.setAttribute('aria-label', 'Scrollable data table');
        table.before(scroll); scroll.appendChild(table);
    });
})();
