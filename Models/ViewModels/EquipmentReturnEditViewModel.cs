using System.ComponentModel.DataAnnotations;

namespace ITEquipmentInventory.Models.ViewModels;

public class EquipmentReturnEditViewModel
{
    public int Id { get; set; }

    [Display(Name = "Inventurni broj")]
    public string? InventoryNumber { get; set; }
    [Display(Name = "Serijski broj")]
    public string? SerialNumber { get; set; }
    [Display(Name = "Vrsta")]
    public EquipmentType EquipmentType { get; set; }
    [Display(Name = "Naziv")]
    public string? Name { get; set; }
    public string? PreviousSiteCode { get; set; }
    public string? PreviousSiteName { get; set; }
    public string? PreviousEmployeeCode { get; set; }
    public string? PreviousEmployeeName { get; set; }
    [Display(Name = "Datum zaduženja")]
    public DateTime? AssignedAt { get; set; }
    [Display(Name = "Datum razduženja")]
    public DateTime ReturnedAt { get; set; }
    [Display(Name = "Razdužio")]
    public string? HandedOverBy { get; set; }
    [Display(Name = "Napomena")]
    public string? Note { get; set; }
}
