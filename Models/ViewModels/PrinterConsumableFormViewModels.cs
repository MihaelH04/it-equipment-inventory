using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ITEquipmentInventory.Models.ViewModels;

public class PrinterConsumableFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Naziv artikla je obavezan.")]
    [StringLength(150)]
    [Display(Name = "Naziv artikla")]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "Šifra artikla")]
    public string? ProductCode { get; set; }

    [Required]
    [EnumDataType(typeof(ConsumableType))]
    [Display(Name = "Vrsta")]
    public ConsumableType Type { get; set; } = ConsumableType.Toner;

    [Range(0, 100000, ErrorMessage = "Količina ne može biti negativna.")]
    [Display(Name = "Dostupno")]
    public int QuantityAvailable { get; set; }

    [Range(0, 100000, ErrorMessage = "Količina ne može biti negativna.")]
    [Display(Name = "Naručeno")]
    public int QuantityOrdered { get; set; }

    [Display(Name = "Originalni proizvod")]
    public bool IsOriginal { get; set; } = true;

    [Display(Name = "Slika artikla")]
    public IFormFile? ProductImage { get; set; }

    public string? ImageUrl { get; set; }

    [Required(ErrorMessage = "Odaberi barem jedan kompatibilni printer iz baze.")]
    [Display(Name = "Kompatibilni printeri")]
    public string CompatiblePrintersText { get; set; } = string.Empty;
}

public class PrinterConsumableCreateViewModel : PrinterConsumableFormViewModel
{
}

public class PrinterConsumableEditViewModel : PrinterConsumableFormViewModel
{
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
