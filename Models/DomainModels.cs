using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCare.Models;

public enum AppointmentStatus { Pending, Approved, Rejected, Arrived, Cancelled, Completed }
public enum QuoteStatus { Pending, Approved, Rejected }
public enum RepairStatus { WaitingForInspection, WaitingForApproval, RepairInProgress, WaitingForParts, QualityChecking, ReadyForCollection, Completed, Cancelled }
public enum PaymentStatus { Unpaid, PartiallyPaid, Paid, Refunded }

public class AppUser
{
    public int Id { get; set; }
    [Required, StringLength(80)] public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(120)] public string Email { get; set; } = "";
    [StringLength(20)] public string? Phone { get; set; }
    [Required] public string PasswordHash { get; set; } = "";
    [Required, StringLength(10)] public string Role { get; set; } = "User";
    public bool IsActive { get; set; } = true;
    public bool IsEmailVerified { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEnd { get; set; }
    [StringLength(200)] public string? EmailVerificationOtpHash { get; set; }
    public DateTime? EmailVerificationExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
    public ICollection<SavedService> SavedServices { get; set; } = new List<SavedService>();
}

public class Vehicle
{
    public int Id { get; set; }
    [Required] public int UserId { get; set; }
    public AppUser? User { get; set; }
    [Required, StringLength(20)] public string RegistrationNumber { get; set; } = "";
    [Required, StringLength(50)] public string Brand { get; set; } = "";
    [Required, StringLength(50)] public string Model { get; set; } = "";
    [Range(1950, 2100)] public int Year { get; set; }
    [Range(0, 3000000)] public int Mileage { get; set; }
    [StringLength(30)] public string? Colour { get; set; }
}

public class WorkshopService
{
    public int Id { get; set; }
    [Required, StringLength(80)] public string Name { get; set; } = "";
    [StringLength(400)] public string? Description { get; set; }
    [Column(TypeName="decimal(10,2)"), Range(0, 999999)] public decimal EstimatedPrice { get; set; }
    [Range(15, 1440)] public int EstimatedMinutes { get; set; }
    public bool IsActive { get; set; } = true;
}

public class SavedService
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public int WorkshopServiceId { get; set; }
    public WorkshopService? WorkshopService { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.Now;
}

public class Appointment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public int WorkshopServiceId { get; set; }
    public WorkshopService? WorkshopService { get; set; }
    [DataType(DataType.DateTime)] public DateTime AppointmentAt { get; set; }
    [Required, StringLength(800)] public string ProblemDescription { get; set; } = "";
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;
    [StringLength(300)] public string? AdminRemark { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public Inspection? Inspection { get; set; }
}

public class Inspection
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public Appointment? Appointment { get; set; }
    [Required, StringLength(1200)] public string Findings { get; set; } = "";
    [StringLength(1200)] public string? Recommendations { get; set; }
    public int Mileage { get; set; }
    public DateTime InspectedAt { get; set; } = DateTime.Now;
    public Quotation? Quotation { get; set; }
    public ICollection<InspectionPhoto> Photos { get; set; } = new List<InspectionPhoto>();
}

public class InspectionPhoto
{
    public int Id { get; set; }
    public int InspectionId { get; set; }
    public Inspection? Inspection { get; set; }
    [Required, StringLength(300)] public string FilePath { get; set; } = "";
    [Required, StringLength(180)] public string OriginalFileName { get; set; } = "";
    public DateTime UploadedAt { get; set; } = DateTime.Now;
}

public class Quotation
{
    public int Id { get; set; }
    public int InspectionId { get; set; }
    public Inspection? Inspection { get; set; }
    [Column(TypeName="decimal(10,2)")] public decimal ServiceCharge { get; set; }
    [Column(TypeName="decimal(10,2)")] public decimal PartsCharge { get; set; }
    [Column(TypeName="decimal(10,2)")] public decimal LabourCharge { get; set; }
    [Column(TypeName="decimal(10,2)")] public decimal Discount { get; set; }
    [NotMapped] public decimal Total => ServiceCharge + PartsCharge + LabourCharge - Discount;
    public DateTime ExpiresAt { get; set; } = DateTime.Today.AddDays(7);
    public QuoteStatus Status { get; set; } = QuoteStatus.Pending;
    public RepairJob? RepairJob { get; set; }
}

public class RepairJob
{
    public int Id { get; set; }
    public int QuotationId { get; set; }
    public Quotation? Quotation { get; set; }
    public RepairStatus Status { get; set; } = RepairStatus.RepairInProgress;
    [StringLength(1000)] public string? ProgressNote { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
    public Invoice? Invoice { get; set; }
    public ICollection<RepairPart> RepairParts { get; set; } = new List<RepairPart>();
}

public class SparePart
{
    public int Id { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Required, StringLength(40)] public string PartNumber { get; set; } = "";
    [Range(0, 100000)] public int Quantity { get; set; }
    [Column(TypeName="decimal(10,2)")] public decimal UnitPrice { get; set; }
    public int ReorderLevel { get; set; } = 5;
    public bool IsActive { get; set; } = true;
}

public class RepairPart
{
    public int Id { get; set; }
    public int RepairJobId { get; set; }
    public RepairJob? RepairJob { get; set; }
    public int SparePartId { get; set; }
    public SparePart? SparePart { get; set; }
    [Range(1, 10000)] public int QuantityUsed { get; set; }
    [Column(TypeName="decimal(10,2)")] public decimal UnitPriceAtUse { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
}

public class Invoice
{
    public int Id { get; set; }
    public int RepairJobId { get; set; }
    public RepairJob? RepairJob { get; set; }
    [Column(TypeName="decimal(10,2)")] public decimal Amount { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;
    public string? PaymentMethod { get; set; }
    public DateTime IssuedAt { get; set; } = DateTime.Now;
    public DateTime? PaidAt { get; set; }
    [StringLength(120)] public string? StripeCheckoutSessionId { get; set; }
    [StringLength(120)] public string? PaymentReference { get; set; }
}

public class Announcement
{
    public int Id { get; set; }
    [Required, StringLength(120)] public string Title { get; set; } = "";
    [Required, StringLength(1500)] public string Content { get; set; } = "";
    public DateTime PublishedAt { get; set; } = DateTime.Now;
}

public class Feedback
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    [Range(1,5)] public int Rating { get; set; }
    [Required, StringLength(800)] public string Comment { get; set; } = "";
    public string? AdminResponse { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.Now;
}
