using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ITEquipmentInventory.Models;

public class PrinterConsumable
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
    [Display(Name = "Vrsta")]
    public ConsumableType Type { get; set; } = ConsumableType.Toner;

    [Required]
    [Display(Name = "Boja")]
    public ConsumableColor Color { get; set; } = ConsumableColor.Crna;

    [Range(0, 100000, ErrorMessage = "Količina ne može biti negativna.")]
    [Display(Name = "Dostupno")]
    public int QuantityAvailable { get; set; }

    [Range(0, 100000, ErrorMessage = "Naručena količina ne može biti negativna.")]
    [Display(Name = "Naručeno")]
    public int QuantityOrdered { get; set; }

    [Display(Name = "Originalni proizvod")]
    public bool IsOriginal { get; set; } = true;

    [Display(Name = "Kreirano")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Display(Name = "Zadnja izmjena")]
    public DateTime? UpdatedAt { get; set; }

    public ICollection<ConsumableCompatiblePrinter> CompatiblePrinters { get; set; }
        = new List<ConsumableCompatiblePrinter>();

    public ICollection<ConsumableTransaction> Transactions { get; set; }
        = new List<ConsumableTransaction>();

    [NotMapped]
    [Required(ErrorMessage = "Upiši barem jedan kompatibilni printer ili model.")]
    [Display(Name = "Kompatibilni printeri / modeli")]
    public string CompatiblePrintersText { get; set; } = string.Empty;

    [NotMapped]
    public ConsumableAvailabilityStatus AvailabilityStatus =>
        QuantityAvailable > 0
            ? ConsumableAvailabilityStatus.Dostupno
            : QuantityOrdered > 0
                ? ConsumableAvailabilityStatus.Naruceno
                : ConsumableAvailabilityStatus.Nema;

    [NotMapped]
    public string AvailabilityStatusText => AvailabilityStatus switch
    {
        ConsumableAvailabilityStatus.Dostupno => "Dostupno",
        ConsumableAvailabilityStatus.Naruceno => "Naručeno",
        _ => "Nema"
    };

    [NotMapped]
    public string AvailabilityStatusCssClass => AvailabilityStatus switch
    {
        ConsumableAvailabilityStatus.Dostupno => "status-success",
        ConsumableAvailabilityStatus.Naruceno => "status-warning",
        _ => "status-danger"
    };

    [NotMapped]
    public string ProductKindText => IsOriginal ? "Original" : "Zamjenski";

    [NotMapped]
    public string CompatiblePrintersSummary => string.Join(", ",
        CompatiblePrinters
            .Select(x => x.PrinterName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.CurrentCultureIgnoreCase));
}

public class ConsumableCompatiblePrinter
{
    public int Id { get; set; }

    public int PrinterConsumableId { get; set; }
    public PrinterConsumable PrinterConsumable { get; set; } = null!;

    [Required]
    [StringLength(200)]
    [Display(Name = "Printer / model")]
    public string PrinterName { get; set; } = string.Empty;
}

public class ConsumableTransaction
{
    public int Id { get; set; }

    public int? PrinterConsumableId { get; set; }
    public PrinterConsumable? PrinterConsumable { get; set; }

    [Required]
    [StringLength(150)]
    public string ConsumableName { get; set; } = string.Empty;

    [StringLength(100)]
    public string? ProductCode { get; set; }

    public ConsumableType ConsumableType { get; set; }
    public ConsumableColor Color { get; set; }
    public ConsumableTransactionType TransactionType { get; set; }

    [Range(1, 100000)]
    public int Quantity { get; set; }

    [StringLength(200)]
    public string? PrinterName { get; set; }

    public int? SiteId { get; set; }

    [StringLength(200)]
    public string? SiteName { get; set; }

    [StringLength(100)]
    public string? PerformedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public enum ConsumableType
{
    [Display(Name = "Toner")]
    Toner = 1,

    [Display(Name = "Tinta")]
    Tinta = 2,

    [Display(Name = "Ostalo")]
    Ostalo = 4,

    [Display(Name = "Kutija za održavanje")]
    KutijaZaOdrzavanje = 5
}

public enum ConsumableColor
{
    [Display(Name = "Crna")]
    Crna = 1,

    [Display(Name = "Cyan")]
    Cyan = 2,

    [Display(Name = "Magenta")]
    Magenta = 3,

    [Display(Name = "Žuta")]
    Zuta = 4,

    [Display(Name = "Višebojna")]
    Visebojna = 5,

    [Display(Name = "Nije primjenjivo")]
    NijePrimjenjivo = 6
}

public enum ConsumableAvailabilityStatus
{
    Dostupno = 1,
    Naruceno = 2,
    Nema = 3
}

public enum ConsumableTransactionType
{
    [Display(Name = "Naručeno")]
    Naruceno = 1,

    [Display(Name = "Zaprimljeno")]
    Zaprimljeno = 2,

    [Display(Name = "Izdano / potrošeno")]
    Izdano = 3
}
