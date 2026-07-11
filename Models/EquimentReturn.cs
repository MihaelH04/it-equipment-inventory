using System.ComponentModel.DataAnnotations;

namespace ITEquipmentInventory.Models;

public class EquipmentReturn
{
    public int Id { get; set; }

    [Required]
    public int EquipmentId { get; set; }

    [Display(Name = "Inventurni broj")]
    public string? InventoryNumber { get; set; }

    [Display(Name = "Serijski broj")]
    public string? SerialNumber { get; set; }

    [Display(Name = "Vrsta")]
    public EquipmentType EquipmentType { get; set; }

    [Display(Name = "Naziv")]
    public string? Name { get; set; }

    [Display(Name = "Status prije razduženja")]
    public EquipmentStatus PreviousStatus { get; set; }

    public int? PreviousSiteId { get; set; }
    public string? PreviousSiteCode { get; set; }
    public string? PreviousSiteName { get; set; }
    public string? PreviousSiteLocation { get; set; }

    public int? PreviousEmployeeId { get; set; }
    public string? PreviousEmployeeCode { get; set; }
    public string? PreviousEmployeeName { get; set; }

    [Display(Name = "Datum zaduženja")]
    public DateTime? AssignedAt { get; set; }

    [Display(Name = "Datum razduženja")]
    public DateTime ReturnedAt { get; set; }

    [Display(Name = "Tko je zadužio prije razduženja")]
    public string? PreviousHandedOverBy { get; set; }

    [Display(Name = "Razdužio")]
    public string? HandedOverBy { get; set; }

    [Display(Name = "Napomena")]
    public string? Note { get; set; }
}
