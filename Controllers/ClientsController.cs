using FyreApp.Data;
using FyreApp.Dtos;
using FyreApp.Models;
using FyreApp.Hubs;
using FyreApp.Services.Clients;
using FyreApp.ViewModels.Clients;
using FyreApp.ViewModels.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FyreApp.Controllers
{
    [Authorize]
    public class ClientsController : Controller
    {
        private readonly IClientImportService _importService;
        private readonly IHubContext<ImportProgressHub> _hub;
        private readonly IImportTracker _tracker;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IClientService _clients;
        private readonly AppDbContext _db;


        public ClientsController(
            IClientImportService importService,
            IHubContext<ImportProgressHub> hub,
            IImportTracker tracker,
            IServiceScopeFactory scopeFactory,
            IClientService clients,
            AppDbContext db
        )
        {
            _importService = importService;
            _hub = hub;
            _tracker = tracker;
            _scopeFactory = scopeFactory;
            _clients = clients;
            _db = db;
        }

        // GET: ClientsController
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var vm = new ClientIndexVm
            {
                Clients = await _clients.GetAllAsync(activeOnly: true)
            };

            return View(vm);
        }

        // GET: /Clients/Details/5
        public async Task<IActionResult> Details(int id, CancellationToken ct)
        {
            var client = await _clients.GetByIdAsync(id, ct);
            if (client is null)
                return NotFound();

            var tasks = await _clients.GetTasksByClientAsync(id, ct);
            var intervals = await _db.MaintenanceIntervals.OrderBy(i => i.Months).ToListAsync(ct);

            var vm = new ClientDetailsVm
            {
                Client = client,
                Tasks = tasks,
                Intervals = intervals,
                Edit = new UpdateClientRequest
                {
                    Name = client.Name,
                    Active = client.Active,
                    PrimaryContactName = client.PrimaryContactName,
                    PrimaryContactEmail = client.PrimaryContactEmail,
                    PrimaryContactMobile = client.PrimaryContactMobile,
                    PrimaryContactCcEmail = client.PrimaryContactCcEmail,
                    PrimaryContactAddress = client.PrimaryContactAddress,
                    BillingName = client.BillingName,
                    BillingAttentionTo = client.BillingAttentionTo,
                    BillingEmail = client.BillingEmail,
                    BillingCcEmail = client.BillingCcEmail,
                    BillingAddress = client.BillingAddress,
                    Updated = client.Updated
                },
                OpenEdit = TempData["OpenEdit"] as bool? ?? false
            };

            return View(vm);
        }

        // POST: /Client/Create (submitted from modal)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(CreateClientVm vm)
        {

            if (!ModelState.IsValid)
                return await RebuildIndexView(vm);

            var result = await _clients.CreateAsync(vm);

            return result.Status switch
            {
                ClientCreateStatus.Success => TempDataSuccess($"Client \"{vm.Name!.Trim()}\" created.", RedirectToAction("Details", new { id = result.ClientId })),
                ClientCreateStatus.DuplicateName => await RebuildIndexView(vm, result.Existing),
                ClientCreateStatus.ValidationError => await RebuildIndexView(vm),
                _ => BadRequest()
            };
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public IActionResult Import() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(int id, ClientDetailsVm request, CancellationToken ct)
        {
            var result = await _clients.UpdateAsync(id, request.Edit, ct);

            return result.Status switch
            {
                ClientUpdateStatus.Success => RedirectToAction(nameof(Details), new { id }),
                ClientUpdateStatus.NotFound => NotFound(),

                ClientUpdateStatus.DuplicateName => RedirectWithError(id, result.ErrorMessage ?? "A client with this name already exists."),
                ClientUpdateStatus.ValidationError => RedirectWithError(id, result.ErrorMessage ?? "Please check the form and try again."),

                _ => BadRequest()
            };

            IActionResult RedirectWithError(int clientId, string message)
            {
                TempData["Error"] = message;
                TempData["OpenEdit"] = true;
                return RedirectToAction(nameof(Details), new { id = clientId });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id, bool hardDelete = false, CancellationToken ct = default)
        {
            var result = await _clients.DeleteAsync(id, hardDelete, ct);

            if (result.Status == ClientDeleteStatus.NotFound)
                return NotFound();

            TempData["Success"] = hardDelete
                ? "Client deleted."
                : "Client has been archived.";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateAsset(int id, int siteId, string name, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["Error"] = "Asset name is required.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var site = await _db.Sites.FirstOrDefaultAsync(s => s.Id == siteId && s.ClientId == id, ct);
            if (site is null)
                return NotFound();

            _db.Assets.Add(new Asset { SiteId = siteId, Name = name.Trim() });
            await _db.SaveChangesAsync(ct);

            TempData["Success"] = $"Asset \"{name.Trim()}\" created.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTask(int id, CreateClientTaskVm task, CancellationToken ct)
        {
            task.ClientId = id;

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Please check the form and try again.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var siteExists = await _db.Sites.AnyAsync(s => s.Id == task.SiteId && s.ClientId == id, ct);
            if (!siteExists)
            {
                TempData["Error"] = "Selected property does not belong to this client.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var tz = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");
            DateTime? dueUtc = task.DueDateLocal.HasValue
                ? TimeZoneInfo.ConvertTimeToUtc(task.DueDateLocal.Value, tz)
                : null;

            _db.ClientTasks.Add(new ClientTask
            {
                ClientId = id,
                SiteId = task.SiteId,
                Title = task.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(task.Description) ? null : task.Description.Trim(),
                Priority = task.Priority,
                Status = ClientTaskStatus.Open,
                DueDateUtc = dueUtc,
                CreatedUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);

            TempData["Success"] = $"Task \"{task.Title.Trim()}\" created.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateSchedule(int id, ScheduleTargetType targetType, int targetId, DateTime startDate, int intervalId, CancellationToken ct)
        {
            var interval = await _db.MaintenanceIntervals.FindAsync([intervalId], ct);
            if (interval is null)
            {
                TempData["Error"] = "Invalid interval selected.";
                return RedirectToAction(nameof(Details), new { id });
            }

            bool targetValid = targetType == ScheduleTargetType.Site
                ? await _db.Sites.AnyAsync(s => s.Id == targetId && s.ClientId == id, ct)
                : await _db.Assets.AnyAsync(a => a.Id == targetId && a.Site.ClientId == id, ct);

            if (!targetValid)
                return NotFound();

            var startUtc = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);

            _db.MaintenanceSchedules.Add(new MaintenanceSchedule
            {
                TargetType = targetType,
                SiteId = targetType == ScheduleTargetType.Site ? targetId : null,
                AssetId = targetType == ScheduleTargetType.Asset ? targetId : null,
                MaintenanceIntervalId = interval.Id,
                StartDate = startUtc,
                NextRunDate = startUtc.AddMonths(interval.Months)
            });
            await _db.SaveChangesAsync(ct);

            TempData["Success"] = "Maintenance schedule created.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpGet("/api/clients")]
        public async Task<IActionResult> ApiList(string? search = null)
        {
            var clients = await _clients.GetAllAsync(activeOnly: false);

            if (!string.IsNullOrWhiteSpace(search))
                clients = clients.Where(c =>
                    c.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (c.PrimaryContactName ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
                ).ToList();

            return Json(clients.Select(c => new {
                c.Id,
                c.Name,
                c.PrimaryContactName,
                c.PrimaryContactMobile,
                siteCount = c.Sites?.Count ?? 0,
                c.Active
            }));
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> StartImport(IFormFile file, bool dryRun)
        {
            
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            var importId = Guid.NewGuid().ToString("N");

            // Seed tracker immediately so polling never 404s
            _tracker.Set(importId, new ClientImportProgressDto
            {
                ImportId = importId,
                DryRun = dryRun,
                Total = 0,
                Processed = 0,
                WouldCreate = 0,
                SkippedDuplicateExternalId = 0,
                SkippedDuplicateName = 0,
                SkippedMissingName = 0,
                SkippedInvalid = 0,
                Failed = 0,
                Completed = false,
                Message = "Queued..."
            });

            //broadcast initial state (nice UX)
            _ = _hub.Clients.Group(importId).SendAsync("progress", _tracker.Get(importId));


            // Save upload to temp so background task can read it safely
            var ext = Path.GetExtension(file.FileName);
            var tempPath = Path.Combine(Path.GetTempPath(), $"client-import-{importId}{ext}");

            await using (var fs = System.IO.File.Create(tempPath))
            {
                await file.CopyToAsync(fs);
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var importService = scope.ServiceProvider.GetRequiredService<IClientImportService>();

                    await using var readStream = System.IO.File.OpenRead(tempPath);

                    var bgFile = new FormFile(readStream, 0, readStream.Length, "file", file.FileName)
                    {
                        Headers = new HeaderDictionary(),
                        ContentType = file.ContentType
                    };

                    await importService.ImportAsync(
                        bgFile,
                        dryRun,
                        reportProgress: p =>
                        {
                            p.ImportId = importId;
                            _tracker.Set(importId, p);
                            _ = _hub.Clients.Group(importId).SendAsync("progress", p);
                        },
                        importId: importId,
                        ct: CancellationToken.None);

                    // cleanup temp file
                    try { System.IO.File.Delete(tempPath); } catch { /* ignore */ }
                }
                catch (Exception ex)
                {
                    var fail = new ClientImportProgressDto
                    {
                        ImportId = importId,
                        Completed = true,
                        Failed = 1,
                        Message = ex.Message,
                        DryRun = dryRun
                    };

                    _tracker.Set(importId, fail);
                    _ = _hub.Clients.Group(importId).SendAsync("progress", fail);

                    // best effort cleanup
                    try { System.IO.File.Delete(tempPath); } catch { /* ignore */ }
                }
            }, CancellationToken.None);

            return Json(new { importId, dryRun });

        }

        // fallback polling endpoint (if SignalR is blocked)
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public IActionResult ImportStatus(string id)
        {
            var p = _tracker.Get(id);
            return p == null ? NotFound() : Json(p);
        }


        private IActionResult TempDataSuccess(string successMsg, IActionResult redirect)
        {
            TempData["Success"] = successMsg;
            return redirect;
        }

        private async Task<IActionResult> RebuildIndexView(CreateClientVm vm, Client? existing = null)
        {
            if (existing is not null)
                vm.ExistingClient = new ClientVm { ClientId = existing.Id, ClientName = existing.Name };

            return View("Index", new ClientIndexVm
            {
                Clients = await _clients.GetAllAsync(activeOnly: true),
                OpenCreateModal = true,
                Create = vm
            });
        }

    }
}