using FyreApp.Services.Techs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Controllers;

[Authorize(Roles = "Admin")]
public class UsersController : Controller
{
    private readonly ITechService _techService;

    public UsersController(ITechService techService)
    {
        _techService = techService;
    }

    public async Task<IActionResult> Index()
    {
        var techs = await _techService.GetAllAsync();
        return View(techs);
    }

    [HttpGet]
    public IActionResult Create() => View(new TechCreateDto());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TechCreateDto dto)
    {
        if (!ModelState.IsValid) return View(dto);

        var result = await _techService.CreateAsync(dto);

        switch (result.Status)
        {
            case TechCreateStatus.Success:
                TempData["Success"] = "Tech created successfully.";
                return RedirectToAction(nameof(Index));

            case TechCreateStatus.EmailAlreadyExists:
                ModelState.AddModelError(nameof(dto.Email), "A user with this email already exists.");
                return View(dto);

            default:
                foreach (var error in result.Errors ?? [])
                    ModelState.AddModelError(string.Empty, error);
                return View(dto);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var tech = await _techService.GetByIdAsync(id);
        if (tech == null) return NotFound();

        var dto = new TechEditDto
        {
            FirstName = tech.FirstName,
            LastName = tech.LastName,
            Email = tech.Email,
            PhoneNumber = tech.PhoneNumber
        };

        ViewBag.TechId = id;
        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, TechEditDto dto)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.TechId = id;
            return View(dto);
        }

        var result = await _techService.UpdateAsync(id, dto);

        switch (result.Status)
        {
            case TechUpdateStatus.Success:
                TempData["Success"] = "Tech updated successfully.";
                return RedirectToAction(nameof(Index));

            case TechUpdateStatus.NotFound:
                return NotFound();

            case TechUpdateStatus.EmailAlreadyExists:
                ModelState.AddModelError(nameof(dto.Email), "A user with this email already exists.");
                ViewBag.TechId = id;
                return View(dto);

            default:
                foreach (var error in result.Errors ?? [])
                    ModelState.AddModelError(string.Empty, error);
                ViewBag.TechId = id;
                return View(dto);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(string id)
    {
        await _techService.DeactivateAsync(id);
        TempData["Success"] = "Tech deactivated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        await _techService.DeleteAsync(id);
        TempData["Success"] = "Tech deleted.";
        return RedirectToAction(nameof(Index));
    }
}