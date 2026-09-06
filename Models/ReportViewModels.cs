namespace AutoCare.Models;

public class ReportsVm
{
    public string PaymentPeriod { get; set; } = "daily";
    public DateTime PaymentDate { get; set; } = DateTime.Today;
    public DateTime PaymentStart { get; set; }
    public DateTime PaymentEnd { get; set; }
    public List<PaymentReportRow> Payments { get; set; } = new();

    public string PartPeriod { get; set; } = "daily";
    public DateTime PartDate { get; set; } = DateTime.Today;
    public DateTime PartStart { get; set; }
    public DateTime PartEnd { get; set; }
    public List<PartUsageReportRow> PartUsage { get; set; } = new();
    public List<ChartPoint> SalesChart { get; set; } = new();
}

public class ChartPoint
{
    public string Label { get; set; } = "";
    public decimal Value { get; set; }
}

public class PurchaseHistoryVm
{
    public List<PurchaseHistoryRow> Purchases { get; set; } = new();
    public List<ChartPoint> PurchaseChart { get; set; } = new();
}

public class PurchaseHistoryRow
{
    public int InvoiceId { get; set; }
    public DateTime PaidAt { get; set; }
    public string RegistrationNumber { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public decimal Amount { get; set; }
}

public class PaymentReportRow
{
    public int InvoiceId { get; set; }
    public DateTime PaidAt { get; set; }
    public string CustomerName { get; set; } = "";
    public string RegistrationNumber { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public decimal Amount { get; set; }
}

public class PartUsageReportRow
{
    public int SparePartId { get; set; }
    public string PartName { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public int TotalQuantityUsed { get; set; }
    public decimal TotalUsageValue { get; set; }
}
