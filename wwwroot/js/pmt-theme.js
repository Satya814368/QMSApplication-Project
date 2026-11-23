(() => {
    const root = document.documentElement;
    if (root.dataset.pmtTheme !== "system" || !window.matchMedia) return;
    const query = window.matchMedia("(prefers-color-scheme: dark)");
    function apply() {
        const mode = query.matches ? "dark" : "light";
        root.dataset.pmtColor = mode;
        root.dataset.bsTheme = mode;
        document.querySelector('meta[name="theme-color"]')?.setAttribute("content", query.matches ? "#111317" : "#102c54");
    }
    apply();
    if (query.addEventListener) query.addEventListener("change", apply);
    else if (query.addListener) query.addListener(apply);
})();
