(function () {
    const root = document.documentElement;

    let preference =
        root.getAttribute("data-pmt-theme") ||
        root.getAttribute("data-theme-preference") ||
        "light";

    preference = preference.toLowerCase();

    if (!["light", "dark", "system"].includes(preference)) {
        preference = "light";
    }

    const media = window.matchMedia
        ? window.matchMedia("(prefers-color-scheme: dark)")
        : null;

    function apply() {
        const dark = preference === "dark" ||
            (preference === "system" && media && media.matches);

        const mode = dark ? "dark" : "light";

        root.setAttribute("data-pmt-theme", preference);
        root.setAttribute("data-pmt-color", mode);
        root.setAttribute("data-bs-theme", mode);

        root.setAttribute("data-theme-preference", preference);
        root.setAttribute("data-theme-effective", mode);

        const meta = document.querySelector('meta[name="theme-color"]');

        if (meta) {
            meta.setAttribute(
                "content",
                dark ? "#111317" : "#102c54"
            );
        }
    }

    apply();

    if (media) {
        if (media.addEventListener) {
            media.addEventListener("change", apply);
        } else if (media.addListener) {
            media.addListener(apply);
        }
    }
})();