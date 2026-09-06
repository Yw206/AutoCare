using System.Security.Claims;
using AutoCare.Data;
using AutoCare.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoCare.Controllers;

[Authorize(Roles = "User")]
public class PurchaseHistoryController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var purchases = await db.Invoices
            .Where(x => x.PaymentStatus == PaymentStatus.Paid && x.PaidAt.HasValue &&
                x.RepairJob!.Quotation!.Inspection!.Appointment!.UserId == userId)
            .OrderByDescending(x => x.PaidAt)
            .Select(x => new PurchaseHistoryRow
            {
                InvoiceId = x.Id,
                PaidAt = x.PaidAt!.Value,
                RegistrationNumber = x.RepairJob!.Quotation!.Inspection!.Appointment!.Vehicle!.RegistrationNumber,
                ServiceName = x.RepairJob.Quotation.Inspection.Appointment.WorkshopService!.Name,
                PaymentMethod = x.PaymentMethod ?? "Not recorded",
                Amount = x.Amount
            }).ToListAsync();

        DateTime start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-5);
        return View(new PurchaseHistoryVm
        {
            Purchases = purchases,
            PurchaseChart = Enumerable.Range(0, 6).Select(offset =>
            {
                DateTime month = start.AddMonths(offset);
                return new ChartPoint
                {
                    Label = month.ToString("MMM yyyy"),
                    Value = purchases.Where(x => x.PaidAt.Year == month.Year && x.PaidAt.Month == month.Month).Sum(x => x.Amount)
                };
            }).ToList()
        });
    }
}
