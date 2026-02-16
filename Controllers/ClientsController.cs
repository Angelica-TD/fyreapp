using FyreApp.Data;
using FyreApp.Dtos;
using FyreApp.Models;
using FyreApp.Hubs;
using FyreApp.Services.Clients;
using FyreApp.ViewModels.Clients;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FyreApp.Controllers
{
    [Authorize]
    public class ClientsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IClientImportService _importService;
        private readonly IHubContext<ImportProgressHub> _hub;
        private readonly IImportTracker _tracker;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IClientService _clients;


        public ClientsController(
            AppDbContext context,
            IClientImportService importService,
            IHubContext<ImportProgressHub> hub,
            IImportTracker tracker,
            IServiceScopeFactory scopeFactory,
            IClientService clients
        )

        {
            _context = context;
            _importService = importService;
            _hub = hub;
            _tracker = tracker;
            _scopeFactory = scopeFactory;
            _clients = clients;
        }

        // GET: ClientsController
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var vm = new ClientIndexVm
            {
                Clients = await _context.Clients
                    .Include(c => c.Sites)
                    .OrderBy(c => c.Name)
                    .ToListAsync()
            };

            return View(vm);
        }

        // GET: /Clients/Details/5
        public async Task<IActionResult> Details(int id, CancellationToken ct)
        {
            var client = await _context.Clients
                .Include(c => c.Sites)
                    .ThenInclude(s => s.Assets)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (client == null)
                return NotFound();

            var vm = new ClientDetailsVm
            {
                Client = client,
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
            {
                var indexVm = new ClientIndexVm
                {
                    Clients = await _context.Clients
                        .Include(c => c.Sites)
                        .OrderBy(c => c.Name)
                        .ToListAsync(),
                    OpenCreateModal = true,
                    Create = vm
                };

                return View("Index", indexVm);
            }

            var name = vm.Name!.Trim();

            var exists = await _context.Clients
                .FirstOrDefaultAsync(c => c.Name == vm.Name);

            if (exists != null)
            {

                vm.ExistingClient = new ClientVm
                    {
                    ClientId = exists.Id,
                    ClientName = exists.Name  
                    };

                var indexVm = new ClientIndexVm
                {
                    Clients = await _context.Clients
                        .Include(c => c.Sites)
                        .OrderBy(c => c.Name)
                        .ToListAsync(),
                    OpenCreateModal = true,
                    Create = vm
                };

                return View("Index", indexVm);
            }

            _context.Clients.Add(new Client { Name = name });
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Client “{name}” created.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public IActionResult Import() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(int id, UpdateClientRequest request, CancellationToken ct)
        {
            var result = await _clients.UpdateAsync(id, request, ct);

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
                : "Client deactivated.";

            return RedirectToAction(nameof(Index));
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
                    // ✅ create a brand new DI scope for this background job
                    using var scope = _scopeFactory.CreateScope();
                    var importService = scope.ServiceProvider.GetRequiredService<IClientImportService>();

                    await using var readStream = System.IO.File.OpenRead(tempPath);

                    // Build a FormFile backed by the temp stream
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


    }
}