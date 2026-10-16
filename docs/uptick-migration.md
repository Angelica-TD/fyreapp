# Uptick → FyreApp migration — design

Goal: **all of Uptick's data in FyreApp**, so FyreApp can replace Uptick. Data comes through Uptick's REST API
instead of CSV/Excel exports uploaded on the Imports page.

This is a migration, not a live sync. A migration run copies everything; it can be re-run to refresh FyreApp
with what changed in Uptick since, as often as useful while FyreApp is being built, and a final run happens at
cutover. Nothing runs on a timer.

Status: design, not built. Findings below were measured against the live API on 15 Oct 2026.

## 1. What the API gives us

| | |
|---|---|
| Base URL | `https://fyrepower.onuptick.com/api/v2.15/` (the root lists all 199 endpoints) |
| Versions | Uptick answers any `v2.x`, even `v2.99`, but fields differ by version (v2.8 assets have `standard`, v2.15 doesn't) and Uptick retires old minor versions. FyreApp pins one version in config: **v2.15**, the one Airbyte's maintained Uptick connector uses. |
| Format | JSON:API: `data[].id`, `attributes`, `relationships` (`{type, id}`), `included`, `meta.pagination.count`, `links.next` |
| Auth | OAuth2 **password grant** only, at `/api/oauth2/token/` (client ID + secret + an Uptick login). Tokens last 10 h, refresh token supplied. Requesting `scope=read` returns a **read-only token**, which FyreApp always uses. Data visible = what that Uptick login can see; `403` = the login lacks permission. |
| Paging | `page[limit]` up to at least 5,000, `page[offset]`. A 5,000-row page takes ~0.5–2.5 s (timeline events: ~3.6 MB). |
| Sparse fields | `fields[Asset]=id,deleted` returns only those attributes. |
| Changed since | `updatedsince=<ISO time>` filters on the record's `updated`. It narrowed results on most endpoints when tested, but Airbyte only relies on it for 13 streams, so a refresh only trusts it for endpoints it has verified (§4.2). `sort` and `created__gte` are ignored; results come newest first. |
| Schema | `OPTIONS /api/v2.15/<endpoint>/` describes every field: type, required, read-only, and for a relationship the resource it points to. |
| Rate limit | Not published. Uptick states it can **revoke API access without notice** for applications that break its limits. A fast crawl got `429` with `Retry-After` (10–32 s) after ~200 requests. FyreApp keeps to **60 requests/minute** (§3.1). |
| Deleted records | Not returned, and no filter brings them back. A full run finds them by what's missing (§4.3). |

Size: 108 endpoints hold data, ~2 million rows in all. Over half is the Timeline (~1.1 M events); the rest is led
by service tasks (~140 k), report items (~117 k), correspondence links (~113 k), routines (~72 k), remark events,
subtasks, correspondence, document files, assets (~25 k), invoice lines, tasks, invoices, remarks, reports,
dispatches and task sessions (~20 k each). 81 endpoints are empty for FyreApp's account (projects, purchase
orders, IoT, inbox, etc.); a handful are forbidden to the current API login (`logins`, `constance`,
`standardbundles`, `customsqlreports`, …).

**A Full run is ~620 requests: about 10–12 minutes at 60 requests/minute**, plus files (§8). That is ~500 data
pages (217 of them the Timeline; pages overlap slightly, §3.1), one page for each of the 81 empty endpoints and ~40
`updatedsince` verification queries. The client-side budget (§3.1) means a run can never exceed 60 requests in a minute.

## 2. Principles

1. **Read-only towards Uptick.** Nothing FyreApp does changes anything in Uptick: a read-only token, and an API
   client that can only send `GET` and `OPTIONS`.
2. **Copy everything raw first, then map.** Every record of every endpoint lands verbatim in one table. Typed
   FyreApp tables (Client, Site, Asset, …) are built from it. All of Uptick's data is in FyreApp after the first
   run, including data no screen uses yet; mapping is added screen by screen without downloading again, and a
   mapping bug is fixed by re-mapping.
3. **Link by Uptick ID.** The API gives real IDs for every relationship, so FyreApp stops matching on refs and
   names (`PropertyRef`, `ClientName`, `TaskExternalId` strings) and uses foreign keys.
