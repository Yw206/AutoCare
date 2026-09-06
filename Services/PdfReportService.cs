using System.Globalization;
using System.Text;
using AutoCare.Models;

namespace AutoCare.Services;

public static class PdfReportService
{
    public static byte[] BuildPaymentReport(string title, IEnumerable<PaymentReportRow> rows)
    {
        var lines = new List<string> { title, "Generated: " + DateTime.Now.ToString("dd MMM yyyy HH:mm"), "" };
        lines.AddRange(rows.Select(x => $"Invoice #{x.InvoiceId} | {x.PaidAt:dd MMM yyyy HH:mm} | {x.CustomerName} | {x.RegistrationNumber} | {x.ServiceName} | {x.PaymentMethod} | RM {x.Amount:N2}"));
        lines.Add("");
        lines.Add("Total revenue: RM " + rows.Sum(x => x.Amount).ToString("N2"));
        return BuildSimplePdf(lines);
    }

    public static byte[] BuildPartUsageReport(string title, IEnumerable<PartUsageReportRow> rows)
    {
        var lines = new List<string> { title, "Generated: " + DateTime.Now.ToString("dd MMM yyyy HH:mm"), "" };
        lines.AddRange(rows.Select(x => $"{x.PartName} | {x.PartNumber} | Qty {x.TotalQuantityUsed} | RM {x.TotalUsageValue:N2}"));
        lines.Add("");
        lines.Add("Total units: " + rows.Sum(x => x.TotalQuantityUsed).ToString(CultureInfo.InvariantCulture));
        lines.Add("Total usage value: RM " + rows.Sum(x => x.TotalUsageValue).ToString("N2"));
        return BuildSimplePdf(lines);
    }

    private static byte[] BuildSimplePdf(IReadOnlyList<string> lines)
    {
        string Escape(string text) => text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        var content = new StringBuilder("BT /F1 11 Tf 50 790 Td 14 TL ");
        foreach (string line in lines.Take(48))
            content.Append('(').Append(Escape(line)).Append(") Tj T* ");
        content.Append("ET");

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content.ToString())} >>\nstream\n{content}\nendstream"
        };

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        foreach (var obj in objects.Select((value, index) => new { value, number = index + 1 }))
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(obj.number).Append(" 0 obj\n").Append(obj.value).Append("\nendobj\n");
        }

        int xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (int offset in offsets.Skip(1))
            pdf.Append(offset.ToString("D10")).Append(" 00000 n \n");
        pdf.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
