using AutoCare.Data;
using AutoCare.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace AutoCare.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController(AppDbContext db, IWebHostEnvironment environment) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.Users = await db.Users.CountAsync(x => x.Role == "User");
        ViewBag.Pending = await db.Appointments.CountAsync(x => x.Status == AppointmentStatus.Pending);
        ViewBag.Repairs = await db.RepairJobs.CountAsync(x => x.Status != RepairStatus.Completed && x.Status != RepairStatus.Cancelled);
        ViewBag.LowStock = await db.SpareParts.CountAsync(x => x.Quantity <= x.ReorderLevel);
        ViewBag.Parts = await db.SpareParts.Where(x => x.IsActive && x.Quantity > 0).OrderBy(x => x.Name).ToListAsync();

        return View(await db.Appointments
            .Include(x => x.User).Include(x => x.Vehicle).Include(x => x.WorkshopService)
            .Include(x => x.Inspection)!.ThenInclude(x => x.Photos)
            .Include(x => x.Inspection)!.ThenInclude(x => x.Quotation)!.ThenInclude(x => x.RepairJob)!.ThenInclude(x => x.Invoice)
            .Include(x => x.Inspection)!.ThenInclude(x => x.Quotation)!.ThenInclude(x => x.RepairJob)!.ThenInclude(x => x.RepairParts).ThenInclude(x => x.SparePart)
            .OrderByDescending(x => x.CreatedAt).ToListAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAppointmentStatus(int id, AppointmentStatus status, string? remark)
    {
        var appointment = await db.Appointments
            .Include(x => x.Inspection)!.ThenInclude(x => x.Quotation)!.ThenInclude(x => x.RepairJob)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (appointment == null) return NotFound();
        if (appointment.Status == AppointmentStatus.Completed && status != AppointmentStatus.Completed)
        {
            TempData["Error"] = "A completed service is locked and cannot return to an earlier status.";
            return RedirectToAction(nameof(Index));
        }
        appointment.Status = status; appointment.AdminRemark = remark;

        // Keep the simple appointment status and the internal repair record in sync.
        // This prevents a completed appointment from appearing as an unfinished repair.
        var repair = appointment.Inspection?.Quotation?.RepairJob;
        if (status == AppointmentStatus.Completed && repair != null)
        {
            repair.Status = RepairStatus.Completed;
            repair.CompletedAt ??= DateTime.Now;
            if (!await db.Invoices.AnyAsync(x => x.RepairJobId == repair.Id))
                db.Invoices.Add(new Invoice { RepairJobId = repair.Id, Amount = repair.Quotation!.Total });
        }
        await db.SaveChangesAsync(); return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(45_000_000)]
    public async Task<IActionResult> CreateInspection(int appointmentId, string findings, string? recommendations, int mileage, List<IFormFile> photos)
    {
        if (string.IsNullOrWhiteSpace(findings)) return RedirectToAction(nameof(Index));
        if (await db.Inspections.AnyAsync(x => x.AppointmentId == appointmentId))
        {
            TempData["Error"] = "This appointment already has an inspection.";
            return RedirectToAction(nameof(Index));
        }

        var inspection = new Inspection { AppointmentId = appointmentId, Findings = findings, Recommendations = recommendations, Mileage = mileage };
        db.Inspections.Add(inspection);
        var appointment = await db.Appointments.FindAsync(appointmentId);
        if (appointment != null) appointment.Status = AppointmentStatus.Arrived;
        await db.SaveChangesAsync();

        string[] permitted = [".jpg", ".jpeg", ".png", ".webp"];
        string folder = Path.Combine(environment.WebRootPath, "uploads", "inspections", inspection.Id.ToString());
        Directory.CreateDirectory(folder);
        foreach (var photo in photos.Take(8))
        {
            string extension = Path.GetExtension(photo.FileName).ToLowerInvariant();
            if (photo.Length == 0 || photo.Length > 5_000_000 || !permitted.Contains(extension)) continue;
            string fileName = Guid.NewGuid().ToString("N") + extension;
            await using var stream = System.IO.File.Create(Path.Combine(folder, fileName));
            await photo.CopyToAsync(stream);
            inspection.Photos.Add(new InspectionPhoto
            {
                FilePath = $"/uploads/inspections/{inspection.Id}/{fileName}",
                OriginalFileName = Path.GetFileName(photo.FileName)
            });
        }
        await db.SaveChangesAsync();
        TempData["Success"] = $"Inspection saved with {inspection.Photos.Count} photo(s).";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateQuotation(int inspectionId, decimal serviceCharge, decimal partsCharge, decimal labourCharge, decimal discount)
    {
        if (!await db.Quotations.AnyAsync(x => x.InspectionId == inspectionId))
        {
            db.Quotations.Add(new Quotation { InspectionId = inspectionId, ServiceCharge = Math.Max(0, serviceCharge), PartsCharge = Math.Max(0, partsCharge), LabourCharge = Math.Max(0, labourCharge), Discount = Math.Max(0, discount) });
            await db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UsePart(int repairJobId, int sparePartId, int quantity)
    {
        if (quantity < 1) { TempData["Error"] = "Quantity must be at least one."; return RedirectToAction(nameof(Index)); }
        await using var transaction = await db.Database.BeginTransactionAsync();
        var repair = await db.RepairJobs.FindAsync(repairJobId);
        var part = await db.SpareParts.FindAsync(sparePartId);
        if (repair == null || part == null) return NotFound();
        if (repair.Status == RepairStatus.Completed)
        {
            TempData["Error"] = "Parts cannot be added after the service is completed.";
            return RedirectToAction(nameof(Index));
        }
        if (part.Quantity < quantity)
        {
            TempData["Error"] = $"Not enough stock for {part.Name}. Available: {part.Quantity}.";
            return RedirectToAction(nameof(Index));
        }
        part.Quantity -= quantity;
        var used = await db.RepairParts.FirstOrDefaultAsync(x => x.RepairJobId == repairJobId && x.SparePartId == sparePartId);
        if (used == null) db.RepairParts.Add(new RepairPart { RepairJobId = repairJobId, SparePartId = sparePartId, QuantityUsed = quantity, UnitPriceAtUse = part.UnitPrice });
        else used.QuantityUsed += quantity;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["Success"] = $"{quantity} × {part.Name} added. Stock deducted automatically.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRepairStatus(int id, RepairStatus status, string? note)
    {
        // Only the three assignment-level progress choices shown in the UI are accepted.
        if (status != RepairStatus.RepairInProgress &&
            status != RepairStatus.ReadyForCollection &&
            status != RepairStatus.Completed)
        {
            TempData["Error"] = "Please select a valid service progress.";
            return RedirectToAction(nameof(Index));
        }

        var repair = await db.RepairJobs
            .Include(x => x.Quotation)!.ThenInclude(x => x.Inspection)!.ThenInclude(x => x.Appointment)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (repair == null) return NotFound();
        if (repair.Status == RepairStatus.Completed && status != RepairStatus.Completed)
        {
            TempData["Error"] = "A completed service is locked and cannot return to an earlier progress.";
            return RedirectToAction(nameof(Index));
        }
        repair.Status = status; repair.ProgressNote = note;
        var appointment = repair.Quotation?.Inspection?.Appointment;
        if (status == RepairStatus.RepairInProgress && appointment != null)
            appointment.Status = AppointmentStatus.Arrived;
        if (status == RepairStatus.Completed)
        {
            repair.CompletedAt = DateTime.Now;
            if (appointment != null) appointment.Status = AppointmentStatus.Completed;
            if (!await db.Invoices.AnyAsync(x => x.RepairJobId == id)) db.Invoices.Add(new Invoice { RepairJobId = id, Amount = repair.Quotation!.Total });
        }
        await db.SaveChangesAsync(); return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PayInvoice(int id, string method)
    {
        var invoice = await db.Invoices.FindAsync(id); if (invoice == null) return NotFound();
        invoice.PaymentStatus = PaymentStatus.Paid; invoice.PaymentMethod = method; invoice.PaidAt = DateTime.Now;
        await db.SaveChangesAsync(); return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Services() => View(await db.WorkshopServices.OrderBy(x => x.Name).ToListAsync());
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveService(WorkshopService model)
    {
        if (!ModelState.IsValid) return View("Services", await db.WorkshopServices.ToListAsync());
        if (model.Id == 0) db.Add(model);
        else { var item = await db.WorkshopServices.FindAsync(model.Id); if (item == null) return NotFound(); item.Name = model.Name; item.Description = model.Description; item.EstimatedPrice = model.EstimatedPrice; item.EstimatedMinutes = model.EstimatedMinutes; item.IsActive = model.IsActive; }
        await db.SaveChangesAsync(); return RedirectToAction(nameof(Services));
    }

    public async Task<IActionResult> Parts() => View(await db.SpareParts.OrderBy(x => x.Name).ToListAsync());
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePart(SparePart model)
    {
        if (!ModelState.IsValid) return View("Parts", await db.SpareParts.ToListAsync());
        model.Name = model.Name.Trim();
        model.PartNumber = model.PartNumber.Trim().ToUpperInvariant();
        bool duplicateNumber = await db.SpareParts.AnyAsync(x => x.PartNumber == model.PartNumber && x.Id != model.Id);
        if (duplicateNumber)
        {
            TempData["Error"] = $"Part number {model.PartNumber} already exists. Use a different part number or update the existing stock record.";
            return RedirectToAction(nameof(Parts));
        }
        if (model.Id == 0) db.Add(model);
        else { var item = await db.SpareParts.FindAsync(model.Id); if (item == null) return NotFound(); item.Name = model.Name; item.PartNumber = model.PartNumber; item.Quantity = model.Quantity; item.UnitPrice = model.UnitPrice; item.ReorderLevel = model.ReorderLevel; }
        try
        {
            await db.SaveChangesAsync();
            TempData["Success"] = model.Id == 0 ? "Part added to inventory." : "Part updated.";
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "The part could not be saved. Check that the part number is unique and all values are valid.";
        }
        return RedirectToAction(nameof(Parts));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RestockPart(int id, int quantity)
    {
        if (quantity < 1 || quantity > 100000)
        {
            TempData["Error"] = "Restock quantity must be between 1 and 100,000.";
            return RedirectToAction(nameof(Parts));
        }

        var part = await db.SpareParts.FindAsync(id);
        if (part == null) return NotFound();
        if (part.Quantity > 100000 - quantity)
        {
            TempData["Error"] = "The resulting stock quantity would be too large.";
            return RedirectToAction(nameof(Parts));
        }

        part.Quantity += quantity;
        await db.SaveChangesAsync();
        TempData["Success"] = $"Added {quantity} unit(s) to {part.Name}. Current stock: {part.Quantity}.";
        return RedirectToAction(nameof(Parts));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePart(int id)
    {
        var part = await db.SpareParts.FindAsync(id);
        if (part == null) return NotFound();
        part.IsActive = !part.IsActive;
        await db.SaveChangesAsync();
        TempData["Success"] = part.IsActive
            ? $"{part.Name} is enabled and can be selected again."
            : $"{part.Name} is disabled and hidden from repair selection.";
        return RedirectToAction(nameof(Parts));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePart(int id)
    {
        var part = await db.SpareParts.FindAsync(id);
        if (part == null) return NotFound();
        if (await db.RepairParts.AnyAsync(x => x.SparePartId == id))
        {
            TempData["Error"] = $"{part.Name} has repair history and cannot be deleted. Disable it instead.";
            return RedirectToAction(nameof(Parts));
        }

        db.SpareParts.Remove(part);
        await db.SaveChangesAsync();
        TempData["Success"] = $"{part.Name} was deleted.";
        return RedirectToAction(nameof(Parts));
    }

    public async Task<IActionResult> Reports(string paymentPeriod = "daily", DateTime? paymentDate = null,
        string partPeriod = "daily", DateTime? partDate = null)
    {
        paymentPeriod = NormalPeriod(paymentPeriod);
        partPeriod = NormalPeriod(partPeriod);
        DateTime selectedPaymentDate = (paymentDate ?? DateTime.Today).Date;
        DateTime selectedPartDate = (partDate ?? DateTime.Today).Date;
        var paymentRange = ReportRange(paymentPeriod, selectedPaymentDate);
        var partRange = ReportRange(partPeriod, selectedPartDate);

        DateTime chartStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-5);
        var chartPayments = await db.Invoices
            .Where(x => x.PaymentStatus == PaymentStatus.Paid && x.PaidAt.HasValue && x.PaidAt.Value >= chartStart)
            .Select(x => new { PaidAt = x.PaidAt!.Value, x.Amount }).ToListAsync();
        var salesChart = Enumerable.Range(0, 6).Select(offset =>
        {
            DateTime month = chartStart.AddMonths(offset);
            return new ChartPoint
            {
                Label = month.ToString("MMM yyyy"),
                Value = chartPayments.Where(x => x.PaidAt.Year == month.Year && x.PaidAt.Month == month.Month).Sum(x => x.Amount)
            };
        }).ToList();

        return View(new ReportsVm
        {
            PaymentPeriod = paymentPeriod,
            PaymentDate = selectedPaymentDate,
            PaymentStart = paymentRange.Start,
            PaymentEnd = paymentRange.End,
            Payments = await PaymentRows(paymentRange.Start, paymentRange.End),
            PartPeriod = partPeriod,
            PartDate = selectedPartDate,
            PartStart = partRange.Start,
            PartEnd = partRange.End,
            PartUsage = await PartUsageRows(partRange.Start, partRange.End),
            SalesChart = salesChart
        });
    }

    public async Task<IActionResult> ExportPaymentReport(string period = "daily", DateTime? date = null)
    {
        period = NormalPeriod(period);
        DateTime selected = (date ?? DateTime.Today).Date;
        var range = ReportRange(period, selected);
        var rows = await PaymentRows(range.Start, range.End);
        var csv = new StringBuilder("Invoice,Paid At,Customer,Vehicle,Service,Payment Method,Amount (RM)\r\n");
        foreach (var row in rows)
            csv.AppendLine($"{row.InvoiceId},{Csv(row.PaidAt.ToString("yyyy-MM-dd HH:mm"))},{Csv(row.CustomerName)},{Csv(row.RegistrationNumber)},{Csv(row.ServiceName)},{Csv(row.PaymentMethod)},{row.Amount:F2}");
        csv.AppendLine($",,,,,Total,{rows.Sum(x => x.Amount):F2}");
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv",
            $"payment-report-{period}-{selected:yyyy-MM-dd}.csv");
    }

    public async Task<IActionResult> ExportPartUsageReport(string period = "daily", DateTime? date = null)
    {
        period = NormalPeriod(period);
        DateTime selected = (date ?? DateTime.Today).Date;
        var range = ReportRange(period, selected);
        var rows = await PartUsageRows(range.Start, range.End);
        var csv = new StringBuilder("Part,Part Number,Quantity Used,Usage Value (RM)\r\n");
        foreach (var row in rows)
            csv.AppendLine($"{Csv(row.PartName)},{Csv(row.PartNumber)},{row.TotalQuantityUsed},{row.TotalUsageValue:F2}");
        csv.AppendLine($"Total,,{rows.Sum(x => x.TotalQuantityUsed)},{rows.Sum(x => x.TotalUsageValue):F2}");
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv",
            $"part-usage-report-{period}-{selected:yyyy-MM-dd}.csv");
    }

    private async Task<List<PaymentReportRow>> PaymentRows(DateTime start, DateTime end) =>
        await db.Invoices
            .Where(x => x.PaymentStatus == PaymentStatus.Paid && x.PaidAt.HasValue && x.PaidAt.Value >= start && x.PaidAt.Value < end)
            .OrderByDescending(x => x.PaidAt)
            .Select(x => new PaymentReportRow
            {
                InvoiceId = x.Id,
                PaidAt = x.PaidAt!.Value,
                CustomerName = x.RepairJob!.Quotation!.Inspection!.Appointment!.User!.FullName,
                RegistrationNumber = x.RepairJob.Quotation.Inspection.Appointment.Vehicle!.RegistrationNumber,
                ServiceName = x.RepairJob.Quotation.Inspection.Appointment.WorkshopService!.Name,
                PaymentMethod = x.PaymentMethod ?? "Not recorded",
                Amount = x.Amount
            }).ToListAsync();

    private async Task<List<PartUsageReportRow>> PartUsageRows(DateTime start, DateTime end)
    {
        var rows = await db.RepairParts.Include(x => x.SparePart)
            .Where(x => x.AddedAt >= start && x.AddedAt < end).ToListAsync();
        return rows.GroupBy(x => new { x.SparePartId, x.SparePart!.Name, x.SparePart.PartNumber })
            .Select(g => new PartUsageReportRow
            {
                SparePartId = g.Key.SparePartId,
                PartName = g.Key.Name,
                PartNumber = g.Key.PartNumber,
                TotalQuantityUsed = g.Sum(x => x.QuantityUsed),
                TotalUsageValue = g.Sum(x => x.QuantityUsed * x.UnitPriceAtUse)
            }).OrderByDescending(x => x.TotalQuantityUsed).ThenBy(x => x.PartName).ToList();
    }

    private static string NormalPeriod(string period) =>
        string.Equals(period, "monthly", StringComparison.OrdinalIgnoreCase) ? "monthly" : "daily";

    private static (DateTime Start, DateTime End) ReportRange(string period, DateTime selected)
    {
        DateTime start = period == "monthly" ? new DateTime(selected.Year, selected.Month, 1) : selected.Date;
        return (start, period == "monthly" ? start.AddMonths(1) : start.AddDays(1));
    }

    private static string Csv(string value)
    {
        if (value.Length > 0 && "=+-@".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public async Task<IActionResult> Users() => View(await db.Users.Where(x => x.Role == "User").OrderBy(x => x.FullName).ToListAsync());
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleUser(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user != null && user.Role == "User") { user.IsActive = !user.IsActive; await db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Users));
    }
}
