using ITEquipmentInventory.Models;
using Microsoft.EntityFrameworkCore;

namespace ITEquipmentInventory.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Equipment> Equipment => Set<Equipment>();
    public DbSet<EquipmentReturn> EquipmentReturns => Set<EquipmentReturn>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<SecurityLog> SecurityLogs => Set<SecurityLog>();
    public DbSet<PrinterConsumable> PrinterConsumables => Set<PrinterConsumable>();
    public DbSet<ConsumableCompatiblePrinter> ConsumableCompatiblePrinters => Set<ConsumableCompatiblePrinter>();
    public DbSet<ConsumableTransaction> ConsumableTransactions => Set<ConsumableTransaction>();
    public DbSet<DeletedItem> DeletedItems => Set<DeletedItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>()
            .HasIndex(u => u.UserName)
            .IsUnique();

        modelBuilder.Entity<AppUser>()
            .Property(u => u.Role)
            .HasConversion<string>();

        modelBuilder.Entity<AppUser>()
            .Property(u => u.SecurityStamp)
            .HasMaxLength(64);

        modelBuilder.Entity<SecurityLog>()
            .HasIndex(l => l.CreatedAtUtc);

        modelBuilder.Entity<SecurityLog>()
            .HasIndex(l => l.EventType);

        modelBuilder.Entity<SecurityLog>()
            .HasOne(l => l.AppUser)
            .WithMany(u => u.SecurityLogs)
            .HasForeignKey(l => l.AppUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Employee>()
            .HasIndex(e => e.WorkerCode)
            .IsUnique();

        modelBuilder.Entity<Equipment>()
            .HasIndex(e => e.InventoryNumber)
            .IsUnique();

        modelBuilder.Entity<Employee>()
            .Property(e => e.Status)
            .HasConversion<string>();

        modelBuilder.Entity<Site>()
            .Property(s => s.Status)
            .HasConversion<string>();

        modelBuilder.Entity<Equipment>()
            .Property(e => e.Status)
            .HasConversion<string>();

        modelBuilder.Entity<Equipment>()
            .Property(e => e.EquipmentType)
            .HasConversion<string>();

        modelBuilder.Entity<EquipmentReturn>()
            .Property(e => e.PreviousStatus)
            .HasConversion<string>();

        modelBuilder.Entity<EquipmentReturn>()
            .Property(e => e.EquipmentType)
            .HasConversion<string>();

        modelBuilder.Entity<Employee>()
            .HasOne(e => e.Site)
            .WithMany(s => s.Employees)
            .HasForeignKey(e => e.SiteId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Equipment>()
            .HasOne(e => e.CurrentSite)
            .WithMany(s => s.EquipmentItems)
            .HasForeignKey(e => e.CurrentSiteId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Equipment>()
            .HasOne(e => e.CurrentEmployee)
            .WithMany()
            .HasForeignKey(e => e.CurrentEmployeeId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<PrinterConsumable>()
            .Property(c => c.Type)
            .HasConversion<string>();

        modelBuilder.Entity<PrinterConsumable>()
            .Property(c => c.Color)
            .HasConversion<string>();

        modelBuilder.Entity<PrinterConsumable>()
            .Property(c => c.IsOriginal)
            .HasDefaultValue(true);

        modelBuilder.Entity<PrinterConsumable>()
            .HasIndex(c => c.ProductCode);

        modelBuilder.Entity<PrinterConsumable>()
            .HasIndex(c => c.Name);

        modelBuilder.Entity<ConsumableCompatiblePrinter>()
            .HasOne(x => x.PrinterConsumable)
            .WithMany(x => x.CompatiblePrinters)
            .HasForeignKey(x => x.PrinterConsumableId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ConsumableCompatiblePrinter>()
            .HasIndex(x => new { x.PrinterConsumableId, x.PrinterName })
            .IsUnique();

        modelBuilder.Entity<ConsumableTransaction>()
            .Property(x => x.ConsumableType)
            .HasConversion<string>();

        modelBuilder.Entity<ConsumableTransaction>()
            .Property(x => x.Color)
            .HasConversion<string>();

        modelBuilder.Entity<ConsumableTransaction>()
            .Property(x => x.TransactionType)
            .HasConversion<string>();

        modelBuilder.Entity<ConsumableTransaction>()
            .HasOne(x => x.PrinterConsumable)
            .WithMany(x => x.Transactions)
            .HasForeignKey(x => x.PrinterConsumableId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ConsumableTransaction>()
            .HasIndex(x => x.CreatedAt);

        modelBuilder.Entity<ConsumableTransaction>()
            .HasIndex(x => x.PrinterName);

        modelBuilder.Entity<ConsumableTransaction>()
            .HasIndex(x => x.SiteId);

        modelBuilder.Entity<ConsumableTransaction>()
            .HasIndex(x => x.SiteName);

        modelBuilder.Entity<DeletedItem>()
            .HasIndex(x => x.DeletedAtUtc);

        modelBuilder.Entity<DeletedItem>()
            .HasIndex(x => x.ExpiresAtUtc);

        modelBuilder.Entity<DeletedItem>()
            .HasIndex(x => new { x.EntityType, x.OriginalId });
    }
}
