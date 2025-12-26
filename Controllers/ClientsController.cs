using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FyreApp.Data;

namespace FyreApp.Controllers
{
    public class ClientsController : Controller
    {
        private readonly AppDbContext _context;

        public ClientsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: ClientsController
        public async Task<IActionResult> Index()
        {
            var clients = await _context.Clients
                .Include(c => c.Sites)
                .ToListAsync();

            return View(clients);
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

    }
}
