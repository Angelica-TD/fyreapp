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
