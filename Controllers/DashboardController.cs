using System.Security.Claims;
using AutoCare.Data;
using AutoCare.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoCare.Controllers;

[Authorize(Roles = "User")]
public class DashboardController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        ViewBag.Vehicles = await db.Vehicles.CountAsync(x => x.UserId == userId);
        ViewBag.Pending = await db.Appointments.CountAsync(x => x.UserId == userId && x.Status == AppointmentStatus.Pending);
        ViewBag.Announcements = await db.Announcements.OrderByDescending(x => x.PublishedAt).Take(5).ToListAsync();
        return View(await db.Appointments.Where(x => x.UserId == userId)
            .Include(x => x.Vehicle).Include(x => x.WorkshopService)
            .Include(x => x.Inspection)!.ThenInclude(x => x.Photos)
            .Include(x => x.Inspection)!.ThenInclude(x => x.Quotation)!.ThenInclude(x => x.RepairJob)!.ThenInclude(x => x.Invoice)
            .OrderByDescending(x => x.CreatedAt).ToListAsync());
    }
}
