document.addEventListener("DOMContentLoaded", function () {

    // =========================================================
    // SIDEBAR TOGGLE (desktop)
    // Toggles .sidebar-collapsed on the <aside id="appSidebar">
    // State is persisted in localStorage so it survives page loads.
    // =========================================================
    const sidebar = document.getElementById("appSidebar");

    const toggleBtn = sidebar ? sidebar.querySelector("#sidebarToggle") : null;

    if (sidebar && toggleBtn) {
        // Restore saved state
        if (localStorage.getItem("sidebarCollapsed") === "true") {
            sidebar.classList.add("sidebar-collapsed");
        }

        toggleBtn.addEventListener("click", function () {
            const isCollapsed = sidebar.classList.toggle("sidebar-collapsed");
            localStorage.setItem("sidebarCollapsed", isCollapsed);
        });
    }


    // =========================================================
    // PHONES: list filter panels start closed (open, they fill the screen); the Filters button opens them
    // =========================================================
    if (window.matchMedia("(max-width: 767.98px)").matches) {
        // Wide tables (5+ columns) read as cards: each cell labelled with its column's heading (site.css .table-stack).
        // Not the routines grid, which scrolls across the months.
        document.querySelectorAll(".app-main table.table:not(.routine-grid):not(.table-stack)").forEach(function (table) {
            var heads = table.tHead ? Array.prototype.slice.call(table.tHead.rows[0] ? table.tHead.rows[0].cells : []) : [];
            if (heads.length < 5 || heads.some(function (h) { return h.colSpan > 1; })) return;
            var labels = heads.map(function (h) { return h.textContent.trim(); });
            Array.prototype.forEach.call(table.tBodies, function (body) {
                Array.prototype.forEach.call(body.rows, function (row) {
                    if (row.cells.length !== labels.length) return;
                    Array.prototype.forEach.call(row.cells, function (cell, i) { cell.setAttribute("data-label", labels[i]); });
                });
            });
            table.classList.add("table-stack");
        });

        document.querySelectorAll(".js-filters.collapse.show").forEach(function (panel) {
            panel.classList.remove("show");
            document.querySelectorAll('[data-bs-target="#' + panel.id + '"]').forEach(function (b) {
                b.setAttribute("aria-expanded", "false");
                b.classList.add("collapsed");
            });
        });
    }

    document.querySelectorAll(".has-subnav").forEach(function (item) {
        const activeLink = item.querySelector(".sidebar-link.active");
        if (activeLink) {
            item.classList.add("subnav-open");
        }
    });

    // =========================================================
    // SIDEBAR GROUPS (Customer data, People, ...)
    // The group holding the current page is opened by the server; the others open and close on click
    // and are remembered in localStorage. The sidebar renders twice (desktop + mobile), so both follow.
    // =========================================================
    const groupsKey = "sidebarGroupsOpen";
    let openGroups = [];
    try { openGroups = JSON.parse(localStorage.getItem(groupsKey) || "[]"); } catch { openGroups = []; }

    function setGroup(key, open) {
        document.querySelectorAll(`.sidebar-group[data-group="${key}"]`).forEach(function (group) {
            group.classList.toggle("open", open);
            group.querySelector(".sidebar-group-toggle")?.setAttribute("aria-expanded", open);
        });
    }

    openGroups.forEach(key => setGroup(key, true));

    document.querySelectorAll(".sidebar-group-toggle").forEach(function (toggle) {
        toggle.addEventListener("click", function () {
            const key = toggle.closest(".sidebar-group").dataset.group;
            const open = !toggle.closest(".sidebar-group").classList.contains("open");
            setGroup(key, open);
            openGroups = open ? [...new Set([...openGroups, key])] : openGroups.filter(k => k !== key);
            try { localStorage.setItem(groupsKey, JSON.stringify(openGroups)); } catch { }
        });
    });

    const offcanvasEl = document.getElementById("offcanvasNav");
    if (offcanvasEl) {
        offcanvasEl.addEventListener("click", function (e) {
            const link = e.target.closest("a.sidebar-link");
            if (link && !link.classList.contains("disabled")) {
                const bsOffcanvas = bootstrap.Offcanvas.getInstance(offcanvasEl);
                if (bsOffcanvas) bsOffcanvas.hide();
            }
        });
    }

});
