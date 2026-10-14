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

    // =========================================================
    // UPLOAD SIZE CHECK
    // File inputs with data-max-bytes refuse files over the server's limit before uploading;
    // otherwise the server cuts the upload off and the browser only shows a bare HTTP 400.
    // =========================================================
    document.querySelectorAll("input[type=file][data-max-bytes]").forEach(function (input) {
        input.addEventListener("change", function () {
            const max = Number(input.dataset.maxBytes);
            const tooBig = Array.from(input.files).find(f => f.size > max);
            const mb = n => (n / 1000000).toFixed(1) + " MB";
            input.setCustomValidity(tooBig
                ? `${tooBig.name} is ${mb(tooBig.size)}; the limit is ${mb(max)}. Split the export or export as XLSX (smaller than CSV).`
                : "");
            input.reportValidity();
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
