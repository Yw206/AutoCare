using System.Security.Claims;
using AutoCare.Data;
using AutoCare.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoCare.Controllers;

[Authorize(Roles = "User")]
public class ServicesController(AppDbContext db) : Controller
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Catalog(string? search, string sort = "name_asc", int page = 1)
    {
        const int pageSize = 6;
        page = Math.Max(1, page);
        var query = db.WorkshopServices.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(x => x.Name.Contains(term) || (x.Description != null && x.Description.Contains(term)));
        }
        query = sort switch
        {
            "name_desc" => query.OrderByDescending(x => x.Name),
            "price_asc" => query.OrderBy(x => x.EstimatedPrice).ThenBy(x => x.Name),
            "price_desc" => query.OrderByDescending(x => x.EstimatedPrice).ThenBy(x => x.Name),
            "duration_asc" => query.OrderBy(x => x.EstimatedMinutes).ThenBy(x => x.Name),
            _ => query.OrderBy(x => x.Name)
        };
        int totalItems = await query.CountAsync();
        int totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        page = Math.Min(page, totalPages);
        ViewBag.SavedIds = await db.SavedServices.Where(x => x.UserId == UserId)
            .Select(x => x.WorkshopServiceId).ToListAsync();
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalItems = totalItems;
        return PartialView("_ServiceCards", await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int serviceId)
    {
        bool valid = await db.WorkshopServices.AnyAsync(x => x.Id == serviceId && x.IsActive);
        bool exists = await db.SavedServices.AnyAsync(x => x.UserId == UserId && x.WorkshopServiceId == serviceId);
        if (valid && !exists)
        {
            db.SavedServices.Add(new SavedService { UserId = UserId, WorkshopServiceId = serviceId });
            await db.SaveChangesAsync();
            TempData["Success"] = "Service saved to your list.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int serviceId)
    {
        var saved = await db.SavedServices.FirstOrDefaultAsync(x => x.UserId == UserId && x.WorkshopServiceId == serviceId);
        if (saved != null) { db.SavedServices.Remove(saved); await db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index));
    }
}
