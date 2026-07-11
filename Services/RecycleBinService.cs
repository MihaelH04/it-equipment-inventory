using System.Text.Json;
using ITEquipmentInventory.Data;
using ITEquipmentInventory.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ITEquipmentInventory.Services;

public class RecycleBinService
{
    private readonly AppDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public RecycleBinService(AppDbContext context, IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
    }

    public void MoveEquipment(Equipment item)
    {
        AddDeletedItem("Equipment", "Oprema", item.Id,
            BuildName(item.InventoryNumber, item.Name, item.SerialNumber),
            new EquipmentSnapshot(item.Id, item.InventoryNumber, item.Name, item.SerialNumber,
                item.EquipmentType, item.Status, item.CurrentEmployeeId, item.CurrentSiteId,
                item.AssignedAt, item.ReturnedAt, item.HandedOverBy));
        _context.Equipment.Remove(item);
    }

    public void MoveEmployee(Employee item, IReadOnlyCollection<int> assignedEquipmentIds)
    {
        AddDeletedItem("Employee", "Zaposlenici", item.Id,
            BuildName(item.WorkerCode, item.FullName),
            new EmployeeSnapshot(item.Id, item.WorkerCode, item.FullName, item.SiteId,
                item.Status, item.CreatedAt, assignedEquipmentIds.ToArray()));
        _context.Employees.Remove(item);
    }

    public void MoveSite(Site item, IReadOnlyCollection<int> employeeIds, IReadOnlyCollection<int> equipmentIds)
    {
        AddDeletedItem("Site", "Radni nalozi", item.Id,
            BuildName(item.Code, item.Name, item.Location),
            new SiteSnapshot(item.Id, item.Code, item.Name, item.Location,
                item.Status, employeeIds.ToArray(), equipmentIds.ToArray()));
        _context.Sites.Remove(item);
    }

    public void MoveEquipmentReturn(EquipmentReturn item)
    {
        AddDeletedItem("EquipmentReturn", "Razduženja", item.Id,
            BuildName(item.InventoryNumber, item.Name, item.ReturnedAt.ToString("dd.MM.yyyy.")),
            new EquipmentReturnSnapshot(item));
        _context.EquipmentReturns.Remove(item);
    }

    public void MoveConsumable(PrinterConsumable item)
    {
        AddDeletedItem("PrinterConsumable", "Toneri i tinte", item.Id,
            BuildName(item.ProductCode, item.Name),
            new ConsumableSnapshot(
                item.Id, item.Name, item.ProductCode, item.Type, item.Color,
                item.QuantityAvailable, item.QuantityOrdered, item.IsOriginal,
                item.CreatedAt, item.UpdatedAt,
                item.CompatiblePrinters.Select(x => x.PrinterName).ToArray()));
        _context.PrinterConsumables.Remove(item);
    }

