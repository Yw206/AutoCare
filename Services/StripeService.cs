using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AutoCare.Models;
using Microsoft.Extensions.Options;

namespace AutoCare.Services;

public class StripeSettings
{
    public string SecretKey { get; set; } = "";
    public string Currency { get; set; } = "myr";
}

public record StripeCheckoutResult(string SessionId, string Url);
public record StripeVerificationResult(bool Paid, int InvoiceId, string? PaymentIntent, long AmountTotal);

public class StripeService(HttpClient http, IOptions<StripeSettings> options)
{
    private readonly StripeSettings settings = options.Value;
    public bool IsConfigured => settings.SecretKey.StartsWith("sk_test_", StringComparison.Ordinal);

    private void Authorize()
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(settings.SecretKey + ":")));
    }

    public async Task<StripeCheckoutResult> CreateCheckoutAsync(Invoice invoice, string customerEmail, string successUrl, string cancelUrl)
    {
        if (!IsConfigured) throw new InvalidOperationException("Stripe test SecretKey is not configured.");
        Authorize();
        long amount = checked((long)Math.Round(invoice.Amount * 100m, MidpointRounding.AwayFromZero));
        var fields = new Dictionary<string, string>
        {
            ["mode"] = "payment",
            ["success_url"] = successUrl,
            ["cancel_url"] = cancelUrl,
            ["client_reference_id"] = invoice.Id.ToString(),
            ["customer_email"] = customerEmail,
            ["line_items[0][price_data][currency]"] = settings.Currency.ToLowerInvariant(),
            ["line_items[0][price_data][product_data][name]"] = $"AutoCare workshop invoice #{invoice.Id}",
            ["line_items[0][price_data][unit_amount]"] = amount.ToString(),
            ["line_items[0][quantity]"] = "1",
            ["metadata[invoice_id]"] = invoice.Id.ToString()
        };
        using var response = await http.PostAsync("https://api.stripe.com/v1/checkout/sessions", new FormUrlEncodedContent(fields));
        string json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Stripe could not create Checkout: " + ReadStripeError(json));
        using var doc = JsonDocument.Parse(json);
        return new StripeCheckoutResult(
            doc.RootElement.GetProperty("id").GetString()!,
            doc.RootElement.GetProperty("url").GetString()!);
    }

    public async Task<StripeVerificationResult> VerifyCheckoutAsync(string sessionId)
    {
        if (!IsConfigured) throw new InvalidOperationException("Stripe test SecretKey is not configured.");
        Authorize();
        using var response = await http.GetAsync("https://api.stripe.com/v1/checkout/sessions/" + Uri.EscapeDataString(sessionId));
        string json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Stripe could not verify Checkout: " + ReadStripeError(json));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        int invoiceId = int.Parse(root.GetProperty("client_reference_id").GetString()!);
        string? intent = root.TryGetProperty("payment_intent", out var pi) && pi.ValueKind == JsonValueKind.String ? pi.GetString() : null;
        long total = root.TryGetProperty("amount_total", out var amount) && amount.ValueKind == JsonValueKind.Number ? amount.GetInt64() : 0;
        return new StripeVerificationResult(root.GetProperty("payment_status").GetString() == "paid", invoiceId, intent, total);
    }

    private static string ReadStripeError(string json)
    {
        try { using var d = JsonDocument.Parse(json); return d.RootElement.GetProperty("error").GetProperty("message").GetString() ?? "Unknown Stripe error"; }
        catch { return "Unknown Stripe error"; }
    }
}
