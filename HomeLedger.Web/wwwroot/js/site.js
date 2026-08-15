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

    function busyMessageForForm(form) {
        var custom = form.getAttribute("data-busy-message");
        if (custom) {
            return custom;
        }

        var receipts = form.querySelector('input[name="ReceiptImages"]');
        if (receipts && receipts.files && receipts.files.length > 0) {
            var count = receipts.files.length;
            return count === 1
                ? "Uploading 1 receipt. AI extraction will continue in the background after this upload."
                : "Uploading " + count + " receipts. AI extraction will continue in the background after this upload — often a minute or more per image.";
        }

        var file = form.querySelector('input[name="File"]');
        if (file && file.files && file.files.length > 0 && /\.pdf$/i.test(file.files[0].name)) {
            return "Extracting this PDF with AI. This can take several minutes. Please leave this page open until it finishes.";
        }

        return "Importing…";
    }

    document.addEventListener("submit", function (event) {
        var form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.hasAttribute("data-busy-submit")) {
            return;
        }

        if (form.dataset.busyArmed === "true") {
            event.preventDefault();
            return;
        }

        form.dataset.busyArmed = "true";

        var message = busyMessageForForm(form);
        var banner = document.getElementById("import-busy");
        var bannerMessage = document.getElementById("import-busy-message");
        if (banner && bannerMessage) {
            bannerMessage.textContent = message;
            banner.hidden = false;
            banner.scrollIntoView({ block: "nearest" });
        }

        form.querySelectorAll("button[type='submit'], input[type='submit']").forEach(function (button) {
            button.setAttribute("aria-busy", "true");
        });

        form.setAttribute("aria-busy", "true");

        window.setTimeout(function () {
            form.querySelectorAll("button[type='submit'], input[type='submit']").forEach(function (button) {
                button.disabled = true;
            });
        }, 0);
    });
})();
