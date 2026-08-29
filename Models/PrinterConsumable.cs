using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace ITEquipmentInventory.Models;

public class PrinterConsumable
{
    private static readonly ConsumableColor[] StandardColors =
    [
        ConsumableColor.Cyan,
        ConsumableColor.Magenta,
        ConsumableColor.Zuta,
        ConsumableColor.Crna
    ];

    public int Id { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    [Required(ErrorMessage = "Naziv artikla je obavezan.")]
    [StringLength(150)]
    [Display(Name = "Naziv artikla")]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "Šifra artikla")]
    public string? ProductCode { get; set; }

    // Stanje po bojama sprema se unutar jednog proizvoda. Ovo nije zaseban proizvod po boji.
    [StringLength(2000)]
    public string? ColorStateJson { get; set; }

    [Required]
    [EnumDataType(typeof(ConsumableType))]
    [Display(Name = "Vrsta")]
    public ConsumableType Type { get; set; } = ConsumableType.Toner;

    // Zadržano radi kompatibilnosti sa starim podacima. Novi proizvodi koriste NijePrimjenjivo,
    // a stvarno stanje po bojama vodi se kroz ColorStateJson.
    [Required]
    [EnumDataType(typeof(ConsumableColor))]
    [Display(Name = "Boja")]
    public ConsumableColor Color { get; set; } = ConsumableColor.NijePrimjenjivo;

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

    [NotMapped]
    [Display(Name = "Slika artikla")]
    public IFormFile? ProductImage { get; set; }

    [NotMapped]
    public string? ImageUrl { get; set; }

    public ICollection<ConsumableCompatiblePrinter> CompatiblePrinters { get; set; }
        = new List<ConsumableCompatiblePrinter>();

    public ICollection<ConsumableTransaction> Transactions { get; set; }
        = new List<ConsumableTransaction>();

    public ICollection<ConsumablePendingOrder> PendingOrders { get; set; }
        = new List<ConsumablePendingOrder>();

    [NotMapped]
    [Required(ErrorMessage = "Odaberi barem jedan kompatibilni printer iz baze.")]
    [Display(Name = "Kompatibilni printeri")]
    public string CompatiblePrintersText { get; set; } = string.Empty;

    [NotMapped]
    public bool UsesStandardColors => Type is ConsumableType.Toner or ConsumableType.Tinta;

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
    public string ProductKindText => IsOriginal ? "Originalni proizvod" : "Zamjenski proizvod";

    [NotMapped]
    public IReadOnlyList<ConsumableColorState> ColorStates => GetColorStates();

    [NotMapped]
    public string ColorStatesSummary => string.Join(", ", GetColorStates()
        .Where(x => x.QuantityAvailable > 0 || x.QuantityOrdered > 0)
        .Select(x => $"{GetColorDisplayName(x.Color)}: {x.QuantityAvailable}/{x.QuantityOrdered}"));

    [NotMapped]
    public string CompatiblePrintersSummary => string.Join(", ",
        CompatiblePrinters
            .Select(x => x.PrinterName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.CurrentCultureIgnoreCase));

    public IReadOnlyList<ConsumableColorState> GetColorStates()
    {
        var parsedStates = DeserializeColorStates();
        if (parsedStates.Count > 0)
            return ReconcileAggregateTotals(NormalizeColorStates(parsedStates).ToList());

        if (UsesStandardColors)
        {
            var initialColor = Color is ConsumableColor.Cyan or ConsumableColor.Magenta or ConsumableColor.Zuta or ConsumableColor.Crna
                ? Color
                : ConsumableColor.Crna;

            // Stari zapisi bez JSON-a ponekad imaju samo ukupnu količinu. Kako se količina ne bi izgubila,
            // takav se zapis privremeno tretira kao K/Black. Nakon prve promjene sprema se ispravan JSON.
            return StandardColors
                .Select(color => new ConsumableColorState(
                    color,
                    color == initialColor ? QuantityAvailable : 0,
                    color == initialColor ? QuantityOrdered : 0))
                .ToList();
        }

        return
        [
            new ConsumableColorState(ConsumableColor.NijePrimjenjivo, QuantityAvailable, QuantityOrdered)
        ];
    }

    public void SetColorStates(IEnumerable<ConsumableColorState> states)
    {
        var normalized = NormalizeColorStates(states).ToList();
        ColorStateJson = JsonSerializer.Serialize(normalized);
        QuantityAvailable = normalized.Sum(x => x.QuantityAvailable);
        QuantityOrdered = normalized.Sum(x => x.QuantityOrdered);
        Color = ConsumableColor.NijePrimjenjivo;
    }

    private List<ConsumableColorState> DeserializeColorStates()
    {
        if (string.IsNullOrWhiteSpace(ColorStateJson))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<ConsumableColorState>>(ColorStateJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private IReadOnlyList<ConsumableColorState> ReconcileAggregateTotals(List<ConsumableColorState> states)
    {
        ReconcileQuantity(states, QuantityAvailable, state => state.QuantityAvailable,
            (state, value) => state with { QuantityAvailable = value });
        ReconcileQuantity(states, QuantityOrdered, state => state.QuantityOrdered,
            (state, value) => state with { QuantityOrdered = value });
        return states;
    }

    private static void ReconcileQuantity(
        List<ConsumableColorState> states,
        int expectedTotal,
        Func<ConsumableColorState, int> selector,
        Func<ConsumableColorState, int, ConsumableColorState> update)
    {
        if (states.Count == 0)
            return;

        var difference = Math.Max(0, expectedTotal) - states.Sum(selector);
        if (difference > 0)
        {
            var preferredIndex = states.FindIndex(x => x.Color is ConsumableColor.Crna or ConsumableColor.NijePrimjenjivo);
            if (preferredIndex < 0) preferredIndex = states.Count - 1;
            var state = states[preferredIndex];
            states[preferredIndex] = update(state, selector(state) + difference);
            return;
        }

        var toRemove = -difference;
        foreach (var index in Enumerable.Range(0, states.Count)
                     .OrderByDescending(i => selector(states[i])))
        {
            if (toRemove == 0) break;
            var state = states[index];
            var current = selector(state);
            var remove = Math.Min(current, toRemove);
            states[index] = update(state, current - remove);
            toRemove -= remove;
        }
    }

    private IReadOnlyList<ConsumableColorState> NormalizeColorStates(IEnumerable<ConsumableColorState> states)
    {
        var grouped = states
            .Where(x => x.QuantityAvailable >= 0 && x.QuantityOrdered >= 0)
            .GroupBy(x => x.Color)
            .ToDictionary(
                group => group.Key,
                group => new ConsumableColorState(
                    group.Key,
                    group.Sum(x => x.QuantityAvailable),
                    group.Sum(x => x.QuantityOrdered)));

        if (UsesStandardColors)
        {
            var result = StandardColors
                .Select(color => grouped.TryGetValue(color, out var state)
                    ? state
                    : new ConsumableColorState(color, 0, 0))
                .ToList();

            // Sačuvaj eventualni stari ukupni zapis koji nije imao konkretnu boju.
            var legacyTotal = grouped
                .Where(x => x.Key is ConsumableColor.NijePrimjenjivo or ConsumableColor.Visebojna)
                .Select(x => x.Value)
                .Aggregate(
                    new ConsumableColorState(ConsumableColor.Crna, 0, 0),
                    (sum, value) => sum with
                    {
                        QuantityAvailable = sum.QuantityAvailable + value.QuantityAvailable,
                        QuantityOrdered = sum.QuantityOrdered + value.QuantityOrdered
                    });

            if (legacyTotal.QuantityAvailable > 0 || legacyTotal.QuantityOrdered > 0)
            {
                var blackIndex = result.FindIndex(x => x.Color == ConsumableColor.Crna);
                var black = result[blackIndex];
                result[blackIndex] = black with
                {
                    QuantityAvailable = black.QuantityAvailable + legacyTotal.QuantityAvailable,
                    QuantityOrdered = black.QuantityOrdered + legacyTotal.QuantityOrdered
                };
            }

            return result;
        }

        var totalAvailable = grouped.Values.Sum(x => x.QuantityAvailable);
        var totalOrdered = grouped.Values.Sum(x => x.QuantityOrdered);
        return
        [
            new ConsumableColorState(ConsumableColor.NijePrimjenjivo, totalAvailable, totalOrdered)
        ];
    }

    public static string GetColorDisplayName(ConsumableColor color) => color switch
    {
        ConsumableColor.Crna => "K - Black",
        ConsumableColor.Cyan => "C - Cyan",
        ConsumableColor.Magenta => "M - Magenta",
        ConsumableColor.Zuta => "Y - Yellow",
        ConsumableColor.NijePrimjenjivo => "Ukupno",
        _ => "Ukupno"
    };
}

public sealed record ConsumableColorState(ConsumableColor Color, int QuantityAvailable, int QuantityOrdered);

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

public class ConsumablePendingOrder
{
    public int Id { get; set; }

    public Guid? OrderGroupId { get; set; }

    public int PrinterConsumableId { get; set; }
    public PrinterConsumable PrinterConsumable { get; set; } = null!;

    public ConsumableColor Color { get; set; } = ConsumableColor.NijePrimjenjivo;

    [Range(1, 100000)]
    public int QuantityOrdered { get; set; }

    [Range(0, 100000)]
    public int QuantityReceived { get; set; }

    public DateTime OrderedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }

    [StringLength(100)]
    public string? OrderedBy { get; set; }

    [NotMapped]
    public int RemainingQuantity => Math.Max(0, QuantityOrdered - QuantityReceived);

    [NotMapped]
    public bool IsCompleted => RemainingQuantity == 0;
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
