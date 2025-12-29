using FyreApp.Data;
using FyreApp.Models;
using FyreApp.ViewModels.Clients;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace FyreApp.Controllers
{
    [Authorize]
    public class ClientsController : Controller
    {
        private readonly AppDbContext _context;

        public ClientsController(AppDbContext context)
        {
            _context = context;
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
        public async Task<IActionResult> Details(int id)
        {
            var client = await _context.Clients
                .Include(c => c.Sites)
                    .ThenInclude(s => s.Assets)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (client == null)
                return NotFound();

            return View(client);
        }

        // POST: /Client/Create (submitted from modal)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(ClientIndexVm vm)
        {
            
            if (!ModelState.IsValid)
            {
                vm.Clients = await _context.Clients
                    .Include(c => c.Sites)
                    .OrderBy(c => c.Name)
                    .ToListAsync();

                vm.OpenCreateModal = true;
                return View("Index", vm);
            }

            var name = vm.Create.Name!.Trim();

            var exists = await _context.Clients
                .AnyAsync(c => c.Name.ToLower() == name.ToLower());

            if (exists)
            {
                ModelState.AddModelError("Create.Name", "A client with this name already exists.");

                vm.Clients = await _context.Clients
                    .Include(c => c.Sites)
                    .OrderBy(c => c.Name)
                    .ToListAsync();

                vm.OpenCreateModal = true;
                return View("Index", vm);
            }

            _context.Clients.Add(new Client { Name = name });
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Client “{name}” created.";
            return RedirectToAction(nameof(Index));
        }
    }
}