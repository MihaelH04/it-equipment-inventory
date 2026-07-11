using System.ComponentModel.DataAnnotations;

namespace ITEquipmentInventory.Models;

public class Site
{
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    [Display(Name = "Šifra")]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(150)]
    [Display(Name = "Naziv")]
    public string Name { get; set; } = string.Empty;

    [StringLength(150)]
    [Display(Name = "Lokacija")]
    public string Location { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Status")]
    public SiteStatus Status { get; set; } = SiteStatus.Aktivno;

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
    public ICollection<Equipment> EquipmentItems { get; set; } = new List<Equipment>();
}