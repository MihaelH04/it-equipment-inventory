using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ITEquipmentInventory.Data;
using ITEquipmentInventory.Models;
using ITEquipmentInventory.Models.ViewModels;
using ITEquipmentInventory.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Table;

namespace ITEquipmentInventory.Controllers;

[Authorize(Roles = "Admin")]
public class PrinterConsumablesController : Controller
{
    private const int MaximumQuantity = 100000;
    private readonly AppDbContext _context;
    private readonly RecycleBinService _recycleBin;

    public PrinterConsumablesController(AppDbContext context, RecycleBinService recycleBin)
    {
        _context = context;
        _recycleBin = recycleBin;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? searchString, string? statusFilter, string? sortOrder)
    {
        var items = await GetFilteredItemsAsync(searchString, statusFilter, sortOrder);
        await LoadStatsAsync();
        await LoadIndexBagsAsync(searchString, statusFilter, sortOrder);
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel(string? searchString, string? statusFilter, string? sortOrder)
    {
        var items = await GetFilteredItemsAsync(searchString, statusFilter, sortOrder);
        var bytes = ExcelExportHelper.CreateExcel(
            "Toneri i tinte",
            new[]
            {
                "Kompatibilni printeri / modeli", "Naziv artikla", "Šifra artikla", "Vrsta", "Boja",
                "Original / zamjenski", "Dostupno", "Naručeno", "Status", "Zadnja izmjena"
            },
            items.Select(x => new object?[]
            {
                x.CompatiblePrintersSummary, x.Name, x.ProductCode, GetEnumDisplayName(x.Type),
                GetEnumDisplayName(x.Color), x.ProductKindText, x.QuantityAvailable,
                x.QuantityOrdered, GetAvailabilityText(x),
                x.UpdatedAt?.ToString("dd.MM.yyyy. HH:mm") ?? x.CreatedAt.ToString("dd.MM.yyyy. HH:mm")
            }));

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ExcelExportHelper.FileName("Toneri_i_tinte"));
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await LoadFormBagsAsync();
        return View(new PrinterConsumable
        {
            Type = ConsumableType.Toner,
            Color = ConsumableColor.Crna,
            IsOriginal = true
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PrinterConsumable model)
    {
        NormalizeModel(model);
        var printers = ParsePrinterNames(model.CompatiblePrintersText);
        ValidateCompatiblePrinters(printers);

        if (await DuplicateExistsAsync(model))
            ModelState.AddModelError(string.Empty, "Artikl s istom šifrom ili istim nazivom već postoji.");

        if (!ModelState.IsValid)
        {
            await LoadFormBagsAsync();
            return View(model);
        }

        model.CreatedAt = DateTime.Now;
        model.UpdatedAt = null;
        foreach (var printer in printers)
            model.CompatiblePrinters.Add(new ConsumableCompatiblePrinter { PrinterName = printer });

        _context.PrinterConsumables.Add(model);
        await _context.SaveChangesAsync();

        if (model.QuantityOrdered > 0)
        {
            AddTransaction(model, ConsumableTransactionType.Naruceno, model.QuantityOrdered, null, null, null);
            await _context.SaveChangesAsync();
        }

        TempData["Success"] = "Artikl je uspješno dodan.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var item = await _context.PrinterConsumables
            .Include(x => x.CompatiblePrinters)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (item == null)
            return NotFound();

        item.CompatiblePrintersText = string.Join(Environment.NewLine,
            item.CompatiblePrinters.OrderBy(x => x.PrinterName).Select(x => x.PrinterName));

        await LoadFormBagsAsync();
        return View(item);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PrinterConsumable model)
    {
        if (id != model.Id)
            return NotFound();

        NormalizeModel(model);
        var printers = ParsePrinterNames(model.CompatiblePrintersText);
        ValidateCompatiblePrinters(printers);

        if (await DuplicateExistsAsync(model, model.Id))
            ModelState.AddModelError(string.Empty, "Artikl s istom šifrom ili istim nazivom već postoji.");

        if (!ModelState.IsValid)
        {
            await LoadFormBagsAsync();
            return View(model);
        }

        var item = await _context.PrinterConsumables
            .Include(x => x.CompatiblePrinters)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (item == null)
            return NotFound();

        item.Name = model.Name;
        item.ProductCode = model.ProductCode;
        item.Type = model.Type;
        item.Color = model.Color;
        item.QuantityAvailable = model.QuantityAvailable;
        item.QuantityOrdered = model.QuantityOrdered;
        item.IsOriginal = model.IsOriginal;
        item.UpdatedAt = DateTime.Now;

        _context.ConsumableCompatiblePrinters.RemoveRange(item.CompatiblePrinters);
        item.CompatiblePrinters = printers
            .Select(x => new ConsumableCompatiblePrinter { PrinterName = x })
            .ToList();

        await _context.SaveChangesAsync();
        TempData["Success"] = "Promjene su spremljene.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Order(int id, int quantity, string? returnUrl)
    {
        if (!IsQuantityValid(quantity, "Količina za narudžbu", returnUrl, out var errorResult))
            return errorResult!;

        var item = await _context.PrinterConsumables.FindAsync(id);
        if (item == null)
            return NotFound();

        if (item.QuantityOrdered > MaximumQuantity - quantity)
        {
            TempData["Error"] = $"Ukupna naručena količina ne smije biti veća od {MaximumQuantity}.";
            return RedirectBack(returnUrl);
        }

        item.QuantityOrdered += quantity;
        item.UpdatedAt = DateTime.Now;
        AddTransaction(item, ConsumableTransactionType.Naruceno, quantity, null, null, null);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Naručeno je {quantity} kom. artikla {item.Name}.";
        return RedirectBack(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receive(int id, int quantity, string? returnUrl)
    {
        if (!IsQuantityValid(quantity, "Zaprimljena količina", returnUrl, out var errorResult))
            return errorResult!;

        var item = await _context.PrinterConsumables.FindAsync(id);
        if (item == null)
            return NotFound();

        if (quantity > item.QuantityOrdered)
        {
            TempData["Error"] = $"Možeš zaprimiti najviše {item.QuantityOrdered} kom.";
            return RedirectBack(returnUrl);
        }

        if (item.QuantityAvailable > MaximumQuantity - quantity)
        {
            TempData["Error"] = $"Ukupna dostupna količina ne smije biti veća od {MaximumQuantity}.";
            return RedirectBack(returnUrl);
        }

        item.QuantityOrdered -= quantity;
        item.QuantityAvailable += quantity;
        item.UpdatedAt = DateTime.Now;
        AddTransaction(item, ConsumableTransactionType.Zaprimljeno, quantity, null, null, null);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Zaprimljeno je {quantity} kom. artikla {item.Name}.";
        return RedirectBack(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Use(int id, int quantity, string printerName, int? siteId, string? returnUrl)
    {
        if (!IsQuantityValid(quantity, "Izdana količina", returnUrl, out var errorResult))
            return errorResult!;

        var item = await _context.PrinterConsumables
            .Include(x => x.CompatiblePrinters)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (item == null)
            return NotFound();

        var selectedPrinter = item.CompatiblePrinters
            .Select(x => x.PrinterName)
            .FirstOrDefault(x => string.Equals(x, printerName?.Trim(), StringComparison.CurrentCultureIgnoreCase));

        if (selectedPrinter == null)
        {
            TempData["Error"] = "Odaberi jedan od printera koji su spremljeni uz ovaj artikl.";
            return RedirectBack(returnUrl);
        }

        var sites = await GetSitesForPrinterNameAsync(selectedPrinter);
        var selectedSiteId = siteId ?? 0;
        var selectedSite = sites.FirstOrDefault(x => x.SiteId == selectedSiteId);

        if (selectedSite == null)
        {
            TempData["Error"] = "Odaberi radni nalog / lokaciju za odabrani printer.";
            return RedirectBack(returnUrl);
        }

        if (quantity > item.QuantityAvailable)
        {
            TempData["Error"] = $"Na stanju je samo {item.QuantityAvailable} kom. artikla {item.Name}.";
            return RedirectBack(returnUrl);
        }

        item.QuantityAvailable -= quantity;
        item.UpdatedAt = DateTime.Now;
        AddTransaction(item, ConsumableTransactionType.Izdano, quantity, selectedPrinter, selectedSite.SiteId == 0 ? null : selectedSite.SiteId, selectedSite.SiteName);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Izdano je {quantity} kom. za printer {selectedPrinter} / {selectedSite.SiteName}.";
        return RedirectBack(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string? returnUrl)
    {
        var item = await _context.PrinterConsumables
            .Include(x => x.CompatiblePrinters)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (item == null)
            return NotFound();

        _recycleBin.MoveConsumable(item);
        await _context.SaveChangesAsync();
        TempData["Success"] = $"Artikl '{item.Name}' premješten je u Nedavno obrisano.";
        return RedirectBack(returnUrl);
    }

    [HttpGet]
    public async Task<IActionResult> History(DateTime? dateFrom, DateTime? dateTo, string? searchString, string? sortOrder)
    {
        var from = (dateFrom ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var to = (dateTo ?? from.AddMonths(1).AddDays(-1)).Date;
        if (to < from)
            (from, to) = (to, from);

        var transactions = await GetHistoryTransactionsAsync(from, to, searchString, sortOrder);
        var model = new ConsumableHistoryViewModel
        {
            DateFrom = from,
            DateTo = to,
            SearchString = searchString ?? string.Empty,
            SortOrder = sortOrder ?? string.Empty,
            Transactions = transactions,
            OrderedSummary = transactions
                .Where(x => x.TransactionType == ConsumableTransactionType.Naruceno)
                .GroupBy(x => new { x.ConsumableName, x.ProductCode })
                .Select(g => new ConsumableOrderedSummaryRow
                {
                    ItemName = g.Key.ConsumableName,
                    ProductCode = g.Key.ProductCode,
                    Quantity = g.Sum(x => x.Quantity)
                })
                .OrderByDescending(x => x.Quantity)
                .ThenBy(x => x.ItemName)
                .ToList(),
            UsedSummary = transactions
                .Where(x => x.TransactionType == ConsumableTransactionType.Izdano && x.PrinterName != null)
                .GroupBy(x => new
                {
                    PrinterName = x.PrinterName!,
                    SiteName = string.IsNullOrWhiteSpace(x.SiteName) ? "Bez radnog naloga" : x.SiteName!,
                    x.ConsumableName,
                    x.ProductCode
                })
                .Select(g => new ConsumableUsedSummaryRow
                {
                    PrinterName = g.Key.PrinterName,
                    SiteName = g.Key.SiteName,
                    ItemName = g.Key.ConsumableName,
                    ProductCode = g.Key.ProductCode,
                    Quantity = g.Sum(x => x.Quantity)
                })
                .OrderBy(x => x.PrinterName)
                .ThenBy(x => x.SiteName)
                .ThenByDescending(x => x.Quantity)
                .ToList()
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> ExportHistoryExcel(DateTime? dateFrom, DateTime? dateTo, string? searchString, string? sortOrder)
    {
        var from = (dateFrom ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var to = (dateTo ?? from.AddMonths(1).AddDays(-1)).Date;
        if (to < from)
            (from, to) = (to, from);

        var transactions = await GetHistoryTransactionsAsync(from, to, searchString, sortOrder);
        var bytes = CreateHistoryWorkbook(transactions, from, to);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Evidencija_tonera_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");
    }

    private async Task<List<PrinterConsumable>> GetFilteredItemsAsync(string? searchString, string? statusFilter, string? sortOrder)
    {
        var items = await _context.PrinterConsumables
            .AsNoTracking()
            .Include(x => x.CompatiblePrinters)
            .ToListAsync();

        items = statusFilter switch
        {
            "Dostupno" => items.Where(x => x.QuantityAvailable > 0).ToList(),
            "Naruceno" => items.Where(x => x.QuantityOrdered > 0).ToList(),
            "Nema" => items.Where(x => x.QuantityAvailable == 0 && x.QuantityOrdered == 0).ToList(),
            _ => items
        };

        var tokens = TokenizeSearch(searchString);
        if (tokens.Count > 0)
            items = items.Where(x => MatchesSearch(x, tokens)).ToList();

        return sortOrder switch
        {
            "printers_desc" => items.OrderByDescending(x => x.CompatiblePrintersSummary).ThenBy(x => x.Name).ToList(),
            "name" => items.OrderBy(x => x.Name).ToList(),
            "name_desc" => items.OrderByDescending(x => x.Name).ToList(),
            "type" => items.OrderBy(x => GetEnumDisplayName(x.Type)).ThenBy(x => x.Name).ToList(),
            "type_desc" => items.OrderByDescending(x => GetEnumDisplayName(x.Type)).ThenBy(x => x.Name).ToList(),
            "color" => items.OrderBy(x => GetEnumDisplayName(x.Color)).ThenBy(x => x.Name).ToList(),
            "color_desc" => items.OrderByDescending(x => GetEnumDisplayName(x.Color)).ThenBy(x => x.Name).ToList(),
            "available" => items.OrderBy(x => x.QuantityAvailable).ThenBy(x => x.Name).ToList(),
            "available_desc" => items.OrderByDescending(x => x.QuantityAvailable).ThenBy(x => x.Name).ToList(),
            "ordered" => items.OrderBy(x => x.QuantityOrdered).ThenBy(x => x.Name).ToList(),
            "ordered_desc" => items.OrderByDescending(x => x.QuantityOrdered).ThenBy(x => x.Name).ToList(),
            "status" => items.OrderBy(GetAvailabilitySortValue).ThenBy(x => x.Name).ToList(),
            "status_desc" => items.OrderByDescending(GetAvailabilitySortValue).ThenBy(x => x.Name).ToList(),
            "original" => items.OrderByDescending(x => x.IsOriginal).ThenBy(x => x.Name).ToList(),
            "original_desc" => items.OrderBy(x => x.IsOriginal).ThenBy(x => x.Name).ToList(),
            _ => items.OrderBy(x => x.CompatiblePrintersSummary).ThenBy(x => x.Name).ToList()
        };
    }

    private async Task<List<ConsumableTransaction>> GetHistoryTransactionsAsync(DateTime from, DateTime to, string? searchString, string? sortOrder)
    {
        var until = to.AddDays(1);
        var items = await _context.ConsumableTransactions
            .AsNoTracking()
            .Where(x => x.CreatedAt >= from && x.CreatedAt < until)
            .ToListAsync();

        var tokens = TokenizeSearch(searchString);
        if (tokens.Count > 0)
        {
            items = items.Where(x =>
            {
                var searchable = RemoveDiacritics(NormalizeSearch(string.Join(" ", new[]
                {
                    x.ConsumableName, x.ProductCode, x.PrinterName, x.SiteName, x.PerformedBy,
                    GetEnumDisplayName(x.ConsumableType), GetEnumDisplayName(x.Color),
                    GetEnumDisplayName(x.TransactionType)
                }.Where(v => !string.IsNullOrWhiteSpace(v)))));
                return tokens.All(searchable.Contains);
            }).ToList();
        }

        return sortOrder switch
        {
            "date" => items.OrderBy(x => x.CreatedAt).ToList(),
            "item" => items.OrderBy(x => x.ConsumableName).ThenByDescending(x => x.CreatedAt).ToList(),
            "printer" => items.OrderBy(x => x.PrinterName).ThenBy(x => x.SiteName).ThenByDescending(x => x.CreatedAt).ToList(),
            "site" => items.OrderBy(x => x.SiteName).ThenBy(x => x.PrinterName).ThenByDescending(x => x.CreatedAt).ToList(),
            "quantity_desc" => items.OrderByDescending(x => x.Quantity).ThenByDescending(x => x.CreatedAt).ToList(),
            _ => items.OrderByDescending(x => x.CreatedAt).ToList()
        };
    }

    private async Task LoadStatsAsync()
    {
        var items = await _context.PrinterConsumables.AsNoTracking().ToListAsync();
        ViewBag.TotalCount = items.Count;
        ViewBag.AvailableCount = items.Count(x => x.QuantityAvailable > 0);
        ViewBag.OrderedCount = items.Count(x => x.QuantityOrdered > 0);
        ViewBag.MissingCount = items.Count(x => x.QuantityAvailable == 0 && x.QuantityOrdered == 0);
        ViewBag.TotalAvailableUnits = items.Sum(x => x.QuantityAvailable);
        ViewBag.TotalOrderedUnits = items.Sum(x => x.QuantityOrdered);
    }

    private async Task LoadIndexBagsAsync(string? searchString, string? statusFilter, string? sortOrder)
    {
        SetListViewBags(searchString, statusFilter, sortOrder);
        ViewBag.PrinterSiteMapJson = JsonSerializer.Serialize(await GetPrinterSiteMapAsync(), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private async Task LoadFormBagsAsync()
    {
        var names = await GetPrinterNameOptionsAsync();
        ViewBag.PrinterNameOptionsJson = JsonSerializer.Serialize(names);
        ViewBag.PrinterNameOptions = names;
    }

    private async Task<List<string>> GetPrinterNameOptionsAsync()
    {
        var rows = await _context.Equipment
            .AsNoTracking()
            .Where(x => x.EquipmentType == EquipmentType.Printer && x.Name != null && x.Name.Trim() != "")
            .Select(x => x.Name!)
            .ToListAsync();

        return rows
            .Select(x => string.Join(" ", x.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(x => x)
            .ToList();
    }

    private async Task<Dictionary<string, List<PrinterSiteOption>>> GetPrinterSiteMapAsync()
    {
        var rows = await _context.Equipment
            .AsNoTracking()
            .Include(x => x.CurrentSite)
            .Where(x => x.EquipmentType == EquipmentType.Printer && x.Name != null && x.Name.Trim() != "")
            .Select(x => new
            {
                PrinterName = x.Name!,
                SiteId = x.CurrentSiteId,
                SiteName = x.CurrentSite != null ? x.CurrentSite.Name : null
            })
            .ToListAsync();

        return rows
            .GroupBy(x => NormalizePrinterOption(x.PrinterName), StringComparer.CurrentCultureIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => new
                    {
                        SiteId = x.SiteId ?? 0,
                        SiteName = string.IsNullOrWhiteSpace(x.SiteName) ? "Bez radnog naloga" : x.SiteName!.Trim()
                    })
                    .Select(s => new PrinterSiteOption(s.Key.SiteId, s.Key.SiteName, s.Count()))
                    .OrderBy(s => s.SiteName)
                    .ToList(),
                StringComparer.CurrentCultureIgnoreCase);
    }

    private async Task<List<PrinterSiteOption>> GetSitesForPrinterNameAsync(string printerName)
    {
        var map = await GetPrinterSiteMapAsync();
        if (map.TryGetValue(NormalizePrinterOption(printerName), out var sites) && sites.Count > 0)
            return sites;

        return new List<PrinterSiteOption>
        {
            new(0, "Bez radnog naloga", 0)
        };
    }

    private void AddTransaction(PrinterConsumable item, ConsumableTransactionType type, int quantity, string? printerName, int? siteId, string? siteName)
    {
        _context.ConsumableTransactions.Add(new ConsumableTransaction
        {
            PrinterConsumableId = item.Id,
            ConsumableName = item.Name,
            ProductCode = item.ProductCode,
            ConsumableType = item.Type,
            Color = item.Color,
            TransactionType = type,
            Quantity = quantity,
            PrinterName = string.IsNullOrWhiteSpace(printerName) ? null : printerName,
            SiteId = siteId,
            SiteName = string.IsNullOrWhiteSpace(siteName) ? null : siteName,
            PerformedBy = User.Identity?.Name,
            CreatedAt = DateTime.Now
        });
    }

    private bool IsQuantityValid(int quantity, string fieldName, string? returnUrl, out IActionResult? result)
    {
        if (quantity is > 0 and <= MaximumQuantity)
        {
            result = null;
            return true;
        }

        TempData["Error"] = $"{fieldName} mora biti između 1 i {MaximumQuantity}.";
        result = RedirectBack(returnUrl);
        return false;
    }

    private async Task<bool> DuplicateExistsAsync(PrinterConsumable model, int? excludedId = null)
    {
        var query = _context.PrinterConsumables.AsNoTracking();
        if (excludedId.HasValue)
            query = query.Where(x => x.Id != excludedId.Value);

        var name = model.Name.ToLower();
        var code = model.ProductCode?.ToLower();
        if (!string.IsNullOrWhiteSpace(code) && await query.AnyAsync(x => x.ProductCode != null && x.ProductCode.ToLower() == code))
            return true;

        return await query.AnyAsync(x => x.Name.ToLower() == name && x.Type == model.Type && x.Color == model.Color);
    }

    private void ValidateCompatiblePrinters(IReadOnlyCollection<string> printers)
    {
        if (printers.Count == 0)
            ModelState.AddModelError(nameof(PrinterConsumable.CompatiblePrintersText), "Odaberi barem jedan kompatibilni printer iz popisa.");
        if (printers.Any(x => x.Length > 200))
            ModelState.AddModelError(nameof(PrinterConsumable.CompatiblePrintersText), "Naziv pojedinog printera smije imati najviše 200 znakova.");
    }

    private static List<string> ParsePrinterNames(string? value) =>
        (value ?? string.Empty)
            .Split(new[] { '\r', '\n', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizePrinterOption)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static string NormalizePrinterOption(string? value) =>
        string.Join(" ", (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static void NormalizeModel(PrinterConsumable model)
    {
        model.Name = model.Name?.Trim() ?? string.Empty;
        model.ProductCode = string.IsNullOrWhiteSpace(model.ProductCode) ? null : model.ProductCode.Trim();
        model.CompatiblePrintersText = model.CompatiblePrintersText?.Trim() ?? string.Empty;
    }

    private void SetListViewBags(string? searchString, string? statusFilter, string? sortOrder)
    {
        ViewBag.SearchString = searchString;
        ViewBag.CurrentStatusFilter = statusFilter;
        ViewBag.CurrentSort = sortOrder;
    }

    private IActionResult RedirectBack(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction(nameof(Index));

    private static bool MatchesSearch(PrinterConsumable item, List<string> tokens)
    {
        var searchable = RemoveDiacritics(NormalizeSearch(string.Join(" ", new[]
        {
            item.Name, item.ProductCode, item.CompatiblePrintersSummary,
            GetEnumDisplayName(item.Type), GetEnumDisplayName(item.Color),
            item.ProductKindText, GetAvailabilityText(item)
        }.Where(x => !string.IsNullOrWhiteSpace(x)))));
        return tokens.All(searchable.Contains);
    }

    private static List<string> TokenizeSearch(string? input) =>
        string.IsNullOrWhiteSpace(input)
            ? new List<string>()
            : RemoveDiacritics(NormalizeSearch(input))
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Distinct()
                .ToList();

    private static string NormalizeSearch(string input) => string.Join(" ", input.Trim()
        .ToLower(new CultureInfo("hr-HR"))
        .Replace("_", " ").Replace("-", " ").Replace(".", " ")
        .Replace(",", " ").Replace("/", " ")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string RemoveDiacritics(string input)
    {
        var normalized = input.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var character in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string GetAvailabilityText(PrinterConsumable item) =>
        item.QuantityAvailable > 0
            ? item.QuantityOrdered > 0 ? $"Dostupno (dolazi još {item.QuantityOrdered})" : "Dostupno"
            : item.QuantityOrdered > 0 ? "Naručeno" : "Nema";

    private static int GetAvailabilitySortValue(PrinterConsumable item) =>
        item.QuantityAvailable > 0 ? 1 : item.QuantityOrdered > 0 ? 2 : 3;

    private static string GetEnumDisplayName<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var member = typeof(TEnum).GetMember(value.ToString()).FirstOrDefault();
        return member?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? value.ToString();
    }

    private static byte[] CreateHistoryWorkbook(IReadOnlyCollection<ConsumableTransaction> transactions, DateTime from, DateTime to)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var package = new ExcelPackage();

        CreateOverviewSheet(package, transactions, from, to);
        CreateHistoryDetailsSheet(package, transactions);
        CreateUsedSummarySheet(package, transactions);
        CreateOrderedSummarySheet(package, transactions);

        return package.GetAsByteArray();
    }

    private static void CreateOverviewSheet(ExcelPackage package, IReadOnlyCollection<ConsumableTransaction> transactions, DateTime from, DateTime to)
    {
        var sheet = package.Workbook.Worksheets.Add("Sažetak mjeseca");

        sheet.Cells[1, 1].Value = "Razdoblje";
        sheet.Cells[1, 2].Value = $"{from:dd.MM.yyyy.} - {to:dd.MM.yyyy.}";
        sheet.Cells[1, 1, 1, 2].Style.Font.Bold = true;

        var ordered = transactions.Where(x => x.TransactionType == ConsumableTransactionType.Naruceno).Sum(x => x.Quantity);
        var received = transactions.Where(x => x.TransactionType == ConsumableTransactionType.Zaprimljeno).Sum(x => x.Quantity);
        var used = transactions.Where(x => x.TransactionType == ConsumableTransactionType.Izdano).Sum(x => x.Quantity);

        sheet.Cells[3, 1].Value = "Ukupno naručeno";
        sheet.Cells[3, 2].Value = ordered;
        sheet.Cells[4, 1].Value = "Ukupno zaprimljeno";
        sheet.Cells[4, 2].Value = received;
        sheet.Cells[5, 1].Value = "Ukupno potrošeno / izdano";
        sheet.Cells[5, 2].Value = used;
        sheet.Cells[3, 1, 5, 1].Style.Font.Bold = true;

        sheet.Cells[7, 1].Value = "Potrošnja po printeru, radnom nalogu i artiklu";
        sheet.Cells[7, 1].Style.Font.Bold = true;
        sheet.Cells[8, 1].Value = "Printer";
        sheet.Cells[8, 2].Value = "Radni nalog";
        sheet.Cells[8, 3].Value = "Artikl";
        sheet.Cells[8, 4].Value = "Šifra";
        sheet.Cells[8, 5].Value = "Količina";
        sheet.Cells[8, 1, 8, 5].Style.Font.Bold = true;

        var usedSummary = transactions
            .Where(x => x.TransactionType == ConsumableTransactionType.Izdano && !string.IsNullOrWhiteSpace(x.PrinterName))
            .GroupBy(x => new
            {
                Printer = x.PrinterName!,
                Site = string.IsNullOrWhiteSpace(x.SiteName) ? "Bez radnog naloga" : x.SiteName!,
                x.ConsumableName,
                x.ProductCode
            })
            .Select(g => new
            {
                g.Key.Printer,
                g.Key.Site,
                g.Key.ConsumableName,
                g.Key.ProductCode,
                Quantity = g.Sum(x => x.Quantity)
            })
            .OrderBy(x => x.Printer)
            .ThenBy(x => x.Site)
            .ThenBy(x => x.ConsumableName)
            .ToList();

        var row = 9;
        foreach (var g in usedSummary)
        {
            sheet.Cells[row, 1].Value = g.Printer;
            sheet.Cells[row, 2].Value = g.Site;
            sheet.Cells[row, 3].Value = g.ConsumableName;
            sheet.Cells[row, 4].Value = g.ProductCode;
            sheet.Cells[row, 5].Value = g.Quantity;
            row++;
        }

        if (row == 9)
        {
            sheet.Cells[row, 1].Value = "U odabranom razdoblju nema evidentirane potrošnje.";
            row++;
        }

        sheet.Cells[7, 7].Value = "Naručeno po artiklu";
        sheet.Cells[7, 7].Style.Font.Bold = true;
        sheet.Cells[8, 7].Value = "Artikl";
        sheet.Cells[8, 8].Value = "Šifra";
        sheet.Cells[8, 9].Value = "Vrsta";
        sheet.Cells[8, 10].Value = "Boja";
        sheet.Cells[8, 11].Value = "Količina";
        sheet.Cells[8, 7, 8, 11].Style.Font.Bold = true;

        var orderedSummary = transactions
            .Where(x => x.TransactionType == ConsumableTransactionType.Naruceno)
            .GroupBy(x => new { x.ConsumableName, x.ProductCode, x.ConsumableType, x.Color })
            .Select(g => new
            {
                g.Key.ConsumableName,
                g.Key.ProductCode,
                g.Key.ConsumableType,
                g.Key.Color,
                Quantity = g.Sum(x => x.Quantity)
            })
            .OrderByDescending(x => x.Quantity)
            .ThenBy(x => x.ConsumableName)
            .ToList();

        var orderRow = 9;
        foreach (var g in orderedSummary)
        {
            sheet.Cells[orderRow, 7].Value = g.ConsumableName;
            sheet.Cells[orderRow, 8].Value = g.ProductCode;
            sheet.Cells[orderRow, 9].Value = GetEnumDisplayName(g.ConsumableType);
            sheet.Cells[orderRow, 10].Value = GetEnumDisplayName(g.Color);
            sheet.Cells[orderRow, 11].Value = g.Quantity;
            orderRow++;
        }

        if (orderRow == 9)
        {
            sheet.Cells[orderRow, 7].Value = "U odabranom razdoblju nema narudžbi.";
            orderRow++;
        }

        sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        sheet.View.FreezePanes(8, 1);
    }

    private static void CreateHistoryDetailsSheet(ExcelPackage package, IReadOnlyCollection<ConsumableTransaction> transactions)
    {
        var details = package.Workbook.Worksheets.Add("Detaljna evidencija");
        var detailHeaders = new[] { "Datum", "Radnja", "Artikl", "Šifra", "Vrsta", "Boja", "Količina", "Printer", "Radni nalog", "Korisnik" };
        for (var i = 0; i < detailHeaders.Length; i++)
        {
            details.Cells[1, i + 1].Value = detailHeaders[i];
            details.Cells[1, i + 1].Style.Font.Bold = true;
        }

        var row = 2;
        foreach (var x in transactions.OrderByDescending(x => x.CreatedAt))
        {
            details.Cells[row, 1].Value = x.CreatedAt;
            details.Cells[row, 1].Style.Numberformat.Format = "dd.mm.yyyy hh:mm";
            details.Cells[row, 2].Value = GetEnumDisplayName(x.TransactionType);
            details.Cells[row, 3].Value = x.ConsumableName;
            details.Cells[row, 4].Value = x.ProductCode;
            details.Cells[row, 5].Value = GetEnumDisplayName(x.ConsumableType);
            details.Cells[row, 6].Value = GetEnumDisplayName(x.Color);
            details.Cells[row, 7].Value = x.Quantity;
            details.Cells[row, 8].Value = x.PrinterName;
            details.Cells[row, 9].Value = x.SiteName;
            details.Cells[row, 10].Value = x.PerformedBy;
            row++;
        }

        if (row == 2) row++;
        details.Tables.Add(details.Cells[1, 1, row - 1, detailHeaders.Length], "DetaljnaEvidencijaTablica").TableStyle = TableStyles.Medium2;
        details.Cells[details.Dimension.Address].AutoFitColumns();
        details.View.FreezePanes(2, 1);
    }

    private static void CreateUsedSummarySheet(ExcelPackage package, IReadOnlyCollection<ConsumableTransaction> transactions)
    {
        var sheet = package.Workbook.Worksheets.Add("Potrošnja po printeru");
        var headers = new[] { "Printer", "Radni nalog", "Artikl", "Šifra", "Količina" };
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cells[1, i + 1].Value = headers[i];
            sheet.Cells[1, i + 1].Style.Font.Bold = true;
        }

        var row = 2;
        foreach (var g in transactions.Where(x => x.TransactionType == ConsumableTransactionType.Izdano && x.PrinterName != null)
                     .GroupBy(x => new
                     {
                         PrinterName = x.PrinterName!,
                         SiteName = string.IsNullOrWhiteSpace(x.SiteName) ? "Bez radnog naloga" : x.SiteName!,
                         x.ConsumableName,
                         x.ProductCode
                     })
                     .Select(g => new
                     {
                         g.Key.PrinterName,
                         g.Key.SiteName,
                         g.Key.ConsumableName,
                         g.Key.ProductCode,
                         Quantity = g.Sum(x => x.Quantity)
                     })
                     .OrderBy(x => x.PrinterName)
                     .ThenBy(x => x.SiteName)
                     .ThenBy(x => x.ConsumableName))
        {
            sheet.Cells[row, 1].Value = g.PrinterName;
            sheet.Cells[row, 2].Value = g.SiteName;
            sheet.Cells[row, 3].Value = g.ConsumableName;
            sheet.Cells[row, 4].Value = g.ProductCode;
            sheet.Cells[row, 5].Value = g.Quantity;
            row++;
        }

        if (row == 2) row++;
        sheet.Tables.Add(sheet.Cells[1, 1, row - 1, headers.Length], "PotrosnjaPoPrinteruTablica").TableStyle = TableStyles.Medium4;
        sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        sheet.View.FreezePanes(2, 1);
    }

    private static void CreateOrderedSummarySheet(ExcelPackage package, IReadOnlyCollection<ConsumableTransaction> transactions)
    {
        var sheet = package.Workbook.Worksheets.Add("Naručeno po artiklu");
        var headers = new[] { "Artikl", "Šifra", "Vrsta", "Boja", "Količina" };
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cells[1, i + 1].Value = headers[i];
            sheet.Cells[1, i + 1].Style.Font.Bold = true;
        }

        var row = 2;
        foreach (var g in transactions.Where(x => x.TransactionType == ConsumableTransactionType.Naruceno)
                     .GroupBy(x => new { x.ConsumableName, x.ProductCode, x.ConsumableType, x.Color })
                     .Select(g => new
                     {
                         g.Key.ConsumableName,
                         g.Key.ProductCode,
                         g.Key.ConsumableType,
                         g.Key.Color,
                         Quantity = g.Sum(x => x.Quantity)
                     })
                     .OrderByDescending(x => x.Quantity)
                     .ThenBy(x => x.ConsumableName))
        {
            sheet.Cells[row, 1].Value = g.ConsumableName;
            sheet.Cells[row, 2].Value = g.ProductCode;
            sheet.Cells[row, 3].Value = GetEnumDisplayName(g.ConsumableType);
            sheet.Cells[row, 4].Value = GetEnumDisplayName(g.Color);
            sheet.Cells[row, 5].Value = g.Quantity;
            row++;
        }

        if (row == 2) row++;
        sheet.Tables.Add(sheet.Cells[1, 1, row - 1, headers.Length], "NarucenoPoArtikluTablica").TableStyle = TableStyles.Medium6;
        sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        sheet.View.FreezePanes(2, 1);
    }

    private sealed record PrinterSiteOption(int SiteId, string SiteName, int Count);
}
