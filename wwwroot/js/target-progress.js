(() => {
    const form = document.getElementById('progress-form');
    if (!form) return;
    const action = document.getElementById('UpdateAction');
    const selected = () => form.querySelectorAll('.target-select:checked').length;
    function refresh() {
        document.getElementById('selection-count').textContent = `${selected()} targets selected`;
        form.querySelectorAll('section').forEach(section => {
            const boxes = [...section.querySelectorAll('.target-select')];
            const toggle = section.querySelector('.select-language');
            toggle.checked = boxes.length > 0 && boxes.every(x => x.checked);
            toggle.indeterminate = boxes.some(x => x.checked) && !toggle.checked;
            toggle.disabled = boxes.length === 0;
        });
    }
    form.querySelectorAll('.select-language').forEach(toggle => toggle.addEventListener('change', () => {
        toggle.closest('section').querySelectorAll('.target-select').forEach(x => x.checked = toggle.checked); refresh();
    }));
    form.querySelectorAll('.target-select').forEach(x => x.addEventListener('change', refresh));
    function changeAction() {
        form.querySelectorAll('[data-action]').forEach(group => {
            const active = group.dataset.action === action.value;
            group.hidden = !active;
            group.querySelectorAll('input, select, textarea').forEach(input => input.disabled = !active);
        });
    }
    action.addEventListener('change', changeAction); changeAction(); refresh();
    form.addEventListener('submit', event => {
        if (!selected()) { event.preventDefault(); alert('Select at least one target.'); return; }
        if (!confirm(`Apply this update to ${selected()} selected target(s)?`)) event.preventDefault();
    });
})();
