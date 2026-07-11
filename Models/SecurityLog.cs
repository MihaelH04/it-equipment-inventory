using System.ComponentModel.DataAnnotations;

namespace ITEquipmentInventory.Models;

public class SecurityLog
{
    public int Id { get; set; }

    public int? AppUserId { get; set; }
    public AppUser? AppUser { get; set; }

    [StringLength(80)]
    public string? UserName { get; set; }

    [Required]
    [StringLength(80)]
    public string EventType { get; set; } = string.Empty;

    public bool Success { get; set; }

    [StringLength(64)]
    public string? IpAddress { get; set; }

    [StringLength(300)]
    public string? UserAgent { get; set; }

    [StringLength(300)]
    public string? Path { get; set; }

    [StringLength(500)]
    public string? Message { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