4. **Re-runnable and resumable.** Any run can be repeated or interrupted; writes are keyed on Uptick IDs.
5. **Verifiable.** Every run ends with a reconciliation: what Uptick has vs. what FyreApp has (§4.4). That report
   is what says the migration is complete.
6. **Times stored in UTC.** The API returns local times (`+10:00` / `+11:00`).

## 3. Architecture

```
     Uptick API (v2.15, read-only)
                  │  OAuth2 scope=read, 60 req/min, Retry-After
          ┌───────▼────────┐
          │ UptickApiClient │   Services/Uptick/Api
          └───────┬────────┘
                  │  JSON:API records
          ┌───────▼────────┐      UptickEndpointState (per endpoint: last run, counts, errors)
          │ UptickMigrator  │──────────────────────────────────────────
          └───────┬────────┘
                  │  upsert raw            ┌──────────────────────┐
                  ├───────────────────────►│ UptickRecord (copy)   │  every endpoint, verbatim jsonb
                  │                        └──────────┬───────────┘
                  │  changed IDs                      │
          ┌───────▼────────┐  reads copy              │
          │    Mappers      │◄─────────────────────────┘
          └───────┬────────┘
                  │  upsert typed rows, FKs by UptickId
                  ▼
     Client · Site · Asset · ClientTask · Defect · ServiceReport · TimelineEntry · Invoice · …
```

### 3.1 UptickApiClient

- Typed `HttpClient` (`IHttpClientFactory`), settings from configuration section `Uptick:Api`: `BaseUrl`
  (including the pinned version), `ClientId`, `ClientSecret`, `Username`, `Password`, `MaxRequestsPerMinute`
  (default 60), `MaxConcurrency` (default 2), `PageSize` (default 5,000). Prod values in `/etc/fyreapp.env`, dev values in user secrets;
  never in `appsettings*.json` (the repo is public).
- **Read-only:** token requested with `scope=read`; no method sends anything but `GET` / `OPTIONS`.
- Token: password grant on first use, refresh token after that, one re-login when refresh fails (e.g. the
  password changed), then the run fails with a clear "check Uptick API login" error. Held in memory; never logged.
- **Request budget:** one token bucket for the whole run: `MaxRequestsPerMinute`, at most `MaxConcurrency` in
  flight. After any `429`, the rest of the run lowers the rate and drops `PageSize` to 1,000: lighter requests
  for Uptick, at the cost of more of them (a Full run at 1,000 rows/page is ~2,300 requests, ~40 min).
- **Retries:** on `429` with `Retry-After`, wait that long and retry; if `Retry-After` is 30 minutes or more,
  stop the run and report it (as Airbyte does). On `5xx`, timeouts or `429` without the header, exponential
  backoff. At most 5 retries per request, then that endpoint fails for this run.
- `IAsyncEnumerable<UptickResource> ListAsync(endpoint, updatedSince?, fields?)` follows `links.next`.
- Each page starts 50 rows before the previous one ended, so a record deleted in Uptick mid-run can't shift the
  next page and make a record be skipped (and then wrongly marked deleted).
- No `Accept` header: the API root answers `406` to `application/vnd.api+json`; without one, every endpoint
  returns JSON:API.
- Field changes are noticed from the records themselves: each Full run stores the attribute and relationship
  names of an endpoint's first record and flags names added or removed since the previous run. (`OPTIONS` also
  describes fields, with types and relationship targets, which helps when writing mappers, but costs a request
  per endpoint, so runs don't use it.)

### 3.2 Raw copy: `UptickRecord`

One table for every endpoint:

| Column | |
|---|---|
| `Resource` (text) + `UptickId` (bigint) | primary key, e.g. (`assets`, 41642) |
| `Type` | JSON:API type, e.g. `Asset` |
| `Attributes` (jsonb), `Relationships` (jsonb) | as returned |
| `CreatedUtc`, `UpdatedUtc` | from the record's `created` / `updated` when it has them |
| `FetchedUtc` | when FyreApp last received it |
| `DeletedUtc` | set when the record disappears from Uptick (§4.3); never hard-deleted |
| `MappedUtc` | when mappers last consumed it; null = pending |

