using System.ComponentModel.DataAnnotations;

namespace ITEquipmentInventory.Models.ViewModels;

public class SiteCreateViewModel
{
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
}

public class SiteEditViewModel : SiteCreateViewModel
{
    public int Id { get; set; }
}
