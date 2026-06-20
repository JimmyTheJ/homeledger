(function () {
    var storageKey = "homeledger-theme";

    function preferredTheme() {
        var stored = localStorage.getItem(storageKey);
        if (stored === "dark" || stored === "light") {
            return stored;
        }

        return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
    }

    function applyTheme(theme) {
        document.documentElement.dataset.theme = theme;
        var toggle = document.getElementById("theme-toggle");
        if (toggle) {
            toggle.textContent = theme === "dark" ? "Light mode" : "Dark mode";
            toggle.setAttribute("aria-pressed", theme === "dark" ? "true" : "false");
        }
    }

    applyTheme(preferredTheme());

    document.addEventListener("DOMContentLoaded", function () {
        var toggle = document.getElementById("theme-toggle");
        if (!toggle) {
            return;
        }

        toggle.addEventListener("click", function () {
            var next = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
            localStorage.setItem(storageKey, next);
            applyTheme(next);
        });
    });
})();
