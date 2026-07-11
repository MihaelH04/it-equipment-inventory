using System.ComponentModel.DataAnnotations;

namespace ITEquipmentInventory.Models;

public class DeletedItem
{
    public int Id { get; set; }

    [Required]
    [StringLength(80)]
    public string EntityType { get; set; } = string.Empty;

    [Required]
    [StringLength(120)]
    public string EntityLabel { get; set; } = string.Empty;

    public int OriginalId { get; set; }

    [Required]
    [StringLength(300)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    public string SnapshotJson { get; set; } = string.Empty;

    [StringLength(100)]
    public string? DeletedBy { get; set; }

    public DateTime DeletedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddDays(30);
}
