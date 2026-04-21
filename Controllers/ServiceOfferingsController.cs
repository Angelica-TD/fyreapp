using FyreApp.Data;
using FyreApp.Services.ServiceOfferings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Controllers;

[Authorize(Roles = "Admin,Developer")]
public class ServiceOfferingsController : Controller
{
    private readonly IServiceOfferingService _service;
    private readonly AppDbContext _db;

    public ServiceOfferingsController(IServiceOfferingService service, AppDbContext db)
    {
        _service = service;
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var offerings = await _service.GetAllAsync();
        return View(offerings);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var dto = new ServiceOfferingFormDto();
        await PopulateIntervalsViewBag();
        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ServiceOfferingFormDto dto)
    {
        if (!ModelState.IsValid)
        {
            await PopulateIntervalsViewBag();
            return View(dto);
        }

        await _service.CreateAsync(dto);
        TempData["Success"] = "Service offering created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var offering = await _service.GetByIdAsync(id);
        if (offering == null) return NotFound();

        var dto = new ServiceOfferingFormDto
        {
            Name = offering.Name,
            Description = offering.Description,
            IsActive = offering.IsActive,
            SelectedIntervalIds = offering.Intervals.Select(i => i.Id).ToList()
        };

        ViewBag.OfferingId = id;
        await PopulateIntervalsViewBag();
        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ServiceOfferingFormDto dto)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.OfferingId = id;
            await PopulateIntervalsViewBag();
            return View(dto);
        }

        var found = await _service.UpdateAsync(id, dto);
        if (!found) return NotFound();

        TempData["Success"] = "Service offering updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteAsync(id);
        TempData["Success"] = "Service offering deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateIntervalsViewBag()
    {
        ViewBag.AllIntervals = await _db.MaintenanceIntervals
            .OrderBy(i => i.Months)
            .Select(i => new IntervalSummary { Id = i.Id, Name = i.Name, Months = i.Months })
            .ToListAsync();
    }
}
