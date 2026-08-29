using System.ComponentModel.DataAnnotations;

namespace ITEquipmentInventory.Models.ViewModels;

public class EmployeeCreateViewModel
{
    [Required]
    [StringLength(30)]
    public string WorkerCode { get; set; } = string.Empty;

    [Required]
    [StringLength(120)]
    public string FullName { get; set; } = string.Empty;

    public int? SiteId { get; set; }
    public EmployeeStatus Status { get; set; } = EmployeeStatus.Aktivan;
}

public class EmployeeEditViewModel : EmployeeCreateViewModel
{
    public int Id { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
