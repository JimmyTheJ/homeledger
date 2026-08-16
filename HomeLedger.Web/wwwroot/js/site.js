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

    document.body.addEventListener("htmx:afterSwap", function () {
        var banner = document.getElementById("import-busy");
        if (banner) {
            banner.hidden = true;
        }
    });

    // --- Transaction list: collapsible receipts and row multi-select ---

    var lastSelectedIndex = null;

    function transactionTable() {
        return document.querySelector(".tx-table");
    }

    function rowCheckboxes(table) {
        return Array.prototype.slice.call(table.querySelectorAll("[data-row-select]"));
    }

    function refreshSelection() {
        var table = transactionTable();
        var bar = document.getElementById("bulk-bar");
        if (!table || !bar) {
            return;
        }

        var boxes = rowCheckboxes(table);
        var selected = boxes.filter(function (box) {
            var group = box.closest("tbody");
            if (group) {
                group.classList.toggle("is-selected", box.checked);
            }
            return box.checked;
        }).length;

        var count = bar.querySelector("[data-bulk-count]");
        if (count) {
            count.textContent = selected + " selected";
        }
        bar.hidden = selected === 0;

        var selectAll = table.querySelector("[data-select-all]");
        if (selectAll) {
            selectAll.checked = selected > 0 && selected === boxes.length;
            selectAll.indeterminate = selected > 0 && selected < boxes.length;
        }
    }

    function toggleReceipt(toggle) {
        var group = toggle.closest("tbody");
        if (!group) {
            return;
        }

        var expanded = toggle.getAttribute("aria-expanded") === "true";
        toggle.setAttribute("aria-expanded", expanded ? "false" : "true");
        group.querySelectorAll(".receipt-line-row").forEach(function (row) {
            row.hidden = expanded;
        });
    }

    document.addEventListener("click", function (event) {
        if (!(event.target instanceof Element)) {
            return;
        }

        var toggle = event.target.closest("[data-receipt-toggle]");
        if (toggle) {
            toggleReceipt(toggle);
            return;
        }

        if (event.target.closest("[data-bulk-clear]")) {
            var cleared = transactionTable();
            if (cleared) {
                rowCheckboxes(cleared).forEach(function (box) {
                    box.checked = false;
                });
            }
            lastSelectedIndex = null;
            refreshSelection();
            return;
        }

        var box = event.target.closest("[data-row-select]");
        if (!box) {
            return;
        }

        var table = transactionTable();
        if (!table) {
            return;
        }

        var boxes = rowCheckboxes(table);
        var index = boxes.indexOf(box);

        // Shift-click extends the selection from the previously clicked row.
        if (event.shiftKey && lastSelectedIndex !== null && index > -1) {
            var start = Math.min(index, lastSelectedIndex);
            var end = Math.max(index, lastSelectedIndex);
            for (var i = start; i <= end; i++) {
                boxes[i].checked = box.checked;
            }
        }

        lastSelectedIndex = index;
        refreshSelection();
    });

    document.addEventListener("change", function (event) {
        if (!(event.target instanceof Element) || !event.target.matches("[data-select-all]")) {
            return;
        }

        var table = transactionTable();
        if (table) {
            var checked = event.target.checked;
            rowCheckboxes(table).forEach(function (box) {
                box.checked = checked;
            });
        }

        lastSelectedIndex = null;
        refreshSelection();
    });

    // Rows removed by an HTMX delete should stop counting towards the selection.
    document.addEventListener("htmx:afterSwap", refreshSelection);
})();
