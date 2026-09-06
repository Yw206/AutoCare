using AutoCare.Models;
using Microsoft.EntityFrameworkCore;

namespace AutoCare.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<WorkshopService> WorkshopServices => Set<WorkshopService>();
    public DbSet<SavedService> SavedServices => Set<SavedService>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<InspectionPhoto> InspectionPhotos => Set<InspectionPhoto>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<RepairJob> RepairJobs => Set<RepairJob>();
    public DbSet<SparePart> SpareParts => Set<SparePart>();
    public DbSet<RepairPart> RepairParts => Set<RepairPart>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ChatThread> ChatThreads => Set<ChatThread>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>().HasIndex(x => x.Email).IsUnique();
        b.Entity<Vehicle>().HasIndex(x => x.RegistrationNumber).IsUnique();
        b.Entity<SparePart>().HasIndex(x => x.PartNumber).IsUnique();
        b.Entity<SavedService>().HasIndex(x => new { x.UserId, x.WorkshopServiceId }).IsUnique();
        b.Entity<Notification>().HasOne(x => x.User).WithMany(x => x.Notifications).HasForeignKey(x => x.UserId);
        b.Entity<AuditLog>().HasOne(x => x.AdminUser).WithMany().HasForeignKey(x => x.AdminUserId);
        b.Entity<ChatThread>().HasOne(x => x.User).WithMany(x => x.ChatThreads).HasForeignKey(x => x.UserId);
        b.Entity<ChatMessage>().HasOne(x => x.ChatThread).WithMany(x => x.Messages).HasForeignKey(x => x.ChatThreadId);
        b.Entity<ChatMessage>().HasOne(x => x.SenderUser).WithMany().HasForeignKey(x => x.SenderUserId);
        b.Entity<Appointment>().HasOne(x => x.Inspection).WithOne(x => x.Appointment).HasForeignKey<Inspection>(x => x.AppointmentId);
        b.Entity<Inspection>().HasOne(x => x.Quotation).WithOne(x => x.Inspection).HasForeignKey<Quotation>(x => x.InspectionId);
        b.Entity<Quotation>().HasOne(x => x.RepairJob).WithOne(x => x.Quotation).HasForeignKey<RepairJob>(x => x.QuotationId);
        b.Entity<RepairJob>().HasOne(x => x.Invoice).WithOne(x => x.RepairJob).HasForeignKey<Invoice>(x => x.RepairJobId);

        // SQL Server rejects the default cascade paths from User -> Vehicle ->
        // Appointment and User -> Appointment. Workshop records should never be
        // deleted automatically, so every relationship uses NO ACTION.
        foreach (var foreignKey in b.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            foreignKey.DeleteBehavior = DeleteBehavior.NoAction;
        }
    }
}