Indexes: (`Resource`, `UpdatedUtc`), (`Resource`, `MappedUtc`) where null, and a GIN index on `Relationships`
for lookups like "all timeline events of asset 41642" in the raw data.

At ~2 M rows this is a modest Postgres table. It stays after cutover as the archive of exactly what Uptick held.

### 3.3 `UptickEndpointState`

Per endpoint: `Resource`, `Enabled`, `IncrementalVerified`, `LastFullRunUtc`, `LastRefreshUtc`, `Cursor`
(latest `updated` seen), `UptickCount`, `CopiedCount`, `MappedCount`, `LastError`, `Schema` (jsonb, last seen).
A run lease prevents two runs at once.

### 3.4 Mappers

One class per typed FyreApp entity, `IUptickMapper<TEntity>`:

- `Resource` it reads, and `DependsOn` (e.g. assets depend on properties, asset types, variants, users). Run
  order is a topological sort of these. A relationship that can't be resolved yet stays pending and is retried
  at the end of the run, then reported, rather than failing the batch.
- `Map(UptickRecord record, MappingContext ctx)` sets the entity's fields and resolves relationships to FyreApp
  IDs through `ctx` (a cache of `UptickId → Id` per entity).
- Records deleted in Uptick deactivate or soft-delete the entity rather than removing it, so history pointing at
  it survives.

The existing importers' field logic (e.g. `AssetImporter.Map`) moves into mappers, rewritten against API field
names: `inspected_date` instead of "Last Service Date", `relationships.type` instead of the "Type" name.

## 4. Runs

### 4.1 Kinds of run

All started from the admin page (§9); none scheduled.

| Run | What | Requests (approx.) |
|---|---|---|
| **Full** | Every enabled endpoint in full, then map. Detects deletions. The first run, and the final run at cutover. | ~620 → ~10–12 min |
| **Refresh** | Changes since the last run: verified endpoints with `updatedsince = Cursor − 10 min`, the rest in full. Then map pending records. For keeping FyreApp current while it's being built. | ~250 → ~4 min (most of it the endpoints that can't filter) |
| **Single endpoint** | Full or refresh of one endpoint, e.g. after fixing its mapper. | as needed |
| **Re-map** | Rebuild typed tables from the raw copy. No API calls. | 0 |

Progress streams to the admin page over the existing SignalR hub, as file imports do today. A run can be
cancelled; re-running continues from the raw copy already fetched.

### 4.2 Endpoints and `updatedsince`

A static registry in code lists each endpoint, whether it's enabled, and its mapper if any. Endpoints without a
mapper are still copied, so their data is in FyreApp from the first run.

Not copied: endpoints that are empty for this account (re-checked by count on every Full run, so a module
Fyrepower starts using is picked up), forbidden to the API login, or about Uptick itself rather than Fyrepower's
data (`oauth-applications`, `pushnotificationtokens`, `userpreferences`, `schedulerpresets`,
`schedulerfavouritecontexts`, `mods`, `extensions`, `onboarding-actions`, `chatbotsessions`).

A Refresh only uses `updatedsince` on endpoints where it's **verified**: during a Full run the migrator compares
the full set with an `updatedsince = T` query and checks the latter is exactly the records whose `updated` ≥ T.
Expected to pass: Airbyte's 13 (`tasks`, `assets`, `remarks`, `servicetasks`, `subtasks`, `assettypes`,
`assettypevariants`, `products`, `creditnotes`, `creditnotelineitems`, `rounds`, `promptquestions`, task
profitability) and the large ones where testing showed it narrowing results (`timelineevents`, `routines`,
`reportitems`, `remarkevents`, `correspondence`, `documentfiles`, `invoices`, `invoicelineitems`, `reports`,
`dispatches`, `tasksessions`, `defectquotes`, `defectquotelineitems`, `routineservicelevels`, `routineservices`,
`clients`, `properties`, `billingcards`). Known to ignore it: `correspondenceobjectlinks` (~113 k), `majorservices`,
`contacts`, `addresses`, `taskstock`, `defaulttechnicians`, `contenttypes`, `annotatedstock`,
`dynamicformresponses`, `extrafields`, `displayconditions`, `groups`, `importprojects`, `taskstatuses`,
`standards`, `servicegroups`, `accreditationtypes`.

