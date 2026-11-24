(() => {
    const dialog = document.getElementById('notification-reader');
    if (!dialog) return;
    const previews = dialog.querySelector('[data-notification-previews]');
    const detail = dialog.querySelector('[data-notification-detail]');
    const title = dialog.querySelector('[data-notification-title]');
    const updatedLabel = dialog.querySelector('[data-notification-updated]');
    const date = dialog.querySelector('[data-notification-date]');
    const message = dialog.querySelector('[data-notification-message]');
    const form = dialog.querySelector('[data-notification-acknowledge]');
    const ackButton = dialog.querySelector('[data-notification-ack-button]');
    const closeButton = dialog.querySelector('[data-close-notification-reader]');
    const errorBox = dialog.querySelector('[data-notification-reader-error]');
    const help = dialog.querySelector('[data-notification-reader-help]');
    const count = dialog.querySelector('[data-notification-reader-count]');
    let pending = [], selected = null, selectedRevision = null, busy = false, refreshing = false, revision = 0, forceOpen = false, opener;
    // Keep the compact preview attached to the bell as the header or viewport moves.
    function positionPreview() {
        if (!dialog.open || dialog.classList.contains('is-expanded')) return;
        const viewport = window.visualViewport;
        const width = viewport?.width || document.documentElement.clientWidth || window.innerWidth;
        const height = viewport?.height || window.innerHeight;
        const offsetX = viewport?.offsetLeft || 0, offsetY = viewport?.offsetTop || 0;
        const panelWidth = dialog.getBoundingClientRect().width;
        const bell = [...document.querySelectorAll('[data-open-notification-reader]')].find(node => {
            const r = node.getBoundingClientRect();
            return r.width > 0 && r.height > 0 && r.bottom > offsetY && r.top < offsetY + height && r.right > offsetX && r.left < offsetX + width;
        });
        const bounds = bell?.getBoundingClientRect();
        const center = bounds ? bounds.left + bounds.width / 2 : offsetX + width - 32;
        const left = Math.max(offsetX + 12, Math.min(center - panelWidth + 28, offsetX + width - panelWidth - 12));
        const top = Math.max(offsetY + 12, Math.min(bounds ? bounds.bottom + 14 : offsetY + 24, offsetY + height - 160));
        dialog.style.setProperty('--notice-left', `${left}px`);
        dialog.style.setProperty('--notice-top', `${top}px`);
        dialog.style.setProperty('--notice-available-height', `${Math.max(100, offsetY + height - top - 12)}px`);
        dialog.style.setProperty('--notice-pointer-left', `${Math.max(22, Math.min(center - left, panelWidth - 22))}px`);
        dialog.classList.toggle('has-bell-anchor', !!bounds && bounds.bottom + 14 === top);
    }
    let positioning = false;
    function schedulePosition() {
        if (positioning) return;
        positioning = true;
        window.requestAnimationFrame(() => { positioning = false; positionPreview(); });
    }
    function error(text = '') { errorBox.textContent = text; errorBox.hidden = !text; }
    function dateText(value) {
        const utc = /(?:Z|[+-]\d\d:\d\d)$/.test(value) ? value : value + 'Z';
        const d = new Date(utc);
        return Number.isNaN(d.getTime()) ? '' : d.toLocaleString();
    }
    function element(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    }
    function badge() {
        document.querySelectorAll('[data-pending-notification-count]').forEach(node => { node.textContent = pending.length > 99 ? '99+' : String(pending.length); node.hidden = !pending.length; });
        document.querySelectorAll('[data-open-notification-reader]').forEach(node => node.setAttribute('aria-label', `Notifications, ${pending.length} awaiting acknowledgement`));
        count.textContent = pending.length ? `${pending.length} awaiting acknowledgement` : 'You are all caught up';
        closeButton.hidden = pending.length > 0;
        help.textContent = pending.length ? 'Read and acknowledge your notifications to continue.' : 'No notifications need acknowledgement.';
    }
    function compact() {
        selected = null; selectedRevision = null; detail.hidden = true; previews.hidden = false; dialog.classList.remove('is-expanded');
        previews.replaceChildren();
        if (!pending.length) previews.append(element('p', 'text-muted py-3', 'You have no pending notifications.'));
        for (const notice of pending) {
            const card = element('article', 'qms-notice-card');
            const heading = element('div', 'qms-notice-card-heading');
            if (!notice.isRead) { const dot = element('span', 'qms-notice-card-dot'); dot.setAttribute('aria-label', 'Unread'); heading.append(dot); }
            heading.append(element('h3', '', notice.title));
            if (notice.revision > 0) card.append(element('span', 'qms-notice-update-badge', 'Notification Updated'));
            card.append(heading, element('p', '', notice.previewText), element('time', '', dateText(notice.dateUtc)));
            const button = element('button', 'btn btn-sm btn-outline-primary', 'Read more');
            button.type = 'button'; button.addEventListener('click', () => read(notice.id)); card.append(button); previews.append(card);
        }
        badge(); positionPreview();
    }
    function open(force = false) {
        if (!force && document.querySelector('dialog[open]:not(#notification-reader)')) return;
        if (!dialog.open) { opener = document.activeElement; dialog.showModal(); document.body.classList.add('qms-notice-overlay-open'); }
        positionPreview();
    }
    function close() {
        dialog.close(); document.body.classList.remove('qms-notice-overlay-open');
    }
    async function request(url, options = {}) {
        const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', ...options, headers: { Accept: 'application/json', ...options.headers } });
        if (response.redirected || response.status === 401) throw new Error('Your session expired. Sign out and sign in again.');
        if (response.status === 404) { const e = new Error('This notification is no longer available.'); e.missing = true; throw e; }
        if (response.status === 409) {
            const e = new Error('This notification was updated. Open it again to review the latest version.');
            e.updated = true; throw e;
        }
        if (!response.ok || !(response.headers.get('content-type') || '').includes('application/json')) throw new Error('Unable to load your notification. Please try again.');
        return response.json();
    }
    async function refresh(force = false) {
        forceOpen ||= force;
        if (refreshing || busy || (document.hidden && !force)) return;
        refreshing = true; const ticket = revision;
        try {
            const result = await request(dialog.dataset.pendingUrl);
            if (ticket !== revision) return;
            const next = result.notifications || [];
            const changed = JSON.stringify(next) !== JSON.stringify(pending);
            pending = next; error(); badge();
            if (selected !== null && !pending.some(n => n.id === selected && n.revision === selectedRevision)) compact();
            else if (!selected && (changed || forceOpen || !dialog.open)) compact();
            if (pending.length || forceOpen) open(forceOpen);
            else if (dialog.open) close();
        } catch (e) { if (dialog.open || forceOpen) { error(e.message); open(true); } }
        finally { refreshing = false; forceOpen = false; }
    }
    function postData(id, noticeRevision) {
        const data = new URLSearchParams({ id: String(id), __RequestVerificationToken: form.querySelector('[name="__RequestVerificationToken"]').value });
        if (noticeRevision !== undefined) data.set('revision', String(noticeRevision));
        return data;
    }
    async function read(id) {
        if (busy) return;
        busy = true; revision++; error();
        try {
            const result = await request(dialog.dataset.readUrl, { method: 'POST', body: postData(id) });
            selected = result.id; selectedRevision = result.revision;
            const notice = pending.find(n => n.id === id && n.revision === selectedRevision);
            if (notice) notice.isRead = true;
            updatedLabel.hidden = !(selectedRevision > 0);
            title.textContent = result.title; date.textContent = dateText(result.dateUtc); message.textContent = result.message;
            form.querySelector('[name="id"]').value = String(selected);
            form.querySelector('[name="revision"]').value = String(selectedRevision);
            previews.hidden = true; detail.hidden = false; dialog.classList.add('is-expanded'); message.focus();
        } catch (e) { error(e.message); if (e.missing) { busy = false; await refresh(); } }
        finally { busy = false; }
    }
    form.addEventListener('submit', async event => {
        event.preventDefault(); if (busy || selected === null) return;
        busy = true; revision++; ackButton.disabled = true; ackButton.textContent = 'Acknowledging…'; error();
        try {
            const result = await request(form.action, { method: 'POST', body: postData(selected, selectedRevision) });
            pending = result.notifications || []; compact();
            document.dispatchEvent(new CustomEvent('notifications:acknowledged', { detail: { pending: pending.length } }));
            if (!pending.length) { close(); if (window.location.pathname.toLowerCase().endsWith('/notifications')) window.location.reload(); }
            else previews.querySelector('button')?.focus();
        } catch (e) {
            if (e.missing || e.updated) { busy = false; compact(); await refresh(); }
            if (dialog.open) error(e.message);
        }
        finally { busy = false; ackButton.disabled = false; ackButton.textContent = 'Acknowledge'; }
    });
    dialog.querySelector('[data-notification-back]').addEventListener('click', () => { if (!busy) { error(); compact(); previews.querySelector('button')?.focus(); } });
    closeButton.addEventListener('click', () => { if (!pending.length) close(); });
    dialog.addEventListener('cancel', event => { if (pending.length || busy) event.preventDefault(); });
    dialog.addEventListener('close', () => { document.body.classList.remove('qms-notice-overlay-open'); if (opener?.isConnected) opener.focus(); });
    document.addEventListener('click', event => { if (event.target.closest('[data-open-notification-reader]')) refresh(true); });
    document.addEventListener('visibilitychange', () => refresh());
    window.addEventListener('pageshow', () => refresh());
    window.addEventListener('resize', schedulePosition, { passive: true });
    window.addEventListener('scroll', schedulePosition, { passive: true, capture: true });
    window.visualViewport?.addEventListener('resize', schedulePosition, { passive: true });
    window.visualViewport?.addEventListener('scroll', schedulePosition, { passive: true });
    if (typeof ResizeObserver !== 'undefined') {
        const observer = new ResizeObserver(schedulePosition);
        document.querySelectorAll('[data-open-notification-reader]').forEach(bell => observer.observe(bell));
    }
    window.setInterval(() => refresh(), 30000);
    compact(); refresh();
})();
