using System.ComponentModel.DataAnnotations;

namespace ITEquipmentInventory.Models.ViewModels;

public class EquipmentCreateViewModel
{
    [Display(Name = "Inventurni broj")]
    public string? InventoryNumber { get; set; }

    [Display(Name = "Naziv")]
    public string? Name { get; set; }

    [Display(Name = "Serijski broj")]
    public string? SerialNumber { get; set; }

    [Display(Name = "Vrsta opreme")]
    public EquipmentType EquipmentType { get; set; }

    [Display(Name = "Status")]
    public EquipmentStatus Status { get; set; }

    public int? CurrentEmployeeId { get; set; }
    public int? CurrentSiteId { get; set; }

    [Display(Name = "Datum zaduženja")]
    public DateTime? AssignedAt { get; set; }

    [Display(Name = "Datum razduženja")]
    public DateTime? ReturnedAt { get; set; }

    [Display(Name = "Predao")]
    public string? HandedOverBy { get; set; }
}

public class EquipmentEditViewModel : EquipmentCreateViewModel
{
    public int Id { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