Full pulls compare each row with the raw copy and only mark changed rows for mapping, so an unchanged table costs
requests but no mapping work.

### 4.3 Deletions

Uptick doesn't return deleted records. A Full run marks raw rows whose ID wasn't returned `DeletedUtc = now`, and
mappers deactivate the matching entity; a row that comes back clears it. A Refresh doesn't detect deletions
(except on endpoints it pulls in full); the next Full run does.

Safety: skip marking deletions for an endpoint if its pull returned fewer than 90% of the previous count (an API
hiccup shouldn't wipe a table).

### 4.4 Reconciliation

Every run ends with a per-endpoint report: Uptick's count (`meta.pagination.count`), rows in the raw copy, rows
mapped, rows pending (unresolved relationships, with the first few IDs), rows that failed to map (with the
error), and schema changes. Sums of money (invoice, quote and credit note totals) are compared too. The report
is kept per run. At cutover it's the sign-off that FyreApp holds everything.

### 4.5 Failure handling

Each endpoint fetches and commits independently. A failure records `LastError`, leaves the cursor where it was,
and the run carries on with the other endpoints. Re-running picks up the failed ones.

**Endpoints paging can't read whole** (`UptickEndpoints.FilledPerParent`). `invoicelineitems` is sorted by
`index` (0 on almost every line), so its order changes between requests: one pass over all 22,857 rows returns
only ~15,000 distinct lines. It's copied after `invoices`, paged as usual, then every invoice whose held lines
don't add up to its `subtotal` (or that has none) is fetched with `invoice=<id>`. That request returns all of an
invoice's lines, so held lines it doesn't return are marked deleted; lines of a deleted invoice are too. If
Uptick ever ignored the filter (returned another invoice's lines) the endpoint fails rather than marking lines
deleted. An invoice fetched this way gets `ChildrenFetchedUtc` and isn't fetched again until the invoice
itself changes (a line added or removed changes its subtotal), so ones that never add up (Xero-synced invoices
with no lines in Uptick, discounts) cost one request ever. The first fill is ~10,000 invoices (~3 h); later Full
runs fetch only invoices changed since. Completeness is lines held
against Uptick's count.

## 5. Data model changes

1. **`UptickId` (bigint, unique, nullable) on every mapped entity.** It replaces `ExternalId`, which holds a ref
   on some entities (Site: "P-0712"; Client) and an Uptick ID on others (Asset, Defect). Refs move to their own
   column (`Ref`) where Uptick has one (properties `ref`, assets `uptick_ref` "A-41642", quotes, invoices).
   `DisplayRef` keeps working off `Ref`.
2. **Foreign keys instead of copied names.** e.g. `DefectQuote.PropertyRef/PropertyName/ClientName` become
   `SiteId` / `ClientId`; `Invoice.TaskExternalId` becomes `ClientTaskId`; `ServiceReport.TaskExternalId`,
   `Defect.RepairTaskExternalId` likewise.
3. **Fields the exports never had**, e.g. `Asset.LastServicedById` (API `serviced_by`) replacing today's guess
   from reports in `AssetPageService.LastServiceByAsync`.
4. **`TimelineEntry`**: `UptickId` (unique) plus `Latitude` / `Longitude` for the "· Map" link; `Key`
   (`trackedchange`, `general` = note, `correspondence`, …).
5. **New entities** for the rest (§6), added as the screens that use them are built; the raw copy already holds
   their data.

FyreApp-native rows (created in FyreApp, `UptickId` null) are left as they are. The `UptickData` jsonb columns on
typed entities are dropped once their entity is mapped from the raw copy.

## 6. Resource map

Existing FyreApp entities and the endpoint each is mapped from:

| FyreApp entity | Uptick endpoint(s) | Today's file import |
|---|---|---|
| Client | `clients` | (placeholders from properties) |
| ClientContact | `clientcontacts`, `contacts` | ClientContacts |
| Site | `properties` (+ `addresses`) | Properties |
| SiteContact | `propertycontacts`, `contacts` | PropertyContacts |
| Asset | `assets` | Assets |
| AssetType / AssetTypeVariant | `assettypes` / `assettypevariants` | AssetTypes / AssetTypeVariants |
| UptickUser | `users` | Users |
| SecurityGroup | `groups` | SecurityGroups |
| Contractor / Supplier | `contractors` (+ `contractorcontacts`) / `suppliers` | Contractors / Suppliers |
| ClientTask | `tasks` | Tasks |
| TaskAsset | `servicetasks` | (generated from routines) |
| Defect | `remarks` | Remarks |
| RemarkType | `remarktypes` | RemarkTypes |
| ServiceReport | `reports` | Reports |
| RoutineServiceType / RoutineServiceLevel | `routineservicetypes` / `routineservicelevels` (+ `routineserviceleveltypes`) | same |
| PropertyRoutineService | `routineservices` | (FyreApp-native) |
| RoutineOccurrence | `routines` | (routines export) |
| TimelineEntry | `timelineevents` | (derived from imports) |
| DefectQuote | `defectquotes` | DefectQuotes |
| SalesQuote | `servicequotes` | SalesQuotes |
| Invoice / CreditNote | `invoices` / `creditnotes` | Invoices / CreditNotes |
| BillingCard / BillingContract / BillingContractLineItem | `billingcards` / `billingcontracts` / `billingcontractlineitems` | same |
| Product / Warehouse / StockLevel | `products` / `warehouses` / `stock` | same |
| Accreditation | `accreditations` | Accreditations |
| ExtraField | `extrafields` | ExtraFields |
| FormResponse | `dynamicformresponses` | FormResponses |
| PropertySchedule | `propertyschedules` | PropertySchedules |
| PromptQuestion | `promptquestions` | PromptQuestions |

Copied from the first run, typed entity added with the screen that needs it (largest first):
`reportitems`, `correspondence` + `correspondenceobjectlinks`, `remarkevents`, `subtasks`, `documentfiles`,
`invoicelineitems`, `dispatches`, `tasksessions` (technician time on site), `defectquotelineitems`,
`majorservices`, `taskstocktransactions`, `appointments`, `taskclientsignatures`, `contractortasks`,
`suggestedproducts`, `prices` / `pricetiers`, `actions` / `actiongroups`, `servicequote*lineitems`,
`stock*` movements, `logbooks` / `logbookentries`, `taskstatuses`, `taskcategories`, `tasksessiontypes`,
`reporttypes`, `documenttemplates` / `emailtemplates` / `snippettemplates`, `standards`, `servicegroups`.

## 7. Edits made in FyreApp before cutover

Edits made in FyreApp never reach Uptick. FyreApp's own records (`UptickId` null: FyreApp quotes, notes added in
FyreApp, routines set up in FyreApp) are never touched by a migration run.

For an edit to a field of a **migrated** record (e.g. an asset's Location changed on FyreApp's asset form), a
later Refresh or Full run applies **last change wins, per field**:

- Saving the edit records a `LocalEdit`: entity, record ID, field, the FyreApp value, **the Uptick value at the
  time of the edit**, who and when.
- When a mapper writes that field, it compares Uptick's current value with the one recorded at the edit:
  - **Same:** Uptick hasn't changed that field since, so the FyreApp value stays.
  - **Different:** someone changed it in Uptick after the FyreApp edit, so Uptick's value wins and the
    `LocalEdit` is closed (kept for history).
- Because the comparison is against stored values, not timing, edits survive Full runs and re-mapping.
- The record page marks fields that differ from Uptick ("edited in FyreApp"), with an admin action to reset a
  field to Uptick's value.

If staff work only in Uptick until cutover, this rarely comes into play, but it keeps re-runs safe.

## 8. Files

"All data" includes the files Uptick stores: report PDFs, photos, signatures, attachments
(`documentfiles` ~26 k, plus files referenced by `reports`, `taskclientsignatures`, `correspondence`). The raw
copy holds their metadata and links; the files themselves are downloaded in their own run and recorded as
`UptickFile` (source URL, size, content type, hash, storage key), skipping files already downloaded.

**Storage: Amazon S3, Sydney region (`ap-southeast-2`).**
- Keeps the data in Australia; Azure Blob (Australia East) would too, at a similar storage price.
- The S3 API is the de facto standard: Cloudflare R2, Backblaze B2 and others implement it. FyreApp talks to
  storage through one `IFileStore` interface backed by the AWS SDK's S3 client with a configurable endpoint, so
  moving to an S3-compatible provider later is a config change. Worth it because the ongoing cost is mostly
  downloads (staff opening PDFs and photos), which R2 doesn't charge for. Azure Blob's API is Azure-only.
- FyreApp is hosted on its own VPS, not Azure, so Azure has no integration advantage here.
- Bucket private; files served through FyreApp (authorisation checked) or short-lived pre-signed URLs.
  Credentials (`Storage:S3:*`) in `/etc/fyreapp.env`, from an IAM user limited to that bucket.

The first Full run measures the total size from the metadata, so the monthly cost is known before the bucket is
created. File downloads count against the same request budget: at 60/minute ~26 k files take ~7–8 hours, so this
run is best left going overnight; it's resumable, so it can also run in parts.

## 9. Admin page: Uptick migration

Replaces the Uptick section of the Imports page (file upload stays available until the file importers are
removed):
- Connection check (token OK, API version, login used).
- Buttons: Full run, Refresh, Re-map; per endpoint: run, re-map.
- Live progress via SignalR; cancel.
- Per endpoint: Uptick count, copied, mapped, pending, failed, last run, last error, schema changes.
- Reconciliation reports of past runs (§4.4).

## 10. Phases

Each is one PR.

1. **API client + raw copy + Full / Refresh runs + admin page + reconciliation.** After this, all of Uptick's
   data (except files) is in FyreApp's database, verified by count. No screen changes yet.
2. **Timeline.** `TimelineEntry` mapper; drop the made-up "was created" entries and import-derived "Uptick"
   changes. (Designed earlier: notes, Map link, template wording.)
3. **Core records:** clients, properties, contacts, users, asset types/variants, assets. `UptickId` + `Ref`
   columns and FKs; `LocalEdit` (§7), since the asset form already edits migrated fields; file importers for
   these turned off.
4. **Work:** tasks, service tasks, remarks, reports, routines, routine services/levels/types.
5. **Commercial:** defect quotes, sales quotes, invoices, credit notes, billing, products, stock.
6. **Remaining reference data;** remove the file importers and `UptickData` columns.
7. **Files.**
8. **The rest:** typed entities and screens for the endpoints still only in the raw copy, in order of need.
9. **Cutover:** freeze Uptick, final Full run + files refresh, reconciliation sign-off, FyreApp becomes the
   system of record.

## 11. Alternatives considered

**Airbyte** has a maintained Uptick connector that could copy Uptick into Postgres. Not chosen: it syncs a fixed
subset of fields per stream (new Uptick fields don't appear until the connector is updated), doesn't cover every
endpoint FyreApp needs or the files, and self-hosting it adds a heavy service (Docker, several GB of RAM) next to
a single ASP.NET app. Its documented behaviour (password grant, v2.15, 60 requests/minute, `Retry-After`
handling, which streams honour `updatedsince`) informed this design.

**Keep using file exports.** Exports lack IDs for relationships, who did what, the Timeline, service-task detail,
correspondence and files; they can't get all of Uptick into FyreApp.

## 12. Open questions

1. **API login.** Create a dedicated Uptick user (e.g. "FyreApp Integration") before running against prod for
   real: a staff member's password change or departure then can't break a run, and its activity is easy to spot
   in Uptick. It needs read access to everything (the current login gets `403` on `logins`, `constance`,
   `standardbundles`, `customsqlreports`); if Uptick offers a read-only role, use it. Development can use the
   current login until then.
2. **Rate limit.** Start at 60 requests/minute. Worth asking Uptick support whether more is fine for a one-off
   migration (it mostly matters for files).
3. **Invoice line items.** Decided: fetched an invoice at a time for invoices whose held lines don't add up to
   their subtotal (§4.5).
4. **Storage account.** An AWS account and S3 bucket in Sydney (§8), needed by the Files phase; the first run
   measures the total file size so the cost is known first.
