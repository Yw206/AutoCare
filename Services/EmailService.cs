using System.Net;
using System.Net.Mail;
using AutoCare.Models;
using Microsoft.Extensions.Options;

namespace AutoCare.Services;

public class EmailSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string FromName { get; set; } = "AutoCare Workshop";
    public string FromEmail { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class EmailService(IOptions<EmailSettings> options, ILogger<EmailService> logger)
{
    private readonly EmailSettings settings = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(settings.Host)
        && !string.IsNullOrWhiteSpace(settings.FromEmail)
        && !string.IsNullOrWhiteSpace(settings.Username)
        && !string.IsNullOrWhiteSpace(settings.Password);

    public async Task<bool> SendVerificationOtpAsync(string recipient, string name, string otp)
    {
        string subject = "AutoCare email verification code";
        string body = $"<h2>Verify your AutoCare account</h2><p>Hello {WebUtility.HtmlEncode(name)},</p><p>Your verification code is:</p><h1>{otp}</h1><p>This code expires in 10 minutes.</p>";
        return await SendAsync(recipient, subject, body);
    }

    public async Task<bool> SendReceiptAsync(string recipient, string name, Invoice invoice)
    {
        string subject = $"AutoCare e-receipt #{invoice.Id}";
        string body = $"<h2>AutoCare payment receipt</h2><p>Hello {WebUtility.HtmlEncode(name)},</p><p>Your Stripe test payment was recorded successfully.</p><table><tr><td>Invoice</td><td>#{invoice.Id}</td></tr><tr><td>Amount</td><td>RM {invoice.Amount:N2}</td></tr><tr><td>Payment method</td><td>{WebUtility.HtmlEncode(invoice.PaymentMethod)}</td></tr><tr><td>Payment reference</td><td>{WebUtility.HtmlEncode(invoice.PaymentReference)}</td></tr><tr><td>Paid at</td><td>{invoice.PaidAt:dd MMM yyyy HH:mm}</td></tr></table><p>Thank you for choosing AutoCare.</p>";
        return await SendAsync(recipient, subject, body);
    }

    public async Task<bool> SendPasswordResetOtpAsync(string recipient, string name, string otp)
    {
        string subject = "AutoCare password reset code";
        string body = $"<h2>Reset your AutoCare password</h2><p>Hello {WebUtility.HtmlEncode(name)},</p><p>Your password reset code is:</p><h1>{otp}</h1><p>This code expires in 10 minutes. Ignore this email if you did not request a reset.</p>";
        return await SendAsync(recipient, subject, body);
    }

    public async Task<bool> SendAppointmentReminderAsync(string recipient, string name, Appointment appointment)
    {
        string subject = "AutoCare appointment reminder";
        string body = $"<h2>Upcoming workshop appointment</h2><p>Hello {WebUtility.HtmlEncode(name)},</p><p>This is a reminder for your {WebUtility.HtmlEncode(appointment.WorkshopService?.Name)} appointment.</p><table><tr><td>Vehicle</td><td>{WebUtility.HtmlEncode(appointment.Vehicle?.RegistrationNumber)}</td></tr><tr><td>Date</td><td>{appointment.AppointmentAt:dd MMM yyyy}</td></tr><tr><td>Time</td><td>{appointment.AppointmentAt:hh:mm tt}</td></tr></table><p>See you at AutoCare Workshop.</p>";
        return await SendAsync(recipient, subject, body);
    }

    public async Task<bool> SendStatusUpdateAsync(string recipient, string name, string title, string message)
    {
        string subject = "AutoCare update: " + title;
        string body = $"<h2>{WebUtility.HtmlEncode(title)}</h2><p>Hello {WebUtility.HtmlEncode(name)},</p><p>{WebUtility.HtmlEncode(message)}</p><p>You can sign in to AutoCare to view the latest details.</p>";
        return await SendAsync(recipient, subject, body);
    }

    private async Task<bool> SendAsync(string recipient, string subject, string htmlBody)
    {
        if (!IsConfigured)
        {
            logger.LogWarning("Email is not configured. Message for {Recipient}: {Subject}", recipient, subject);
            return false;
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(settings.FromEmail, settings.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(recipient);
            using var client = new SmtpClient(settings.Host, settings.Port)
            {
                EnableSsl = settings.UseSsl,
                Credentials = new NetworkCredential(settings.Username, settings.Password)
            };
            await client.SendMailAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {Recipient}", recipient);
            return false;
        }
    }
}
