using System.Security.Claims;
using AutoCare.Data;
using AutoCare.Models;
using AutoCare.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AutoCare.Controllers;

[Authorize(Roles = "User")]
public class AppointmentsController(AppDbContext db, EmailService emailService, NotificationService notifications) : Controller
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task LoadLists()
    {
        ViewBag.Vehicles = new SelectList(await db.Vehicles.Where(x => x.UserId == UserId).ToListAsync(), "Id", "RegistrationNumber");
        ViewBag.Services = new SelectList(await db.WorkshopServices.Where(x => x.IsActive).ToListAsync(), "Id", "Name");
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.SavedServices = await db.SavedServices.Where(x => x.UserId == UserId)
            .Include(x => x.WorkshopService).OrderByDescending(x => x.SavedAt).ToListAsync();
        ViewBag.Calendar = await db.Appointments.Where(x => x.UserId == UserId && x.AppointmentAt >= DateTime.Today.AddDays(-7))
            .Include(x => x.WorkshopService).OrderBy(x => x.AppointmentAt).Take(12).ToListAsync();
        return View(await db.Appointments.Where(x => x.UserId == UserId)
            .Include(x => x.Vehicle).Include(x => x.WorkshopService)
            .OrderByDescending(x => x.AppointmentAt).ToListAsync());
    }

    public async Task<IActionResult> Create(int? serviceId)
    {
        await LoadLists();
        var vm = new AppointmentVm();
        if (serviceId.HasValue && await db.WorkshopServices.AnyAsync(x => x.Id == serviceId && x.IsActive))
            vm.WorkshopServiceId = serviceId.Value;
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> CheckAvailability(DateTime appointmentAt, int serviceId)
    {
        if (appointmentAt <= DateTime.Now) return Json(new { available = false, message = "Select a future date and time." });
        if (appointmentAt.Hour < 8 || appointmentAt.Hour >= 18) return Json(new { available = false, message = "Workshop hours are 8:00 AM to 6:00 PM." });
        if (!await db.WorkshopServices.AnyAsync(x => x.Id == serviceId && x.IsActive))
            return Json(new { available = false, message = "The selected service is unavailable." });
        bool occupied = await db.Appointments.AnyAsync(x => x.AppointmentAt == appointmentAt
            && x.Status != AppointmentStatus.Cancelled && x.Status != AppointmentStatus.Rejected);
        return Json(new { available = !occupied, message = occupied ? "This time slot is already booked." : "Service and time slot are available." });
    }

    [HttpGet]
    public async Task<IActionResult> AvailableSlots(DateTime date, int serviceId)
    {
        var day = date.Date;
        var occupied = await db.Appointments.Where(x => x.AppointmentAt >= day && x.AppointmentAt < day.AddDays(1)
            && x.Status != AppointmentStatus.Cancelled && x.Status != AppointmentStatus.Rejected)
            .Select(x => x.AppointmentAt).ToListAsync();
        var slots = Enumerable.Range(8, 10).Select(hour => day.AddHours(hour))
            .Select(slot => new
            {
                value = slot.ToString("yyyy-MM-ddTHH:mm"),
                label = slot.ToString("hh:mm tt"),
                available = slot > DateTime.Now && !occupied.Contains(slot),
                booked = occupied.Contains(slot),
                status = occupied.Contains(slot) ? "booked" : slot <= DateTime.Now ? "past" : "available"
            });
        return Json(slots);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AppointmentVm vm)
    {
        if (!await db.Vehicles.AnyAsync(x => x.Id == vm.VehicleId && x.UserId == UserId))
            ModelState.AddModelError(nameof(vm.VehicleId), "Invalid vehicle.");
        if (!await db.WorkshopServices.AnyAsync(x => x.Id == vm.WorkshopServiceId && x.IsActive))
            ModelState.AddModelError(nameof(vm.WorkshopServiceId), "Selected service is unavailable.");
        if (vm.AppointmentAt <= DateTime.Now) ModelState.AddModelError(nameof(vm.AppointmentAt), "Appointment must be in the future.");
        if (vm.AppointmentAt.Hour < 8 || vm.AppointmentAt.Hour >= 18)
            ModelState.AddModelError(nameof(vm.AppointmentAt), "Workshop hours are 8:00 AM to 6:00 PM.");
        if (await db.Appointments.AnyAsync(x => x.AppointmentAt == vm.AppointmentAt
            && x.Status != AppointmentStatus.Cancelled && x.Status != AppointmentStatus.Rejected))
            ModelState.AddModelError(nameof(vm.AppointmentAt), "This time slot is unavailable.");
        if (!ModelState.IsValid) { await LoadLists(); return View(vm); }

        db.Appointments.Add(new Appointment
        {
            UserId = UserId,
            VehicleId = vm.VehicleId,
            WorkshopServiceId = vm.WorkshopServiceId,
            AppointmentAt = vm.AppointmentAt,
            ProblemDescription = vm.ProblemDescription
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "Appointment submitted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var appointment = await db.Appointments.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId && x.Status == AppointmentStatus.Pending);
        if (appointment != null) { appointment.Status = AppointmentStatus.Cancelled; await db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Quote(int id, bool approve)
    {
        var quotation = await db.Quotations.Include(x => x.Inspection)!.ThenInclude(x => x.Appointment)
            .FirstOrDefaultAsync(x => x.Id == id && x.Inspection!.Appointment!.UserId == UserId && x.Status == QuoteStatus.Pending);
        if (quotation == null) return NotFound();
        quotation.Status = approve ? QuoteStatus.Approved : QuoteStatus.Rejected;
        if (approve)
        {
            db.RepairJobs.Add(new RepairJob { QuotationId = quotation.Id });
            await notifications.NotifyUserAsync(UserId, "Quotation approved", "Your quotation was approved and the repair job has started.", "Quotation");
        }
        else
        {
            await notifications.NotifyUserAsync(UserId, "Quotation rejected", "Your quotation was rejected. The workshop will keep the record for reference.", "Quotation");
        }
        await db.SaveChangesAsync();
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SendReminder(int id)
    {
        var appointment = await db.Appointments.Include(x => x.User).Include(x => x.Vehicle).Include(x => x.WorkshopService)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId && x.AppointmentAt > DateTime.Now);
        if (appointment == null) return NotFound();
        bool sent = await emailService.SendAppointmentReminderAsync(appointment.User!.Email, appointment.User.FullName, appointment);
        appointment.ReminderSent = sent;
        await db.SaveChangesAsync();
        TempData["Success"] = sent ? "Appointment reminder email sent." : "Reminder recorded. Configure SMTP to send email.";
        return RedirectToAction(nameof(Index));
    }
}
