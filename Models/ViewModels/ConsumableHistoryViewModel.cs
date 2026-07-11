using ITEquipmentInventory.Models;

namespace ITEquipmentInventory.Models.ViewModels;

public class ConsumableHistoryViewModel
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public string SearchString { get; set; } = string.Empty;
    public string SortOrder { get; set; } = string.Empty;
    public IReadOnlyList<ConsumableTransaction> Transactions { get; set; }
        = Array.Empty<ConsumableTransaction>();
    public IReadOnlyList<ConsumableOrderedSummaryRow> OrderedSummary { get; set; }
        = Array.Empty<ConsumableOrderedSummaryRow>();
    public IReadOnlyList<ConsumableUsedSummaryRow> UsedSummary { get; set; }
        = Array.Empty<ConsumableUsedSummaryRow>();
}

public class ConsumableOrderedSummaryRow
{
    public string ItemName { get; set; } = string.Empty;
    public string? ProductCode { get; set; }
    public int Quantity { get; set; }
}

public class ConsumableUsedSummaryRow
{
    public string PrinterName { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? ProductCode { get; set; }
    public int Quantity { get; set; }
}
