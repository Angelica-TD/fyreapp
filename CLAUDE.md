# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

### Backend (.NET)
```bash
dotnet build                    # Build the project
dotnet run                      # Run the app (dev mode)
dotnet test                     # Run all tests
dotnet test --filter "ClassName.MethodName"  # Run a single test
dotnet ef migrations add <Name> # Add a new EF Core migration
dotnet ef database update       # Apply pending migrations
```

### Frontend (React + SCSS)
```bash
npm run build        # Build CSS and React bundles
npm run react:dev    # Watch mode for React (Vite)
npm run css:watch    # Watch mode for SCSS
```

There is no standalone frontend dev server — React components are embedded in Razor views and served by ASP.NET Core.

## Architecture

FyreApp is an **ASP.NET Core MVC (.NET 10) application** with a **hybrid frontend**: Razor views for most UI, with React components (built via Vite) embedded as islands for interactive search/filtering.

### Backend Structure

- **Controllers/** — MVC controllers handling both HTML views and JSON API endpoints (prefixed `/api/`). JSON endpoints return data for React components.
- **Services/** — Business logic layer, organized by domain (Clients, Tasks, Sites, ServiceQuotes, Email). Controllers depend only on service interfaces, not on DbContext directly.
- **Models/** — EF Core entity models (mapped to PostgreSQL). All timestamps use `timestamptz` with UTC defaults.
- **Data/** — `AppDbContext`, migrations, and seed data (`DbInitialiser`, `IdentitySeed`).
- **Dtos/** — Data transfer objects for API responses and form models.
- **Hubs/** — SignalR hub for streaming CSV/Excel import progress to the client.
- **Infrastructure/** — Cross-cutting concerns: breadcrumb filter (applied globally), custom tag helpers, `UiFieldAttribute` for field ordering.
- **Auth/** — `DevAuthHandler` bypasses authentication when `Auth:Enabled = false` in config (development only).

### Frontend Structure

- **FyreFrontend/react/** — React components (`ClientSearch`, `TaskSearch`, plus shared components). Each component is a separate Vite entry point built to `wwwroot/js/react/`.
- **FyreFrontend/scss/** — Bootstrap 5 SCSS customizations, compiled to `wwwroot/css/`.
- React components use local `useState`/`useEffect` and the Fetch API — no Redux, Zustand, or React Query.
- List pages mirror Uptick's (same default filters, filter count, reset, server-side paging). Properties, Assets, Remarks, Reports, Routines, Contractors, Suppliers and Users (Uptick users, each with its linked FyreApp login) are server-rendered Razor with query-string filters (`Views/Shared/_ListPager.cshtml`, `ViewModels/Lists`); the Clients and Tasks lists are React islands using `shared/ListControls.jsx` against paged `/api/clients` and `/api/tasks`. Every list has Download (CSV of the current filters) and, for admins, row / page / "select all matching" selection with an Edit modal (`wwwroot/js/list-selection.js` + `_SelectionToolbar` / `_BulkFormFields` for Razor; `useSelection` / `SelectionBar` for React). Each list service has one `Filtered` query shared by the list, its download and select-all.
- Components mount on `#[component-name]-root` divs in Razor views.

### Key Domain Models

| Entity | Notes |
|--------|-------|
| `Client` | Core record; has unique Name and ExternalId |
| `Property` | Property/location belonging to a Client (was `Site`; services and view models still sit under `Sites/`) |
| `Asset` | Equipment at a Property; N:N with `AssetType` |
| `MaintenanceSchedule` | Recurring maintenance plan linking Asset + Property + interval |
| `MaintenanceHistory` | Audit log of completed maintenance events |
| `ClientTask` | Work items with status, priority, and due date; assigned to an `UptickUser` (technician) |
| `ServiceQuote` | Proposals with a `ClientToken` (Guid) for public/unauthenticated viewing |
| `ApplicationUser` | A FyreApp login (`IdentityUser` + FirstName, LastName, IsActive), linked to the `UptickUser` it takes its name and email from |

### Authentication

- ASP.NET Identity with PostgreSQL backing store.
- 5 failed login attempts triggers a 15-minute lockout.
- API/AJAX requests get `401`/`403` responses; UI requests redirect to the login page (path: `/`).
- Dev bypass: set `Auth:Enabled: false` in `appsettings.Development.json` — all requests authenticate as Admin via `DevAuthHandler`.

### Database

- **PostgreSQL** via Npgsql EF Core provider.
- Dev connection: `Host=localhost;Port=5432;Database=fyre;Username=postgres;Password=admin`.
- Migrations auto-apply on startup in development (`context.Database.Migrate()`).
- Seed data runs via `DbInitialiser.Seed()` and `IdentitySeed.SeedAdminAsync()`.

### Configuration Sections

| Key | Purpose |
|-----|---------|
| `ConnectionStrings:DefaultConnection` | PostgreSQL DSN |
| `Auth:Enabled` | Set to `false` in dev to bypass login |
| `Email` | SMTP settings (MailKit); dev default targets smtp4dev on localhost:25 |
| `GoogleMaps:BrowserKey` / `GoogleMaps:ApiKey` | Maps integration keys |
| `Uptick:Api` | Uptick API migration (`docs/uptick-migration.md`): `BaseUrl` (pinned API version), pace and page size in `appsettings.json`; `ClientId`, `ClientSecret`, `Username`, `Password` are secrets (user secrets in dev, `Uptick__Api__*` in `/etc/fyreapp.env` in prod) |

### Uptick API migration

`Services/Uptick/` copies Uptick's data into FyreApp through its REST API, read-only (token `scope=read`, only `GET` requests), at most 60 requests/minute. Admin page: `/UptickMigration` (Full / Refresh runs, per-endpoint counts, run reports). Every endpoint lands verbatim in `UptickRecord` (jsonb); mapping into FyreApp's own tables is added phase by phase (see the design doc): one `IUptickMapper` per endpoint in `Services/Uptick/Mapping/`, registered in run order in `Program.cs`, run after every Full / Refresh run and by Re-map (no API calls). Mapped so far: `clients`, `properties`, `clientcontacts`, `propertycontacts`, `users`, `assettypes`, `assettypevariants`, `assets` (each row's `UptickId`; `UptickEntityMapper` is the base) and `timelineevents` → `TimelineEvent`. Edits made in FyreApp to a migrated record's fields are recorded as `LocalEdit` on save (`Data/LocalEditLog.cs`) and kept by later runs until the field changes in Uptick; mappers save with `RecordLocalEdits = false`. The CSV/Excel importers for mapped entities are off (`UptickImportService.FromApi`); the rest remain until each entity is mapped from the API.

### Testing

- xUnit with Moq; EF Core In-Memory provider for data-layer tests.
- Test project: `FyreApp.Tests/`.