    public async Task<(bool Success, string Message)> RestoreAsync(int deletedItemId)
    {
        var deleted = await _context.DeletedItems.FirstOrDefaultAsync(x => x.Id == deletedItemId);
        if (deleted == null)
            return (false, "Obrisana stavka nije pronađena.");

        if (deleted.ExpiresAtUtc < DateTime.UtcNow)
            return (false, "Rok za vraćanje ove stavke je istekao.");

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            switch (deleted.EntityType)
            {
                case "Equipment":
                    await RestoreEquipmentAsync(deleted);
                    break;
                case "Employee":
                    await RestoreEmployeeAsync(deleted);
                    break;
                case "Site":
                    await RestoreSiteAsync(deleted);
                    break;
                case "EquipmentReturn":
                    await RestoreEquipmentReturnAsync(deleted);
                    break;
                case "PrinterConsumable":
                    await RestoreConsumableAsync(deleted);
                    break;
                default:
                    return (false, "Ova vrsta stavke trenutačno se ne može vratiti.");
            }

            _context.DeletedItems.Remove(deleted);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return (true, $"Stavka '{deleted.DisplayName}' uspješno je vraćena.");
        }
        catch (RestoreConflictException)
        {
            await transaction.RollbackAsync();
            return (false, "Stavku nije moguće vratiti jer već postoji zapis s istom šifrom, inventurnim brojem ili drugim jedinstvenim podatkom.");
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            return (false, "Stavku nije moguće vratiti zbog sukoba s postojećim podacima.");
        }
        catch (JsonException)
        {
            await transaction.RollbackAsync();
            return (false, "Spremljeni podaci za vraćanje nisu ispravni.");
        }
    }

    public async Task PurgeExpiredAsync()
    {
        var now = DateTime.UtcNow;
        var expired = await _context.DeletedItems.Where(x => x.ExpiresAtUtc < now).ToListAsync();
        if (expired.Count == 0)
            return;

        _context.DeletedItems.RemoveRange(expired);
        await _context.SaveChangesAsync();
    }

    private void AddDeletedItem<T>(string entityType, string entityLabel, int originalId, string displayName, T snapshot)
    {
        _context.DeletedItems.Add(new DeletedItem
        {
            EntityType = entityType,
            EntityLabel = entityLabel,
            OriginalId = originalId,
            DisplayName = displayName,
            SnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions),
            DeletedBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name,
            DeletedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(30)
        });
    }

    private async Task RestoreEquipmentAsync(DeletedItem deleted)
    {
        var x = Deserialize<EquipmentSnapshot>(deleted);
        if (await _context.Equipment.AnyAsync(e => e.Id == x.Id))
            throw new RestoreConflictException();

        _context.Equipment.Add(new Equipment
        {
            Id = x.Id,
            InventoryNumber = x.InventoryNumber,
            Name = x.Name,
            SerialNumber = x.SerialNumber,
            EquipmentType = x.EquipmentType,
            Status = x.Status,
            CurrentEmployeeId = await ExistingIdOrNullAsync(_context.Employees, x.CurrentEmployeeId),
            CurrentSiteId = await ExistingIdOrNullAsync(_context.Sites, x.CurrentSiteId),
            AssignedAt = x.AssignedAt,
            ReturnedAt = x.ReturnedAt,
            HandedOverBy = x.HandedOverBy
        });
    }

    private async Task RestoreEmployeeAsync(DeletedItem deleted)
    {
        var x = Deserialize<EmployeeSnapshot>(deleted);
        if (await _context.Employees.AnyAsync(e => e.Id == x.Id))
            throw new RestoreConflictException();

        _context.Employees.Add(new Employee
        {
            Id = x.Id,
            WorkerCode = x.WorkerCode,
            FullName = x.FullName,
            SiteId = await ExistingIdOrNullAsync(_context.Sites, x.SiteId),
            Status = x.Status,
            CreatedAt = x.CreatedAt
        });

        await _context.SaveChangesAsync();
        var equipment = await _context.Equipment.Where(e => x.AssignedEquipmentIds.Contains(e.Id)).ToListAsync();
        foreach (var item in equipment)
            item.CurrentEmployeeId = x.Id;
    }

    private async Task RestoreSiteAsync(DeletedItem deleted)
    {
        var x = Deserialize<SiteSnapshot>(deleted);
        if (await _context.Sites.AnyAsync(e => e.Id == x.Id))
            throw new RestoreConflictException();

        _context.Sites.Add(new Site
        {
            Id = x.Id,
            Code = x.Code,
            Name = x.Name,
            Location = x.Location,
            Status = x.Status
        });

        await _context.SaveChangesAsync();
        var employees = await _context.Employees.Where(e => x.EmployeeIds.Contains(e.Id)).ToListAsync();
        foreach (var employee in employees)
            employee.SiteId = x.Id;

        var equipment = await _context.Equipment.Where(e => x.EquipmentIds.Contains(e.Id)).ToListAsync();
        foreach (var item in equipment)
            item.CurrentSiteId = x.Id;
    }

    private async Task RestoreEquipmentReturnAsync(DeletedItem deleted)
    {
        var x = Deserialize<EquipmentReturnSnapshot>(deleted);
        if (await _context.EquipmentReturns.AnyAsync(e => e.Id == x.Id))
            throw new RestoreConflictException();

        _context.EquipmentReturns.Add(x.ToEntity());
    }

    private async Task RestoreConsumableAsync(DeletedItem deleted)
    {
        var x = Deserialize<ConsumableSnapshot>(deleted);
        if (await _context.PrinterConsumables.AnyAsync(e => e.Id == x.Id))
            throw new RestoreConflictException();

        var item = new PrinterConsumable
        {
            Id = x.Id,
            Name = x.Name,
            ProductCode = x.ProductCode,
            Type = x.Type,
            Color = x.Color,
            QuantityAvailable = x.QuantityAvailable,
            QuantityOrdered = x.QuantityOrdered,
            IsOriginal = x.IsOriginal,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        };

        foreach (var printer in x.CompatiblePrinters.Where(p => !string.IsNullOrWhiteSpace(p)))
            item.CompatiblePrinters.Add(new ConsumableCompatiblePrinter { PrinterName = printer.Trim() });

        _context.PrinterConsumables.Add(item);
    }

    private static T Deserialize<T>(DeletedItem item) =>
        JsonSerializer.Deserialize<T>(item.SnapshotJson, JsonOptions)
        ?? throw new JsonException();

    private static string BuildName(params string?[] values) =>
        string.Join(" | ", values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));

    private static async Task<int?> ExistingIdOrNullAsync<TEntity>(DbSet<TEntity> set, int? id)
        where TEntity : class
    {
        if (!id.HasValue)
            return null;
        return await set.FindAsync(id.Value) == null ? null : id;
    }

    private sealed class RestoreConflictException : Exception { }

    private sealed record EquipmentSnapshot(
        int Id, string? InventoryNumber, string? Name, string? SerialNumber,
        EquipmentType EquipmentType, EquipmentStatus Status, int? CurrentEmployeeId,
        int? CurrentSiteId, DateTime? AssignedAt, DateTime? ReturnedAt, string? HandedOverBy);

    private sealed record EmployeeSnapshot(
        int Id, string WorkerCode, string FullName, int? SiteId, EmployeeStatus Status,
        DateTime CreatedAt, int[] AssignedEquipmentIds);

    private sealed record SiteSnapshot(
        int Id, string Code, string Name, string Location, SiteStatus Status,
        int[] EmployeeIds, int[] EquipmentIds);

    private sealed record ConsumableSnapshot(
        int Id, string Name, string? ProductCode, ConsumableType Type, ConsumableColor Color,
        int QuantityAvailable, int QuantityOrdered, bool IsOriginal, DateTime CreatedAt,
        DateTime? UpdatedAt, string[] CompatiblePrinters);

    private sealed record EquipmentReturnSnapshot
    {
        public EquipmentReturnSnapshot() { }

        public EquipmentReturnSnapshot(EquipmentReturn x)
        {
            Id = x.Id; EquipmentId = x.EquipmentId; InventoryNumber = x.InventoryNumber;
            SerialNumber = x.SerialNumber; EquipmentType = x.EquipmentType; Name = x.Name;
            PreviousStatus = x.PreviousStatus; PreviousSiteId = x.PreviousSiteId;
            PreviousSiteCode = x.PreviousSiteCode; PreviousSiteName = x.PreviousSiteName;
            PreviousSiteLocation = x.PreviousSiteLocation; PreviousEmployeeId = x.PreviousEmployeeId;
            PreviousEmployeeCode = x.PreviousEmployeeCode; PreviousEmployeeName = x.PreviousEmployeeName;
            AssignedAt = x.AssignedAt; ReturnedAt = x.ReturnedAt;
            PreviousHandedOverBy = x.PreviousHandedOverBy; HandedOverBy = x.HandedOverBy; Note = x.Note;
        }

        public int Id { get; init; }
        public int EquipmentId { get; init; }
        public string? InventoryNumber { get; init; }
        public string? SerialNumber { get; init; }
        public EquipmentType EquipmentType { get; init; }
        public string? Name { get; init; }
        public EquipmentStatus PreviousStatus { get; init; }
        public int? PreviousSiteId { get; init; }
        public string? PreviousSiteCode { get; init; }
        public string? PreviousSiteName { get; init; }
        public string? PreviousSiteLocation { get; init; }
        public int? PreviousEmployeeId { get; init; }
        public string? PreviousEmployeeCode { get; init; }
        public string? PreviousEmployeeName { get; init; }
        public DateTime? AssignedAt { get; init; }
        public DateTime ReturnedAt { get; init; }
        public string? PreviousHandedOverBy { get; init; }
        public string? HandedOverBy { get; init; }
        public string? Note { get; init; }

        public EquipmentReturn ToEntity() => new()
        {
            Id = Id, EquipmentId = EquipmentId, InventoryNumber = InventoryNumber,
            SerialNumber = SerialNumber, EquipmentType = EquipmentType, Name = Name,
            PreviousStatus = PreviousStatus, PreviousSiteId = PreviousSiteId,
            PreviousSiteCode = PreviousSiteCode, PreviousSiteName = PreviousSiteName,
            PreviousSiteLocation = PreviousSiteLocation, PreviousEmployeeId = PreviousEmployeeId,
            PreviousEmployeeCode = PreviousEmployeeCode, PreviousEmployeeName = PreviousEmployeeName,
            AssignedAt = AssignedAt, ReturnedAt = ReturnedAt,
            PreviousHandedOverBy = PreviousHandedOverBy, HandedOverBy = HandedOverBy, Note = Note
        };
    }
}
