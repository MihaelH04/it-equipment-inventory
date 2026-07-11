using System.ComponentModel.DataAnnotations;

namespace ITEquipmentInventory.Models;

public class Employee : BaseEntity
{
    [Required]
    [StringLength(30)]
    public string WorkerCode { get; set; } = string.Empty;

    [Required]
    [StringLength(120)]
    public string FullName { get; set; } = string.Empty;

    public int? SiteId { get; set; }
    public Site? Site { get; set; }

    public EmployeeStatus Status { get; set; } = EmployeeStatus.Aktivan;
}