(() => {
    const editor = document.getElementById('language-editor');
    if (!editor) return;
    const hidden = document.getElementById('TasksJson');
    const rows = document.getElementById('language-rows');
    const addButton = document.getElementById('add-language');
    const numbers = value => value.trim().split(/[,;\s]+/).filter(Boolean);
    function sync() {
        const tasks = [...rows.querySelectorAll('.language-row')].map(row => {
            const targets = row.querySelector('.target-numbers').value;
            row.querySelector('.row-target-count').textContent = numbers(targets).length;
            return { language: row.querySelector('.language-name').value,
                targets, assignedUserId: Number(row.querySelector('.task-assignee').value) };
        });
        hidden.value = JSON.stringify(tasks);
        document.getElementById('language-count').textContent = tasks.length;
        document.getElementById('target-count').textContent = tasks.reduce((n, t) => n + numbers(t.targets).length, 0);
        addButton.disabled = tasks.length >= 40;
    }
    function add(task = {}) {
        if (rows.children.length >= 40) return;
        const row = document.getElementById('language-template').content.firstElementChild.cloneNode(true);
        row.querySelector('.language-name').value = task.language ?? task.Language ?? '';
        row.querySelector('.target-numbers').value = task.targets ?? task.Targets ?? '';
        row.querySelector('.task-assignee').value = task.assignedUserId ?? task.AssignedUserId ?? '';
        row.querySelector('.remove-language').addEventListener('click', () => { row.remove(); sync(); });
        row.addEventListener('input', sync); row.addEventListener('change', sync);
        rows.appendChild(row); sync();
    }
    let draft = [];
    try { const parsed = JSON.parse(hidden.value); if (Array.isArray(parsed)) draft = parsed.filter(t => t && typeof t === 'object'); } catch { /* show a clean editor */ }
    draft.forEach(add);
    if (!rows.children.length) add();
    addButton.addEventListener('click', () => { add(); rows.lastElementChild.querySelector('input').focus(); });
    hidden.form.addEventListener('submit', sync);
})();
