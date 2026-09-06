using System.Security.Claims;
using AutoCare.Data;
using AutoCare.Models;
using AutoCare.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoCare.Controllers;

[Authorize(Roles = "User")]
public class PaymentController(AppDbContext db, StripeService stripe, EmailService emailService) : Controller
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(int invoiceId)
    {
        var invoice = await UserInvoice(invoiceId);
        if (invoice == null) return NotFound();
        if (invoice.PaymentStatus == PaymentStatus.Paid) return RedirectToAction("Index", "Dashboard");
        if (!stripe.IsConfigured)
        {
            TempData["Error"] = "Stripe test mode is not configured. Add Stripe:SecretKey using User Secrets.";
            return RedirectToAction("Index", "Dashboard");
        }

        string success = Url.Action(nameof(Success), "Payment", null, Request.Scheme)! + "?session_id={CHECKOUT_SESSION_ID}";
        string cancel = Url.Action(nameof(Cancel), "Payment", new { invoiceId }, Request.Scheme)!;
        try
        {
            var session = await stripe.CreateCheckoutAsync(invoice, invoice.RepairJob!.Quotation!.Inspection!.Appointment!.User!.Email, success, cancel);
            invoice.StripeCheckoutSessionId = session.SessionId;
            await db.SaveChangesAsync();
            return Redirect(session.Url);
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction("Index", "Dashboard");
        }
    }

    public async Task<IActionResult> Success(string session_id)
    {
        if (string.IsNullOrWhiteSpace(session_id)) return BadRequest();
        try
        {
            var result = await stripe.VerifyCheckoutAsync(session_id);
            var invoice = await UserInvoice(result.InvoiceId);
            if (invoice == null || invoice.StripeCheckoutSessionId != session_id) return Forbid();
            long expected = checked((long)Math.Round(invoice.Amount * 100m, MidpointRounding.AwayFromZero));
            if (!result.Paid || result.AmountTotal != expected)
            {
                ViewBag.Message = "Stripe has not confirmed the payment.";
                return View(false);
            }

            bool newlyPaid = invoice.PaymentStatus != PaymentStatus.Paid;
            invoice.PaymentStatus = PaymentStatus.Paid;
            invoice.PaymentMethod = "Stripe Checkout";
            invoice.PaidAt = DateTime.Now;
            invoice.PaymentReference = result.PaymentIntent ?? session_id;

            // A paid invoice belongs to a completed service in this assignment workflow.
            var repair = invoice.RepairJob;
            if (repair != null)
            {
                repair.Status = RepairStatus.Completed;
                repair.CompletedAt ??= DateTime.Now;
                var appointment = repair.Quotation?.Inspection?.Appointment;
                if (appointment != null) appointment.Status = AppointmentStatus.Completed;
            }
            await db.SaveChangesAsync();
            if (newlyPaid)
            {
                var user = invoice.RepairJob!.Quotation!.Inspection!.Appointment!.User!;
                await emailService.SendReceiptAsync(user.Email, user.FullName, invoice);
            }
            ViewBag.Invoice = invoice;
            ViewBag.QrCode = QrCodeService.SvgDataUri(ReceiptQrPayload(invoice));
            return View(true);
        }
        catch (Exception ex)
        {
            ViewBag.Message = ex.Message;
            return View(false);
        }
    }

    public IActionResult Cancel(int invoiceId)
    {
        ViewBag.InvoiceId = invoiceId;
        return View();
    }

    public async Task<IActionResult> Invoice(int id)
    {
        var invoice = await UserInvoice(id);
        if (invoice == null) return NotFound();
        ViewBag.QrCode = QrCodeService.SvgDataUri(ReceiptQrPayload(invoice));
        return View(invoice);
    }

    private static string ReceiptQrPayload(Invoice invoice)
    {
        var appointment = invoice.RepairJob?.Quotation?.Inspection?.Appointment;
        return string.Join(Environment.NewLine,
            "AutoCare Receipt",
            $"Invoice #{invoice.Id}",
            $"Customer: {appointment?.User?.FullName ?? "N/A"}",
            $"Vehicle: {appointment?.Vehicle?.RegistrationNumber ?? "N/A"}",
            $"Service: {appointment?.WorkshopService?.Name ?? "N/A"}",
            $"Amount: RM {invoice.Amount:N2}",
            $"Status: {invoice.PaymentStatus}",
            $"Method: {invoice.PaymentMethod ?? "N/A"}",
            $"Reference: {invoice.PaymentReference ?? "N/A"}",
            $"Paid: {invoice.PaidAt?.ToString("dd MMM yyyy HH:mm") ?? "N/A"}");
    }

    private Task<Invoice?> UserInvoice(int id) => db.Invoices
        .Include(x => x.RepairJob)!.ThenInclude(x => x.Quotation)!.ThenInclude(x => x.Inspection)!
        .ThenInclude(x => x.Appointment)!.ThenInclude(x => x.User)
        .Include(x => x.RepairJob)!.ThenInclude(x => x.Quotation)!.ThenInclude(x => x.Inspection)!
        .ThenInclude(x => x.Appointment)!.ThenInclude(x => x.Vehicle)
        .Include(x => x.RepairJob)!.ThenInclude(x => x.Quotation)!.ThenInclude(x => x.Inspection)!
        .ThenInclude(x => x.Appointment)!.ThenInclude(x => x.WorkshopService)
        .FirstOrDefaultAsync(x => x.Id == id && x.RepairJob!.Quotation!.Inspection!.Appointment!.UserId == UserId);
}
