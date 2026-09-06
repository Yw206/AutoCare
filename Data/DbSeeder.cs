using AutoCare.Models;
using AutoCare.Services;
using Microsoft.EntityFrameworkCore;

namespace AutoCare.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        // EnsureCreated does not add new columns to an existing assignment
        // database, so add the soft-disable flag once for older installations.
        await db.Database.ExecuteSqlRawAsync("""
            IF COL_LENGTH('SpareParts', 'IsActive') IS NULL
                ALTER TABLE [SpareParts] ADD [IsActive] bit NOT NULL
                CONSTRAINT [DF_SpareParts_IsActive] DEFAULT CAST(1 AS bit);
            IF COL_LENGTH('Users', 'PasswordResetOtpHash') IS NULL
                ALTER TABLE [Users] ADD [PasswordResetOtpHash] nvarchar(200) NULL;
            IF COL_LENGTH('Users', 'PasswordResetExpiresAt') IS NULL
                ALTER TABLE [Users] ADD [PasswordResetExpiresAt] datetime2 NULL;
            IF COL_LENGTH('Users', 'ProfilePhotoPath') IS NULL
                ALTER TABLE [Users] ADD [ProfilePhotoPath] nvarchar(300) NULL;
            IF COL_LENGTH('Users', 'ThemePreference') IS NULL
                ALTER TABLE [Users] ADD [ThemePreference] nvarchar(20) NOT NULL
                CONSTRAINT [DF_Users_ThemePreference] DEFAULT N'light';
            IF COL_LENGTH('Users', 'LanguagePreference') IS NULL
                ALTER TABLE [Users] ADD [LanguagePreference] nvarchar(10) NOT NULL
                CONSTRAINT [DF_Users_LanguagePreference] DEFAULT N'en';
            IF COL_LENGTH('SavedServices', 'Note') IS NULL
                ALTER TABLE [SavedServices] ADD [Note] nvarchar(300) NULL;
            IF COL_LENGTH('Appointments', 'ReminderSent') IS NULL
                ALTER TABLE [Appointments] ADD [ReminderSent] bit NOT NULL
                CONSTRAINT [DF_Appointments_ReminderSent] DEFAULT CAST(0 AS bit);
            IF OBJECT_ID('Notifications', 'U') IS NULL
                CREATE TABLE [Notifications] (
                    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Notifications] PRIMARY KEY,
                    [UserId] int NOT NULL,
                    [Title] nvarchar(120) NOT NULL,
                    [Message] nvarchar(600) NOT NULL,
                    [Category] nvarchar(80) NOT NULL,
                    [IsRead] bit NOT NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    CONSTRAINT [FK_Notifications_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
                );
            IF OBJECT_ID('AuditLogs', 'U') IS NULL
                CREATE TABLE [AuditLogs] (
                    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AuditLogs] PRIMARY KEY,
                    [AdminUserId] int NULL,
                    [Action] nvarchar(120) NOT NULL,
                    [EntityName] nvarchar(80) NOT NULL,
                    [EntityId] int NULL,
                    [Details] nvarchar(800) NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    CONSTRAINT [FK_AuditLogs_Users_AdminUserId] FOREIGN KEY ([AdminUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
                );
            IF OBJECT_ID('ChatThreads', 'U') IS NULL
                CREATE TABLE [ChatThreads] (
                    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ChatThreads] PRIMARY KEY,
                    [UserId] int NOT NULL,
                    [Subject] nvarchar(120) NOT NULL,
                    [IsClosed] bit NOT NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    [LastMessageAt] datetime2 NOT NULL,
                    [UserLastReadAt] datetime2 NULL,
                    [AdminLastReadAt] datetime2 NULL,
                    CONSTRAINT [FK_ChatThreads_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
                );
            IF OBJECT_ID('ChatMessages', 'U') IS NULL
                CREATE TABLE [ChatMessages] (
                    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ChatMessages] PRIMARY KEY,
                    [ChatThreadId] int NOT NULL,
                    [SenderUserId] int NOT NULL,
                    [SenderRole] int NOT NULL,
                    [Message] nvarchar(1000) NOT NULL,
                    [SentAt] datetime2 NOT NULL,
                    CONSTRAINT [FK_ChatMessages_ChatThreads_ChatThreadId] FOREIGN KEY ([ChatThreadId]) REFERENCES [ChatThreads] ([Id]) ON DELETE NO ACTION,
                    CONSTRAINT [FK_ChatMessages_Users_SenderUserId] FOREIGN KEY ([SenderUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
                );
            """);

        if (!await db.Users.AnyAsync())
        {
            db.Users.AddRange(
                new AppUser { FullName="System Administrator", Email="admin@autocare.com", Phone="0123456789", Role="Admin", IsEmailVerified=true, PasswordHash=PasswordService.Hash("Admin123!") },
                new AppUser { FullName="Demo Customer", Email="user@autocare.com", Phone="0198765432", Role="User", IsEmailVerified=true, PasswordHash=PasswordService.Hash("User123!") });
            db.Announcements.Add(new Announcement { Title="Welcome to AutoCare", Content="Online appointment booking is now available." });
            await db.SaveChangesAsync();
        }

        // Add useful assignment test data without duplicating records in an
        // existing database. Twelve services make AJAX pagination visible.
        var serviceSeeds = new[]
        {
            new WorkshopService { Name="Engine Oil Service", Description="Replace engine oil and oil filter", EstimatedPrice=180, EstimatedMinutes=60 },
            new WorkshopService { Name="Brake Inspection", Description="Inspect brake pads, discs and brake fluid", EstimatedPrice=80, EstimatedMinutes=45 },
            new WorkshopService { Name="General Maintenance", Description="Complete scheduled vehicle inspection and maintenance", EstimatedPrice=250, EstimatedMinutes=120 },
            new WorkshopService { Name="Tyre Rotation and Balancing", Description="Rotate tyres and balance all four wheels", EstimatedPrice=90, EstimatedMinutes=60 },
            new WorkshopService { Name="Wheel Alignment", Description="Computerised front and rear wheel alignment", EstimatedPrice=75, EstimatedMinutes=45 },
            new WorkshopService { Name="Battery Diagnostic", Description="Test battery health, charging voltage and starting performance", EstimatedPrice=40, EstimatedMinutes=30 },
            new WorkshopService { Name="Air Conditioning Service", Description="Inspect cooling performance and service the air-conditioning system", EstimatedPrice=150, EstimatedMinutes=90 },
            new WorkshopService { Name="Coolant Flush", Description="Drain old coolant and refill the engine cooling system", EstimatedPrice=120, EstimatedMinutes=60 },
            new WorkshopService { Name="Transmission Fluid Service", Description="Inspect and replace automatic transmission fluid", EstimatedPrice=280, EstimatedMinutes=90 },
            new WorkshopService { Name="Suspension Inspection", Description="Inspect absorbers, bushes, links and suspension condition", EstimatedPrice=100, EstimatedMinutes=60 },
            new WorkshopService { Name="Spark Plug Replacement", Description="Replace spark plugs and inspect engine ignition condition", EstimatedPrice=160, EstimatedMinutes=60 },
            new WorkshopService { Name="Computer Diagnostic Scan", Description="Scan vehicle control units and provide a diagnostic report", EstimatedPrice=70, EstimatedMinutes=45 }
        };
        var existingServiceNames = await db.WorkshopServices.Select(x => x.Name).ToHashSetAsync();
        db.WorkshopServices.AddRange(serviceSeeds.Where(x => !existingServiceNames.Contains(x.Name)));

        var partSeeds = new[]
        {
            new SparePart { Name="Oil Filter", PartNumber="OF-001", Quantity=30, UnitPrice=35, ReorderLevel=8 },
            new SparePart { Name="Brake Pad Set", PartNumber="BP-001", Quantity=15, UnitPrice=180, ReorderLevel=5 },
            new SparePart { Name="Fully Synthetic Engine Oil 4L", PartNumber="EO-004", Quantity=25, UnitPrice=165, ReorderLevel=8 },
            new SparePart { Name="Engine Air Filter", PartNumber="AF-001", Quantity=20, UnitPrice=55, ReorderLevel=6 },
            new SparePart { Name="Cabin Air Filter", PartNumber="CF-001", Quantity=18, UnitPrice=48, ReorderLevel=5 },
            new SparePart { Name="Spark Plug Set", PartNumber="SP-004", Quantity=14, UnitPrice=140, ReorderLevel=4 },
            new SparePart { Name="Maintenance-Free Battery", PartNumber="BT-055", Quantity=10, UnitPrice=320, ReorderLevel=3 },
            new SparePart { Name="Long-Life Coolant 4L", PartNumber="CL-004", Quantity=16, UnitPrice=75, ReorderLevel=5 },
            new SparePart { Name="Automatic Transmission Fluid 4L", PartNumber="ATF-004", Quantity=12, UnitPrice=190, ReorderLevel=4 },
            new SparePart { Name="Front Wiper Blade Set", PartNumber="WB-001", Quantity=22, UnitPrice=65, ReorderLevel=6 },
            new SparePart { Name="DOT 4 Brake Fluid", PartNumber="BF-001", Quantity=18, UnitPrice=45, ReorderLevel=5 },
            new SparePart { Name="Engine Drive Belt", PartNumber="DB-001", Quantity=9, UnitPrice=125, ReorderLevel=3 },
            new SparePart { Name="Fuel Filter", PartNumber="FF-001", Quantity=11, UnitPrice=85, ReorderLevel=3 },
            new SparePart { Name="Headlamp Bulb", PartNumber="HL-001", Quantity=24, UnitPrice=38, ReorderLevel=6 },
            new SparePart { Name="Shock Absorber", PartNumber="SA-001", Quantity=8, UnitPrice=260, ReorderLevel=3 }
        };
        var existingPartNumbers = await db.SpareParts.Select(x => x.PartNumber).ToHashSetAsync();
        db.SpareParts.AddRange(partSeeds.Where(x => !existingPartNumbers.Contains(x.PartNumber)));
        await db.SaveChangesAsync();

        // Repair older records that were completed or paid before status
        // synchronization was introduced.
        var finishedRepairs = await db.RepairJobs
            .Include(x => x.Invoice)
            .Include(x => x.Quotation)!.ThenInclude(x => x.Inspection)!.ThenInclude(x => x.Appointment)
            .Where(x => x.Status != RepairStatus.Completed &&
                ((x.Invoice != null && x.Invoice.PaymentStatus == PaymentStatus.Paid) ||
                 x.Quotation!.Inspection!.Appointment!.Status == AppointmentStatus.Completed))
            .ToListAsync();

        foreach (var repair in finishedRepairs)
        {
            repair.Status = RepairStatus.Completed;
            repair.CompletedAt ??= repair.Invoice?.PaidAt ?? DateTime.Now;
            if (repair.Quotation?.Inspection?.Appointment != null)
                repair.Quotation.Inspection.Appointment.Status = AppointmentStatus.Completed;
        }

        if (finishedRepairs.Count > 0) await db.SaveChangesAsync();
    }
}
