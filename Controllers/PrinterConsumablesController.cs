using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ITEquipmentInventory.Data;
using ITEquipmentInventory.Models;
using ITEquipmentInventory.Models.ViewModels;
using ITEquipmentInventory.Services;
using ITEquipmentInventory.Services.Search;
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
    private const int MaximumModalQuantity = 99;
    private static readonly string[] AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly RecycleBinService _recycleBin;
    private readonly ISearchQueryService _searchQueries;
    private readonly ISearchQueryBuilder _searchBuilder;
    private readonly ISearchFuzzyMatcher _fuzzyMatcher;

    public PrinterConsumablesController(
        AppDbContext context,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        RecycleBinService recycleBin,
        ISearchQueryService searchQueries,
        ISearchQueryBuilder searchBuilder,
        ISearchFuzzyMatcher fuzzyMatcher)
    {
        _context = context;
        _environment = environment;
        _configuration = configuration;
        _recycleBin = recycleBin;
        _searchQueries = searchQueries;
        _searchBuilder = searchBuilder;
        _fuzzyMatcher = fuzzyMatcher;
    }

    [HttpGet]
    public async Task<IActionResult> SearchCompatiblePrinters(string term)
    {
        var search = _searchQueries.Parse(term);
        if (search.IsEmpty || search.Normalized.Length < 2)
            return Json(Array.Empty<object>());

        var fields = SearchProfiles.Equipment();
        var printerQuery = _context.Equipment.AsNoTracking()
            .Where(x => x.EquipmentType == EquipmentType.Printer);
        var exactQuery = _searchBuilder.WhereMatches(printerQuery, search, fields);
        var exact = await _searchBuilder.OrderByRelevance(exactQuery, search, fields)
            .ThenBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.InventoryNumber, x.SerialNumber, SiteCode = x.CurrentSite != null ? x.CurrentSite.Code : null })
            .Take(20)
            .ToListAsync(HttpContext.RequestAborted);

        var results = exact.Select(x => new
        {
            id = x.Id,
            text = x.Name ?? "Printer",
            value = x.Name ?? "Printer",
            context = BuildPrinterContext(x.InventoryNumber, x.SerialNumber, x.SiteCode),
            fuzzy = false
        }).ToList();

        if (results.Count < 8 && search.Normalized.Length >= 4)
        {
            var exactIds = exact.Select(x => x.Id).ToHashSet();
            var candidates = await printerQuery
                .Where(x => !exactIds.Contains(x.Id))
                .OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name, x.InventoryNumber, x.SerialNumber, SiteCode = x.CurrentSite != null ? x.CurrentSite.Code : null })
                .Take(150)
                .ToListAsync(HttpContext.RequestAborted);

            results.AddRange(candidates
                .Where(x => _fuzzyMatcher.IsMatch(search, x.Name, x.InventoryNumber, x.SerialNumber, x.SiteCode, "printer pisac"))
                .Take(20 - results.Count)
                .Select(x => new
                {
                    id = x.Id,
                    text = x.Name ?? "Printer",
                    value = x.Name ?? "Printer",
                    context = BuildPrinterContext(x.InventoryNumber, x.SerialNumber, x.SiteCode),
                    fuzzy = true
                }));
        }

        return Json(results);
    }

    private static string BuildPrinterContext(string? inventory, string? serial, string? siteCode) =>
        string.Join(" · ", new[]
        {
            string.IsNullOrWhiteSpace(inventory) ? null : "Inventurni broj: " + inventory,
            string.IsNullOrWhiteSpace(serial) ? null : "SN: " + serial,
            siteCode
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

    [HttpGet]
    public async Task<IActionResult> SearchConsumableSuggestions(string term, string? statusFilter)
    {
        var search = _searchQueries.Parse(term);
        if (search.IsEmpty || search.Normalized.Length < 2)
            return Json(Array.Empty<object>());

        var ids = await FindConsumableIdsAsync(search, HttpContext.RequestAborted);
        var itemQuery = _context.PrinterConsumables.AsNoTracking()
                .Include(x => x.CompatiblePrinters)
                .Where(x => ids.Contains(x.Id));
        itemQuery = statusFilter switch
        {
            "Dostupno" => itemQuery.Where(x => x.QuantityAvailable > 0),
            "Naruceno" => itemQuery.Where(x => x.QuantityOrdered > 0),
            "Nema" => itemQuery.Where(x => x.QuantityAvailable == 0 && x.QuantityOrdered == 0),
            _ => itemQuery
        };
        var rawItems = ids.Count == 0
            ? new List<PrinterConsumable>()
            : await itemQuery
                .Take(80)
                .ToListAsync(HttpContext.RequestAborted);

        var items = BuildFamilyDisplayItems(rawItems)
            .OrderByDescending(x => ConsumableScore(x, search))
            .ThenBy(x => x.Name)
            .Take(20)
            .Select(x => new
            {
                id = x.Id,
                text = x.Name,
                value = x.Name,
                context = string.Join(" · ", new[]
                {
                    string.IsNullOrWhiteSpace(x.ProductCode) ? null : "Šifra: " + x.ProductCode,
                    GetEnumDisplayName(x.Type),
                    x.IsOriginal ? "Originalni" : "Zamjenski",
                    $"Dostupno: {x.QuantityAvailable}",
                    $"Naručeno: {x.QuantityOrdered}",
                    string.IsNullOrWhiteSpace(x.CompatiblePrintersSummary) ? null : "Printeri: " + x.CompatiblePrintersSummary
                }.Where(value => !string.IsNullOrWhiteSpace(value)))
            })
            .ToList();

        return Json(items);
    }

    [HttpGet]
    public async Task<IActionResult> SearchHistorySuggestions(string term, DateTime? dateFrom, DateTime? dateTo)
    {
        var search = _searchQueries.Parse(term);
        if (search.IsEmpty || search.Normalized.Length < 2)
            return Json(Array.Empty<object>());

        var from = (dateFrom ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var to = (dateTo ?? from.AddMonths(1).AddDays(-1)).Date;
        if (to < from) (from, to) = (to, from);
        var until = to.AddDays(1);
        var fields = SearchProfiles.ConsumableTransactions();
        var historyQuery = _context.ConsumableTransactions.AsNoTracking()
            .Where(x => x.CreatedAt >= from && x.CreatedAt < until);
        var matching = _searchBuilder.WhereMatches(historyQuery, search, fields);
        var rows = await _searchBuilder.OrderByRelevance(matching, search, fields)
            .ThenByDescending(x => x.CreatedAt)
            .Take(40)
            .Select(x => new
            {
                x.Id,
                x.ConsumableName,
                x.ProductCode,
                x.PrinterName,
                x.SiteName,
                x.PerformedBy,
                x.TransactionType,
                x.Quantity,
                x.CreatedAt
            })
            .ToListAsync(HttpContext.RequestAborted);

        var results = rows
            .GroupBy(x => new { x.ConsumableName, x.ProductCode, x.PrinterName, x.SiteName, x.TransactionType })
            .Select(group => group.First())
            .Take(20)
            .Select(x => new
            {
                id = x.Id,
                text = x.ConsumableName,
                value = !string.IsNullOrWhiteSpace(x.ProductCode) ? x.ProductCode : x.ConsumableName,
                context = string.Join(" · ", new[]
                {
                    x.ProductCode,
                    x.TransactionType == ConsumableTransactionType.Naruceno ? "Naručeno" :
                        x.TransactionType == ConsumableTransactionType.Zaprimljeno ? "Zaprimljeno" : "Izdano / potrošeno",
                    string.IsNullOrWhiteSpace(x.PrinterName) ? null : "Printer: " + x.PrinterName,
                    string.IsNullOrWhiteSpace(x.SiteName) ? null : "Radni nalog: " + x.SiteName,
                    $"Količina: {x.Quantity}",
                    x.CreatedAt.ToString("dd.MM.yyyy. HH:mm")
                }.Where(value => !string.IsNullOrWhiteSpace(value)))
            })
            .ToList();

        return Json(results);
    }

    private async Task<HashSet<int>> FindConsumableIdsAsync(SearchQuery search, CancellationToken cancellationToken)
    {
        HashSet<int>? matchingIds = null;
        foreach (var group in search.Groups.Take(10))
        {
            var groupQuery = new SearchQuery(search.Original, group.Token,
                string.Concat(group.Token.Where(char.IsLetterOrDigit)), [group]);
            var itemIds = await _searchBuilder
                .WhereMatches(_context.PrinterConsumables.AsNoTracking(), groupQuery, SearchProfiles.Consumables())
                .Select(x => x.Id)
                .Take(500)
                .ToListAsync(cancellationToken);
            var printerIds = await _searchBuilder
                .WhereMatches(_context.ConsumableCompatiblePrinters.AsNoTracking(), groupQuery, SearchProfiles.CompatiblePrinters())
                .Select(x => x.PrinterConsumableId)
                .Take(500)
                .ToListAsync(cancellationToken);

            var groupIds = itemIds.Concat(printerIds).ToHashSet();
            if (matchingIds == null)
                matchingIds = groupIds;
            else
                matchingIds.IntersectWith(groupIds);

            if (matchingIds.Count == 0)
                break;
        }

        return matchingIds ?? [];
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? searchString, string? statusFilter, string? sortOrder, int page = 1)
    {
        await EnsurePendingOrdersBackfilledAsync();
        var result = await GetFilteredItemsAsync(searchString, statusFilter, sortOrder, page);
        await LoadStatsAsync();
        await LoadIndexBagsAsync(searchString, statusFilter, sortOrder);
        await LoadFormBagsAsync();
        SetPaginationViewBags(result);
        return View(result.Items);
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel(string? searchString, string? statusFilter, string? sortOrder)
    {
        var items = await GetFilteredItemsAsync(searchString, statusFilter, sortOrder, null);
        var bytes = ExcelExportHelper.CreateExcel(
            "Toneri i tinte",
            new[]
            {
                "Kompatibilni printeri", "Naziv artikla", "Šifra artikla", "Vrsta",
                "Original / zamjenski", "Stanje po bojama", "Dostupno", "Naručeno", "Status", "Zadnja izmjena"
            },
            items.Items.Select(x => new object?[]
            {
                x.CompatiblePrintersSummary, x.Name, x.ProductCode, GetEnumDisplayName(x.Type),
                x.ProductKindText, x.ColorStatesSummary, x.QuantityAvailable,
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
        return View(new PrinterConsumableCreateViewModel
        {
            Type = ConsumableType.Toner,
            IsOriginal = true
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PrinterConsumableCreateViewModel model)
    {
        NormalizeModel(model);
        if (string.IsNullOrWhiteSpace(model.Name))
            ModelState.AddModelError(nameof(PrinterConsumableFormViewModel.Name), "Naziv proizvoda je obavezan.");
        ValidateProductImage(model);
        var printers = ParsePrinterNames(model.CompatiblePrintersText);
        await ValidateCompatiblePrintersAsync(printers);

        if (await DuplicateExistsAsync(model))
            ModelState.AddModelError(string.Empty, "Artikl s istom šifrom ili istim nazivom već postoji.");

        if (!ModelState.IsValid)
        {
            await LoadFormBagsAsync();
            return View(model);
        }

        var consumable = new PrinterConsumable
        {
            Name = model.Name,
            ProductCode = model.ProductCode,
            Type = model.Type,
            Color = ConsumableColor.NijePrimjenjivo,
            QuantityAvailable = model.QuantityAvailable,
            QuantityOrdered = model.QuantityOrdered,
            IsOriginal = model.IsOriginal,
            CreatedAt = DateTime.Now,
            UpdatedAt = null
        };

        if (consumable.UsesStandardColors)
        {
            // Kod tonera i tinte boje se vode kroz Naruči/Zaprimi/Izdaj, ne u osnovnom obrascu.
            consumable.SetColorStates(CreateEmptyStandardColorStates());
        }
        else
        {
            consumable.SetColorStates([
                new ConsumableColorState(ConsumableColor.NijePrimjenjivo, consumable.QuantityAvailable, consumable.QuantityOrdered)
            ]);
        }

        consumable.CompatiblePrinters = printers
            .Select(printer => new ConsumableCompatiblePrinter { PrinterName = printer })
            .ToList();

        _context.PrinterConsumables.Add(consumable);
        await _context.SaveChangesAsync();
        await SaveConsumableImageAsync(consumable, model.ProductImage);

        if (consumable.QuantityOrdered > 0)
        {
            AddPendingOrder(consumable, ConsumableColor.NijePrimjenjivo, consumable.QuantityOrdered);
            AddTransaction(consumable, ConsumableTransactionType.Naruceno, consumable.QuantityOrdered, null, null, null, ConsumableColor.NijePrimjenjivo);
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

        var family = await GetFamilyItemsAsync(item, includePrinters: true);
        var display = BuildFamilyDisplayItem(family);
        display.CompatiblePrintersText = string.Join(Environment.NewLine,
            display.CompatiblePrinters.OrderBy(x => x.PrinterName).Select(x => x.PrinterName));

        await LoadFormBagsAsync();
        return View(new PrinterConsumableEditViewModel
        {
            Id = display.Id,
            Name = display.Name,
            ProductCode = display.ProductCode,
            Type = display.Type,
            QuantityAvailable = display.QuantityAvailable,
            QuantityOrdered = display.QuantityOrdered,
            IsOriginal = display.IsOriginal,
            CompatiblePrintersText = display.CompatiblePrintersText,
            ImageUrl = display.ImageUrl,
            RowVersion = display.RowVersion
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PrinterConsumableEditViewModel model)
    {
        if (id != model.Id)
            return NotFound();

        var original = await _context.PrinterConsumables.FirstOrDefaultAsync(x => x.Id == id);
        if (original == null)
            return NotFound();

        _context.Entry(original).Property(x => x.RowVersion).OriginalValue = model.RowVersion;

        var family = await GetFamilyItemsAsync(original, includePrinters: true, includePendingOrders: true);
        var originalFamilyIds = family.Select(x => x.Id).ToHashSet();

        NormalizeModel(model);
        if (string.IsNullOrWhiteSpace(model.Name))
            ModelState.AddModelError(nameof(PrinterConsumableFormViewModel.Name), "Naziv proizvoda je obavezan.");
        ValidateProductImage(model);

        // Ako uređivanje pretvara stari zasebni zapis u već postojeću zajedničku obitelj,
        // spoji ih bez gubitka količina, printera, narudžbi ili povijesti.
        var targetAnchor = new PrinterConsumable
        {
            Name = model.Name,
            ProductCode = model.ProductCode,
            Type = model.Type,
            IsOriginal = model.IsOriginal
        };
        var targetFamily = await GetFamilyItemsAsync(targetAnchor, includePrinters: true, includePendingOrders: true);
        family = family.Concat(targetFamily).DistinctBy(x => x.Id).OrderBy(x => x.Id).ToList();
        var familyIds = family.Select(x => x.Id).ToHashSet();

        var printers = ParsePrinterNames(model.CompatiblePrintersText);
        if (targetFamily.Any(x => !originalFamilyIds.Contains(x.Id)))
        {
            printers = printers
                .Concat(targetFamily
                    .Where(x => !originalFamilyIds.Contains(x.Id))
                    .SelectMany(x => x.CompatiblePrinters)
                    .Select(x => NormalizePrinterOption(x.PrinterName)))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            model.CompatiblePrintersText = string.Join(Environment.NewLine, printers);
        }

        await ValidateCompatiblePrintersAsync(printers);

        if (await DuplicateExistsAsync(model, familyIds))
            ModelState.AddModelError(string.Empty, "Artikl s istom šifrom ili istim nazivom već postoji.");

        if (!ModelState.IsValid)
        {
            var display = BuildFamilyDisplayItem(family);
            model.QuantityAvailable = display.QuantityAvailable;
            model.QuantityOrdered = display.QuantityOrdered;
            model.ImageUrl = display.ImageUrl;
            await LoadFormBagsAsync();
            return View(model);
        }

        var canonical = GetCanonicalFamilyItem(family);
        var currentStates = AggregateFamilyStates(family);
        var nextStates = NormalizeStatesForType(currentStates, model.Type);
        var now = DateTime.Now;

        foreach (var familyItem in family)
        {
            familyItem.Name = model.Name;
            familyItem.ProductCode = model.ProductCode;
            familyItem.Type = model.Type;
            familyItem.IsOriginal = model.IsOriginal;
            familyItem.UpdatedAt = now;
        }

        canonical.SetColorStates(nextStates);
        foreach (var legacyVariant in family.Where(x => x.Id != canonical.Id))
        {
            legacyVariant.Color = ConsumableColor.NijePrimjenjivo;
            legacyVariant.ColorStateJson = null;
            legacyVariant.QuantityAvailable = 0;
            legacyVariant.QuantityOrdered = 0;
        }

        _context.ConsumableCompatiblePrinters.RemoveRange(family.SelectMany(x => x.CompatiblePrinters));
        canonical.CompatiblePrinters = printers
            .Select(x => new ConsumableCompatiblePrinter { PrinterName = x })
            .ToList();

        var pendingOrders = await _context.ConsumablePendingOrders
            .Where(x => familyIds.Contains(x.PrinterConsumableId))
            .ToListAsync();
        foreach (var pendingOrder in pendingOrders)
        {
            pendingOrder.PrinterConsumableId = canonical.Id;
            pendingOrder.Color = model.Type is ConsumableType.Toner or ConsumableType.Tinta
                ? pendingOrder.Color is ConsumableColor.Cyan or ConsumableColor.Magenta or ConsumableColor.Zuta or ConsumableColor.Crna
                    ? pendingOrder.Color
                    : ConsumableColor.Crna
                : ConsumableColor.NijePrimjenjivo;
        }

        var transactions = await _context.ConsumableTransactions
            .Where(x => x.PrinterConsumableId.HasValue && familyIds.Contains(x.PrinterConsumableId.Value))
            .ToListAsync();
        foreach (var consumableTransaction in transactions)
            consumableTransaction.PrinterConsumableId = canonical.Id;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["Error"] = "Zapis je u međuvremenu izmijenio drugi korisnik. Vaše promjene nisu spremljene. Osvježite podatke i pokušajte ponovno.";
            return RedirectToAction(nameof(Edit), new { id });
        }
        await SaveConsumableImageAsync(canonical, model.ProductImage);
        TempData["Success"] = "Promjene su spremljene.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Order(
        int id,
        int cyanQuantity,
        int magentaQuantity,
        int yellowQuantity,
        int blackQuantity,
        int totalQuantity,
        string? returnUrl)
    {
        var selected = await _context.PrinterConsumables.FirstOrDefaultAsync(x => x.Id == id);
        if (selected == null)
            return NotFound();

        var family = await GetFamilyItemsAsync(selected);
        var canonical = GetCanonicalFamilyItem(family);
        var quantities = BuildRequestedQuantities(canonical.Type, cyanQuantity, magentaQuantity, yellowQuantity, blackQuantity, totalQuantity);

        if (!ValidateModalQuantities(quantities, "narudžbu", returnUrl, out var validationResult))
            return validationResult!;

        await using var transaction = await _context.Database.BeginTransactionAsync();

        if (!TryApplyStockChange(family, canonical, quantities, StockChangeType.Order, out var message))
        {
            TempData["Error"] = message;
            return RedirectBack(returnUrl);
        }

        var orderGroupId = Guid.NewGuid();
        var orderedAt = DateTime.Now;
        foreach (var pair in quantities.Where(x => x.Value > 0))
        {
            AddPendingOrder(canonical, pair.Key, pair.Value, orderGroupId, orderedAt);
            AddTransaction(canonical, ConsumableTransactionType.Naruceno, pair.Value, null, null, null, pair.Key);
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["Success"] = $"Naručeno je {quantities.Values.Sum()} kom. artikla {canonical.Name}.";
        return RedirectBack(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receive(
        int? orderId,
        int? id,
        string? color,
        int? quantity,
        string? returnUrl)
    {
        await EnsurePendingOrdersBackfilledAsync();

        ConsumablePendingOrder? selectedOrder = null;
        PrinterConsumable? selectedProduct = null;
        ConsumableColor? selectedColor = null;
        int receiveQuantity;
        List<ConsumablePendingOrder> ordersToUpdate = [];
        Dictionary<ConsumableColor, int> receiveQuantities = [];

        if (orderId.HasValue)
        {
            selectedOrder = await _context.ConsumablePendingOrders
                .Include(x => x.PrinterConsumable)
                .FirstOrDefaultAsync(x => x.Id == orderId.Value);

            if (selectedOrder == null)
                return ReceiveError("Narudžba više ne postoji.", returnUrl, StatusCodes.Status404NotFound);

            receiveQuantity = selectedOrder.RemainingQuantity;
            if (receiveQuantity <= 0)
            {
                return ReceiveError("Ta je narudžba već zaprimljena.", returnUrl);
            }

            selectedProduct = selectedOrder.PrinterConsumable;
        }
        else
        {
            if (!id.HasValue || !quantity.HasValue)
            {
                return ReceiveError("Nedostaju podaci za zaprimanje.", returnUrl);
            }

            if (quantity.Value is <= 0 or > MaximumQuantity)
                return ReceiveError($"Zaprimljena količina mora biti između 1 i {MaximumQuantity}.", returnUrl);

            if (!TryParseColor(color, out var parsedColor))
            {
                return ReceiveError("Nije moguće prepoznati boju za zaprimanje.", returnUrl);
            }

            selectedColor = parsedColor;

            selectedProduct = await _context.PrinterConsumables.FirstOrDefaultAsync(x => x.Id == id.Value);
            if (selectedProduct == null)
                return ReceiveError("Proizvod više ne postoji.", returnUrl, StatusCodes.Status404NotFound);

            receiveQuantity = quantity.Value;
        }

        var family = await GetFamilyItemsAsync(selectedProduct);
        var canonical = GetCanonicalFamilyItem(family);
        var familyIds = family.Select(x => x.Id).ToHashSet();

        if (selectedOrder != null)
        {
            ordersToUpdate = selectedOrder.OrderGroupId.HasValue
                ? await _context.ConsumablePendingOrders
                    .Where(x => familyIds.Contains(x.PrinterConsumableId)
                        && x.OrderGroupId == selectedOrder.OrderGroupId
                        && x.CompletedAt == null
                        && x.QuantityOrdered > x.QuantityReceived)
                    .OrderBy(x => x.Id)
                    .ToListAsync()
                : [selectedOrder];

            receiveQuantities = ordersToUpdate
                .GroupBy(x => x.Color)
                .ToDictionary(group => group.Key, group => group.Sum(order => order.RemainingQuantity));
            receiveQuantity = receiveQuantities.Values.Sum();

            if (receiveQuantity <= 0)
            {
                return ReceiveError("Ta je narudžba već zaprimljena.", returnUrl);
            }
        }
        else
        {
            var pending = await _context.ConsumablePendingOrders
                .Where(x => familyIds.Contains(x.PrinterConsumableId)
                    && x.Color == selectedColor!.Value
                    && x.CompletedAt == null
                    && x.QuantityOrdered > x.QuantityReceived)
                .OrderBy(x => x.OrderedAt)
                .ToListAsync();

            if (pending.Sum(x => x.RemainingQuantity) < receiveQuantity)
            {
                return ReceiveError("Ne postoji dovoljna naručena količina za zaprimanje.", returnUrl);
            }

            var remainingToAllocate = receiveQuantity;
            foreach (var pendingOrder in pending)
            {
                if (remainingToAllocate == 0)
                    break;

                var take = Math.Min(remainingToAllocate, pendingOrder.RemainingQuantity);
                pendingOrder.QuantityReceived += take;
                if (pendingOrder.RemainingQuantity == 0)
                    pendingOrder.CompletedAt = DateTime.Now;
                ordersToUpdate.Add(pendingOrder);
                remainingToAllocate -= take;
            }

            receiveQuantities[selectedColor!.Value] = receiveQuantity;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();

        if (!TryApplyStockChange(
                family,
                canonical,
                receiveQuantities,
                StockChangeType.Receive,
                out var message))
        {
            return ReceiveError(message, returnUrl);
        }

        if (selectedOrder != null)
        {
            var completedAt = DateTime.Now;
            foreach (var pendingOrder in ordersToUpdate)
            {
                pendingOrder.QuantityReceived += pendingOrder.RemainingQuantity;
                pendingOrder.CompletedAt = completedAt;
                pendingOrder.PrinterConsumableId = canonical.Id;
            }
        }
        else
        {
            foreach (var pendingOrder in ordersToUpdate)
                pendingOrder.PrinterConsumableId = canonical.Id;
        }

        foreach (var pair in receiveQuantities.Where(x => x.Value > 0))
            AddTransaction(canonical, ConsumableTransactionType.Zaprimljeno, pair.Value, null, null, null, pair.Key);
        await _context.SaveChangesAsync();

        var successMessage = $"Zaprimljeno je {receiveQuantity} kom. artikla {canonical.Name}.";
        if (IsAjaxRequest())
        {
            var product = await BuildClientProductDataAsync(canonical);
            var summary = await BuildClientSummaryAsync();
            await transaction.CommitAsync();
            return Json(new
            {
                success = true,
                message = successMessage,
                product,
                summary
            });
        }

        await transaction.CommitAsync();
        TempData["Success"] = successMessage;
        return RedirectBack(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Use(
        int id,
        int cyanQuantity,
        int magentaQuantity,
        int yellowQuantity,
        int blackQuantity,
        int totalQuantity,
        string printerName,
        int? siteId,
        string? returnUrl)
    {
        var selected = await _context.PrinterConsumables.FirstOrDefaultAsync(x => x.Id == id);
        if (selected == null)
            return NotFound();

        var family = await GetFamilyItemsAsync(selected, includePrinters: true);
        var canonical = GetCanonicalFamilyItem(family);
        var quantities = BuildRequestedQuantities(canonical.Type, cyanQuantity, magentaQuantity, yellowQuantity, blackQuantity, totalQuantity);

        if (!ValidateModalQuantities(quantities, "izdavanje", returnUrl, out var validationResult))
            return validationResult!;

        var selectedPrinter = family
            .SelectMany(x => x.CompatiblePrinters)
            .Select(x => x.PrinterName)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
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

        await using var transaction = await _context.Database.BeginTransactionAsync();

        if (!TryApplyStockChange(family, canonical, quantities, StockChangeType.Use, out var message))
        {
            TempData["Error"] = message;
            return RedirectBack(returnUrl);
        }

        foreach (var pair in quantities.Where(x => x.Value > 0))
        {
            AddTransaction(
                canonical,
                ConsumableTransactionType.Izdano,
                pair.Value,
                selectedPrinter,
                selectedSite.SiteId == 0 ? null : selectedSite.SiteId,
                selectedSite.SiteName,
                pair.Key);
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["Success"] = $"Izdano je {quantities.Values.Sum()} kom. za printer {selectedPrinter} / {selectedSite.SiteName}.";
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

        var family = await GetFamilyItemsAsync(item, includePrinters: true, includePendingOrders: true);
        foreach (var familyItem in family)
            _recycleBin.MoveConsumable(familyItem);

        await _context.SaveChangesAsync();
        TempData["Success"] = $"Artikl '{item.Name}' premješten je u Nedavno obrisano.";
        return RedirectBack(returnUrl);
    }

    [HttpGet]
    public async Task<IActionResult> History(DateTime? dateFrom, DateTime? dateTo, string? searchString, string? sortOrder, int page = 1)
    {
        var from = (dateFrom ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var to = (dateTo ?? from.AddMonths(1).AddDays(-1)).Date;
        if (to < from)
            (from, to) = (to, from);

        var history = await GetPagedHistoryTransactionsAsync(from, to, searchString, sortOrder, page);
        var transactions = history.Items;
        var model = new ConsumableHistoryViewModel
        {
            DateFrom = from,
            DateTo = to,
            SearchString = searchString ?? string.Empty,
            SortOrder = sortOrder ?? string.Empty,
            Transactions = transactions,
            CurrentPage = history.CurrentPage,
            TotalPages = history.TotalPages,
            TotalCount = history.TotalCount,
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

    private async Task<PagedResult<PrinterConsumable>> GetFilteredItemsAsync(string? searchString, string? statusFilter, string? sortOrder, int? page)
    {
        var search = _searchQueries.Parse(searchString);
        var matchingIds = search.IsEmpty
            ? null
            : await FindConsumableIdsAsync(search, HttpContext.RequestAborted);
        IQueryable<PrinterConsumable> query = _context.PrinterConsumables
            .AsNoTracking()
            .Include(x => x.CompatiblePrinters)
            .Include(x => x.Transactions)
            .Include(x => x.PendingOrders);
        if (matchingIds is not null)
            query = query.Where(x => matchingIds.Contains(x.Id));
        query = statusFilter switch
        {
            "Dostupno" => query.Where(x => x.QuantityAvailable > 0),
            "Naruceno" => query.Where(x => x.QuantityOrdered > 0),
            "Nema" => query.Where(x => x.QuantityAvailable == 0 && x.QuantityOrdered == 0),
            _ => query
        };
        query = sortOrder switch
        {
            "printers_desc" => query.OrderByDescending(x => x.CompatiblePrinters.Select(p => p.PrinterName).FirstOrDefault()).ThenBy(x => x.Name),
            "name" => query.OrderBy(x => x.Name),
            "name_desc" => query.OrderByDescending(x => x.Name),
            "type" => query.OrderBy(x => x.Type).ThenBy(x => x.Name),
            "type_desc" => query.OrderByDescending(x => x.Type).ThenBy(x => x.Name),
            "available" => query.OrderBy(x => x.QuantityAvailable).ThenBy(x => x.Name),
            "available_desc" => query.OrderByDescending(x => x.QuantityAvailable).ThenBy(x => x.Name),
            "ordered" => query.OrderBy(x => x.QuantityOrdered).ThenBy(x => x.Name),
            "ordered_desc" => query.OrderByDescending(x => x.QuantityOrdered).ThenBy(x => x.Name),
            "status" => query.OrderBy(x => x.QuantityAvailable > 0 ? 0 : x.QuantityOrdered > 0 ? 1 : 2).ThenBy(x => x.Name),
            "status_desc" => query.OrderByDescending(x => x.QuantityAvailable > 0 ? 0 : x.QuantityOrdered > 0 ? 1 : 2).ThenBy(x => x.Name),
            "original" => query.OrderByDescending(x => x.IsOriginal).ThenBy(x => x.Name),
            "original_desc" => query.OrderBy(x => x.IsOriginal).ThenBy(x => x.Name),
            _ => query.OrderBy(x => x.Name)
        };
        var totalCount = await query.CountAsync(HttpContext.RequestAborted);
        if (page is null)
            return new PagedResult<PrinterConsumable> { Items = BuildFamilyDisplayItems(await query.ToListAsync(HttpContext.RequestAborted)), CurrentPage = 1, TotalPages = 1, TotalCount = totalCount };
        var currentPage = Math.Min(Math.Max(1, page.Value), Math.Max(1, (int)Math.Ceiling(totalCount / (double)PaginationConstants.DefaultPageSize)));
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PaginationConstants.DefaultPageSize));
        var items = await query.Skip((currentPage - 1) * PaginationConstants.DefaultPageSize).Take(PaginationConstants.DefaultPageSize).ToListAsync(HttpContext.RequestAborted);
        return new PagedResult<PrinterConsumable> { Items = BuildFamilyDisplayItems(items), CurrentPage = currentPage, TotalPages = totalPages, TotalCount = totalCount };
    }

    private void SetPaginationViewBags<T>(PagedResult<T> result)
    {
        ViewBag.CurrentPage = result.CurrentPage;
        ViewBag.TotalPages = result.TotalPages;
        ViewBag.FilteredCount = result.TotalCount;
    }

    private bool ConsumableMatches(PrinterConsumable item, SearchQuery search) => _searchQueries.Matches(search,
        item.Name,
        item.ProductCode,
        item.CompatiblePrintersSummary,
        GetEnumDisplayName(item.Type),
        GetEnumDisplayName(item.Color),
        item.ProductKindText,
        item.ColorStatesSummary,
        GetAvailabilityText(item),
        item.QuantityAvailable.ToString(CultureInfo.InvariantCulture),
        item.QuantityOrdered.ToString(CultureInfo.InvariantCulture));

    private int ConsumableScore(PrinterConsumable item, SearchQuery search) => _searchQueries.Score(search,
        new SearchValue(item.ProductCode, 100, true),
        new SearchValue(item.Name, 80),
        new SearchValue(item.CompatiblePrintersSummary, 60),
        new SearchValue(GetEnumDisplayName(item.Type), 40),
        new SearchValue(item.ProductKindText, 30),
        new SearchValue(GetAvailabilityText(item), 20));

    private async Task<List<ConsumableTransaction>> GetHistoryTransactionsAsync(DateTime from, DateTime to, string? searchString, string? sortOrder)
    {
        var query = BuildHistoryTransactionQuery(from, to, searchString, sortOrder);
        return await query.ToListAsync(HttpContext.RequestAborted);
    }

    private IQueryable<ConsumableTransaction> BuildHistoryTransactionQuery(DateTime from, DateTime to, string? searchString, string? sortOrder)
    {
        var until = to.AddDays(1);
        var query = _context.ConsumableTransactions
            .AsNoTracking()
            .Where(x => x.CreatedAt >= from && x.CreatedAt < until);
        var search = _searchQueries.Parse(searchString);
        var fields = SearchProfiles.ConsumableTransactions();
        query = _searchBuilder.WhereMatches(query, search, fields);
        var ranked = _searchBuilder.OrderByRelevance(query, search, fields);

        query = sortOrder switch
        {
            "date" => ranked.ThenBy(x => x.CreatedAt),
            "item" => ranked.ThenBy(x => x.ConsumableName).ThenByDescending(x => x.CreatedAt),
            "printer" => ranked.ThenBy(x => x.PrinterName).ThenBy(x => x.SiteName).ThenByDescending(x => x.CreatedAt),
            "site" => ranked.ThenBy(x => x.SiteName).ThenBy(x => x.PrinterName).ThenByDescending(x => x.CreatedAt),
            "quantity_desc" => ranked.ThenByDescending(x => x.Quantity).ThenByDescending(x => x.CreatedAt),
            _ => ranked.ThenByDescending(x => x.CreatedAt)
        };
        return query;
    }

    private async Task<PagedResult<ConsumableTransaction>> GetPagedHistoryTransactionsAsync(DateTime from, DateTime to, string? searchString, string? sortOrder, int page)
    {
        var query = BuildHistoryTransactionQuery(from, to, searchString, sortOrder);
        var totalCount = await query.CountAsync(HttpContext.RequestAborted);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PaginationConstants.DefaultPageSize));
        var currentPage = Math.Min(Math.Max(1, page), totalPages);
        var items = await query.Skip((currentPage - 1) * PaginationConstants.DefaultPageSize)
            .Take(PaginationConstants.DefaultPageSize)
            .ToListAsync(HttpContext.RequestAborted);
        return new PagedResult<ConsumableTransaction> { Items = items, CurrentPage = currentPage, TotalPages = totalPages, TotalCount = totalCount };
    }

    private async Task LoadStatsAsync()
    {
        var rawItems = await _context.PrinterConsumables.AsNoTracking().ToListAsync();
        var families = BuildFamilyDisplayItems(rawItems);
        ViewBag.TotalCount = families.Count;
        ViewBag.AvailableCount = families.Count(x => x.QuantityAvailable > 0);
        ViewBag.OrderedCount = families.Count(x => x.QuantityOrdered > 0);
        ViewBag.MissingCount = families.Count(x => x.QuantityAvailable == 0 && x.QuantityOrdered == 0);
        ViewBag.TotalAvailableUnits = families.Sum(x => x.QuantityAvailable);
        ViewBag.TotalOrderedUnits = families.Sum(x => x.QuantityOrdered);
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

        return [];
    }

    private void AddTransaction(PrinterConsumable item, ConsumableTransactionType type, int quantity, string? printerName, int? siteId, string? siteName)
    {
        AddTransaction(item, type, quantity, printerName, siteId, siteName, item.Color);
    }

    private void AddTransaction(PrinterConsumable item, ConsumableTransactionType type, int quantity, string? printerName, int? siteId, string? siteName, ConsumableColor color)
    {
        _context.ConsumableTransactions.Add(new ConsumableTransaction
        {
            PrinterConsumableId = item.Id,
            ConsumableName = item.Name,
            ProductCode = item.ProductCode,
            ConsumableType = item.Type,
            Color = color,
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

    private async Task<bool> DuplicateExistsAsync(PrinterConsumableFormViewModel model, IReadOnlySet<int>? excludedIds = null)
    {
        var query = _context.PrinterConsumables.AsNoTracking().AsQueryable();
        if (excludedIds is { Count: > 0 })
            query = query.Where(x => !excludedIds.Contains(x.Id));

        var name = model.Name.ToLower();
        var code = model.ProductCode?.ToLower();
        if (!string.IsNullOrWhiteSpace(code)
            && await query.AnyAsync(x => x.ProductCode != null && x.ProductCode.ToLower() == code))
        {
            return true;
        }

        return await query.AnyAsync(x => x.Name.ToLower() == name && x.Type == model.Type && x.IsOriginal == model.IsOriginal);
    }

    private async Task ValidateCompatiblePrintersAsync(IReadOnlyCollection<string> printers)
    {
        if (printers.Count == 0)
        {
            ModelState.AddModelError(nameof(PrinterConsumable.CompatiblePrintersText), "Odaberi barem jedan kompatibilni printer iz baze.");
            return;
        }

        if (printers.Any(x => x.Length > 200))
            ModelState.AddModelError(nameof(PrinterConsumable.CompatiblePrintersText), "Naziv pojedinog printera smije imati najviše 200 znakova.");

        var databasePrinters = (await GetPrinterNameOptionsAsync()).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var unknownPrinters = printers.Where(x => !databasePrinters.Contains(x)).ToList();
        if (unknownPrinters.Count > 0)
        {
            ModelState.AddModelError(
                nameof(PrinterConsumable.CompatiblePrintersText),
                "Kompatibilni printeri moraju se odabrati iz postojećih printera u bazi.");
        }
    }

    private static List<string> ParsePrinterNames(string? value) =>
        (value ?? string.Empty)
            .Split(new[] { '\r', '\n', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizePrinterOption)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private bool IsAjaxRequest() =>
        string.Equals(Request.Headers["X-Requested-With"].ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    private IActionResult ReceiveError(string message, string? returnUrl, int statusCode = StatusCodes.Status400BadRequest)
    {
        if (IsAjaxRequest())
            return StatusCode(statusCode, new { success = false, message });

        TempData["Error"] = message;
        return RedirectBack(returnUrl);
    }

    private async Task<object> BuildClientProductDataAsync(PrinterConsumable anchor)
    {
        var candidates = await _context.PrinterConsumables
            .AsNoTracking()
            .Include(x => x.CompatiblePrinters)
            .Include(x => x.PendingOrders)
            .Where(x => x.Type == anchor.Type && x.IsOriginal == anchor.IsOriginal)
            .OrderBy(x => x.Id)
            .ToListAsync();

        var familyKey = GetFamilyKey(anchor);
        var family = candidates.Where(x => GetFamilyKey(x) == familyKey).ToList();
        var display = BuildFamilyDisplayItem(family);
        var states = display.ColorStates.Select(state => new
        {
            colorValue = state.Color.ToString(),
            label = PrinterConsumable.GetColorDisplayName(state.Color),
            available = state.QuantityAvailable,
            ordered = state.QuantityOrdered,
            status = StateStatusText(state.QuantityAvailable, state.QuantityOrdered),
            statusClass = StateStatusClass(state.QuantityAvailable, state.QuantityOrdered)
        }).ToArray();
        var pendingOrders = display.PendingOrders
            .Where(order => order.RemainingQuantity > 0)
            .GroupBy(order => order.OrderGroupId?.ToString() ?? $"legacy-{order.Id}")
            .OrderBy(group => group.Min(order => order.OrderedAt))
            .Select(group => new
            {
                id = group.First().Id,
                quantity = group.Sum(order => order.RemainingQuantity),
                orderedAt = group.Min(order => order.OrderedAt).ToString("dd.MM.yyyy. HH:mm"),
                items = group
                    .OrderBy(order => order.Color)
                    .Select(order => new
                    {
                        color = PrinterConsumable.GetColorDisplayName(order.Color),
                        quantity = order.RemainingQuantity
                    })
                    .ToArray()
            }).ToArray();

        return new
        {
            id = display.Id,
            name = display.Name,
            code = display.ProductCode,
            type = GetEnumDisplayName(display.Type),
            typeValue = display.Type.ToString(),
            usesColors = display.UsesStandardColors,
            isOriginal = display.IsOriginal,
            imageUrl = display.ImageUrl,
            available = display.QuantityAvailable,
            ordered = display.QuantityOrdered,
            printers = display.CompatiblePrinters
                .Select(x => x.PrinterName)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(x => x)
                .ToArray(),
            states,
            pendingOrders
        };
    }

    private async Task<object> BuildClientSummaryAsync()
    {
        var rawItems = await _context.PrinterConsumables.AsNoTracking().ToListAsync();
        var families = BuildFamilyDisplayItems(rawItems);
        return new
        {
            totalCount = families.Count,
            availableCount = families.Count(x => x.QuantityAvailable > 0),
            orderedCount = families.Count(x => x.QuantityOrdered > 0),
            missingCount = families.Count(x => x.QuantityAvailable == 0 && x.QuantityOrdered == 0),
            totalAvailableUnits = families.Sum(x => x.QuantityAvailable),
            totalOrderedUnits = families.Sum(x => x.QuantityOrdered)
        };
    }

    private static string StateStatusText(int available, int ordered) => available > 0
        ? ordered > 0 ? $"Dostupno · dolazi još {ordered}" : "Dostupno"
        : ordered > 0 ? "Naručeno" : "Nema";

    private static string StateStatusClass(int available, int ordered) => available > 0
        ? "status-success"
        : ordered > 0 ? "status-warning" : "status-danger";

    private async Task<List<PrinterConsumable>> GetFamilyItemsAsync(
        PrinterConsumable anchor,
        bool includePrinters = false,
        bool includePendingOrders = false)
    {
        IQueryable<PrinterConsumable> query = _context.PrinterConsumables
            .Where(x => x.Type == anchor.Type && x.IsOriginal == anchor.IsOriginal);

        if (includePrinters)
            query = query.Include(x => x.CompatiblePrinters);
        if (includePendingOrders)
            query = query.Include(x => x.PendingOrders);

        var candidates = await query.OrderBy(x => x.Id).ToListAsync();
        var familyKey = GetFamilyKey(anchor);
        return candidates.Where(x => GetFamilyKey(x) == familyKey).ToList();
    }

    private List<PrinterConsumable> BuildFamilyDisplayItems(IEnumerable<PrinterConsumable> items) =>
        items
            .GroupBy(GetFamilyKey)
            .Select(group => BuildFamilyDisplayItem(group.ToList()))
            .ToList();

    private PrinterConsumable BuildFamilyDisplayItem(IReadOnlyCollection<PrinterConsumable> family)
    {
        var canonical = GetCanonicalFamilyItem(family);
        var states = AggregateFamilyStates(family);
        var display = new PrinterConsumable
        {
            Id = canonical.Id,
            RowVersion = canonical.RowVersion,
            Name = GetDisplayFamilyName(canonical),
            ProductCode = canonical.ProductCode,
            Type = canonical.Type,
            Color = ConsumableColor.NijePrimjenjivo,
            IsOriginal = canonical.IsOriginal,
            CreatedAt = family.Min(x => x.CreatedAt),
            UpdatedAt = family.Max(x => x.UpdatedAt),
            ImageUrl = family
                .OrderBy(x => x.Id == canonical.Id ? 0 : 1)
                .Select(x => ResolveConsumableImageUrl(x.Id))
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
            CompatiblePrinters = family
                .SelectMany(x => x.CompatiblePrinters)
                .Where(x => !string.IsNullOrWhiteSpace(x.PrinterName))
                .GroupBy(x => NormalizePrinterOption(x.PrinterName), StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new ConsumableCompatiblePrinter
                {
                    PrinterConsumableId = canonical.Id,
                    PrinterName = group.Key
                })
                .OrderBy(x => x.PrinterName)
                .ToList(),
            Transactions = family.SelectMany(x => x.Transactions).OrderByDescending(x => x.CreatedAt).ToList(),
            PendingOrders = family.SelectMany(x => x.PendingOrders).OrderBy(x => x.OrderedAt).ToList()
        };
        display.SetColorStates(states);
        return display;
    }

    private static PrinterConsumable GetCanonicalFamilyItem(IEnumerable<PrinterConsumable> family) =>
        family
            .OrderByDescending(x => !string.IsNullOrWhiteSpace(x.ColorStateJson))
            .ThenBy(x => x.Id)
            .First();

    private static string GetFamilyKey(PrinterConsumable item)
    {
        var normalizedName = item.Type is ConsumableType.Toner or ConsumableType.Tinta
            ? NormalizeLegacyFamilyName(item.Name)
            : Regex.Replace(item.Name.Trim(), @"\s+", " ").ToLowerInvariant();
        // Toner i tinta predstavljaju jednu obitelj proizvoda; stare boje mogu imati različite šifre.
        // Šifra ostaje važna za proizvode bez CMYK varijanti.
        var normalizedCode = item.Type is ConsumableType.Toner or ConsumableType.Tinta
            ? string.Empty
            : string.IsNullOrWhiteSpace(item.ProductCode)
                ? string.Empty
                : item.ProductCode.Trim().ToLowerInvariant();

        return string.Join("\u001F", new[]
        {
            normalizedName,
            normalizedCode,
            item.Type.ToString(),
            item.IsOriginal ? "1" : "0"
        });
    }

    private static string GetDisplayFamilyName(PrinterConsumable item) =>
        item.Type is ConsumableType.Toner or ConsumableType.Tinta
            ? StripLegacyColorSuffix(item.Name)
            : Regex.Replace(item.Name.Trim(), @"\s+", " ");

    private static string NormalizeLegacyFamilyName(string value) =>
        StripLegacyColorSuffix(value).ToLowerInvariant();

    private static string StripLegacyColorSuffix(string value)
    {
        var normalized = Regex.Replace(value.Trim(), @"\s+", " ");
        normalized = Regex.Replace(
            normalized,
            @"(?:\s*[-–—/|:]?\s*)(cyan|magenta|yellow|black|key|crna|žuta|zuta)\s*$",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return normalized.Trim();
    }

    private static List<ConsumableColorState> AggregateFamilyStates(IEnumerable<PrinterConsumable> family)
    {
        var rows = family.ToList();
        var usesColors = rows.Any(x => x.Type is ConsumableType.Toner or ConsumableType.Tinta);

        if (!usesColors)
        {
            return
            [
                new ConsumableColorState(
                    ConsumableColor.NijePrimjenjivo,
                    rows.Sum(x => x.GetColorStates().Sum(state => state.QuantityAvailable)),
                    rows.Sum(x => x.GetColorStates().Sum(state => state.QuantityOrdered)))
            ];
        }

        var result = CreateEmptyStandardColorStates().ToDictionary(x => x.Color);
        foreach (var state in rows.SelectMany(x => x.GetColorStates()))
        {
            var color = state.Color is ConsumableColor.Cyan or ConsumableColor.Magenta or ConsumableColor.Zuta or ConsumableColor.Crna
                ? state.Color
                : ConsumableColor.Crna;
            var current = result[color];
            result[color] = current with
            {
                QuantityAvailable = current.QuantityAvailable + state.QuantityAvailable,
                QuantityOrdered = current.QuantityOrdered + state.QuantityOrdered
            };
        }

        return result.Values.OrderBy(state => ColorSortOrder(state.Color)).ToList();
    }

    private static IReadOnlyList<ConsumableColorState> NormalizeStatesForType(
        IEnumerable<ConsumableColorState> states,
        ConsumableType type)
    {
        var source = states.ToList();
        if (type is not ConsumableType.Toner and not ConsumableType.Tinta)
        {
            return
            [
                new ConsumableColorState(
                    ConsumableColor.NijePrimjenjivo,
                    source.Sum(x => x.QuantityAvailable),
                    source.Sum(x => x.QuantityOrdered))
            ];
        }

        var result = CreateEmptyStandardColorStates().ToDictionary(x => x.Color);
        foreach (var state in source)
        {
            var color = state.Color is ConsumableColor.Cyan or ConsumableColor.Magenta or ConsumableColor.Zuta or ConsumableColor.Crna
                ? state.Color
                : ConsumableColor.Crna;
            var current = result[color];
            result[color] = current with
            {
                QuantityAvailable = current.QuantityAvailable + state.QuantityAvailable,
                QuantityOrdered = current.QuantityOrdered + state.QuantityOrdered
            };
        }

        return result.Values.OrderBy(state => ColorSortOrder(state.Color)).ToList();
    }

    private static List<ConsumableColorState> CreateEmptyStandardColorStates() =>
    [
        new(ConsumableColor.Cyan, 0, 0),
        new(ConsumableColor.Magenta, 0, 0),
        new(ConsumableColor.Zuta, 0, 0),
        new(ConsumableColor.Crna, 0, 0)
    ];

    private static int ColorSortOrder(ConsumableColor color) => color switch
    {
        ConsumableColor.Cyan => 1,
        ConsumableColor.Magenta => 2,
        ConsumableColor.Zuta => 3,
        ConsumableColor.Crna => 4,
        _ => 5
    };

    private static Dictionary<ConsumableColor, int> BuildRequestedQuantities(
        ConsumableType type,
        int cyanQuantity,
        int magentaQuantity,
        int yellowQuantity,
        int blackQuantity,
        int totalQuantity)
    {
        if (type is ConsumableType.Toner or ConsumableType.Tinta)
        {
            return new Dictionary<ConsumableColor, int>
            {
                [ConsumableColor.Cyan] = cyanQuantity,
                [ConsumableColor.Magenta] = magentaQuantity,
                [ConsumableColor.Zuta] = yellowQuantity,
                [ConsumableColor.Crna] = blackQuantity
            };
        }

        return new Dictionary<ConsumableColor, int>
        {
            [ConsumableColor.NijePrimjenjivo] = totalQuantity
        };
    }

    private bool ValidateModalQuantities(
        IReadOnlyDictionary<ConsumableColor, int> quantities,
        string operationName,
        string? returnUrl,
        out IActionResult? result)
    {
        if (quantities.Values.Any(x => x < 0 || x > MaximumModalQuantity))
        {
            TempData["Error"] = $"Količine za {operationName} moraju biti između 0 i {MaximumModalQuantity}.";
            result = RedirectBack(returnUrl);
            return false;
        }

        if (quantities.Values.All(x => x == 0))
        {
            TempData["Error"] = $"Odaberi barem jednu količinu za {operationName}.";
            result = RedirectBack(returnUrl);
            return false;
        }

        result = null;
        return true;
    }

    private static bool TryApplyStockChange(
        IReadOnlyCollection<PrinterConsumable> family,
        PrinterConsumable canonical,
        IReadOnlyDictionary<ConsumableColor, int> quantities,
        StockChangeType changeType,
        out string message)
    {
        message = string.Empty;
        var states = AggregateFamilyStates(family).ToList();

        foreach (var pair in quantities.Where(x => x.Value > 0))
        {
            var index = states.FindIndex(x => x.Color == pair.Key);
            if (index < 0)
            {
                message = $"Nije pronađeno stanje za {PrinterConsumable.GetColorDisplayName(pair.Key)}.";
                return false;
            }

            var state = states[index];
            switch (changeType)
            {
                case StockChangeType.Order:
                    state = state with { QuantityOrdered = state.QuantityOrdered + pair.Value };
                    break;

                case StockChangeType.Receive:
                    if (state.QuantityOrdered < pair.Value)
                    {
                        message = $"Za {PrinterConsumable.GetColorDisplayName(pair.Key)} nema dovoljno naručene količine za zaprimanje.";
                        return false;
                    }
                    state = state with
                    {
                        QuantityAvailable = state.QuantityAvailable + pair.Value,
                        QuantityOrdered = state.QuantityOrdered - pair.Value
                    };
                    break;

                case StockChangeType.Use:
                    if (state.QuantityAvailable < pair.Value)
                    {
                        message = $"Za {PrinterConsumable.GetColorDisplayName(pair.Key)} dostupno je samo {state.QuantityAvailable} kom.";
                        return false;
                    }
                    state = state with { QuantityAvailable = state.QuantityAvailable - pair.Value };
                    break;
            }

            states[index] = state;
        }

        canonical.Name = GetDisplayFamilyName(canonical);
        canonical.SetColorStates(states);
        canonical.UpdatedAt = DateTime.Now;

        foreach (var legacyVariant in family.Where(x => x.Id != canonical.Id))
        {
            legacyVariant.Color = ConsumableColor.NijePrimjenjivo;
            legacyVariant.ColorStateJson = null;
            legacyVariant.QuantityAvailable = 0;
            legacyVariant.QuantityOrdered = 0;
            legacyVariant.UpdatedAt = canonical.UpdatedAt;
        }

        return true;
    }

    private void AddPendingOrder(
        PrinterConsumable item,
        ConsumableColor color,
        int quantity,
        Guid? orderGroupId = null,
        DateTime? orderedAt = null)
    {
        _context.ConsumablePendingOrders.Add(new ConsumablePendingOrder
        {
            OrderGroupId = orderGroupId ?? Guid.NewGuid(),
            PrinterConsumableId = item.Id,
            Color = color,
            QuantityOrdered = quantity,
            QuantityReceived = 0,
            OrderedAt = orderedAt ?? DateTime.Now,
            OrderedBy = User.Identity?.Name
        });
    }

    private async Task EnsurePendingOrdersBackfilledAsync()
    {
        var items = await _context.PrinterConsumables
            .Include(x => x.Transactions)
            .Include(x => x.PendingOrders)
            .ToListAsync();

        var changed = false;
        foreach (var family in items.GroupBy(GetFamilyKey).Select(x => x.ToList()))
        {
            var canonical = GetCanonicalFamilyItem(family);
            var states = AggregateFamilyStates(family);
            var activeOrders = family
                .SelectMany(x => x.PendingOrders)
                .Where(x => x.CompletedAt == null && x.RemainingQuantity > 0)
                .ToList();

            ConsumablePendingOrder? previousLegacyOrder = null;
            Guid? legacyGroupId = null;
            foreach (var legacyOrder in activeOrders
                         .Where(x => !x.OrderGroupId.HasValue)
                         .OrderBy(x => x.OrderedAt)
                         .ThenBy(x => x.Id))
            {
                var belongsToPreviousSubmission = previousLegacyOrder != null
                    && string.Equals(previousLegacyOrder.OrderedBy, legacyOrder.OrderedBy, StringComparison.Ordinal)
                    && legacyOrder.OrderedAt - previousLegacyOrder.OrderedAt <= TimeSpan.FromSeconds(2);

                if (!belongsToPreviousSubmission)
                    legacyGroupId = Guid.NewGuid();

                legacyOrder.OrderGroupId = legacyGroupId;
                previousLegacyOrder = legacyOrder;
                changed = true;
            }

            foreach (var state in states.Where(x => x.QuantityOrdered > 0))
            {
                var alreadyTracked = activeOrders
                    .Where(x => x.Color == state.Color)
                    .Sum(x => x.RemainingQuantity);
                var missing = state.QuantityOrdered - alreadyTracked;
                if (missing <= 0)
                    continue;

                var orderedAt = family
                    .SelectMany(x => x.Transactions)
                    .Where(x => x.TransactionType == ConsumableTransactionType.Naruceno
                        && (x.Color == state.Color || state.Color == ConsumableColor.NijePrimjenjivo))
                    .OrderByDescending(x => x.CreatedAt)
                    .Select(x => (DateTime?)x.CreatedAt)
                    .FirstOrDefault()
                    ?? canonical.UpdatedAt
                    ?? canonical.CreatedAt;

                _context.ConsumablePendingOrders.Add(new ConsumablePendingOrder
                {
                    OrderGroupId = Guid.NewGuid(),
                    PrinterConsumableId = canonical.Id,
                    Color = state.Color,
                    QuantityOrdered = missing,
                    QuantityReceived = 0,
                    OrderedAt = orderedAt,
                    OrderedBy = "Prijenos postojećeg stanja"
                });
                changed = true;
            }
        }

        if (changed)
            await _context.SaveChangesAsync();
    }

    private static bool TryParseColor(string? value, out ConsumableColor color)
    {
        if (string.Equals(value, "Black", StringComparison.OrdinalIgnoreCase))
        {
            color = ConsumableColor.Crna;
            return true;
        }

        if (string.Equals(value, "Yellow", StringComparison.OrdinalIgnoreCase))
        {
            color = ConsumableColor.Zuta;
            return true;
        }

        color = ConsumableColor.NijePrimjenjivo;
        return Enum.TryParse(value, true, out color)
            && color is ConsumableColor.Crna
                or ConsumableColor.Cyan
                or ConsumableColor.Magenta
                or ConsumableColor.Zuta
                or ConsumableColor.NijePrimjenjivo;
    }

    private enum StockChangeType
    {
        Order,
        Receive,
        Use
    }

    private static string NormalizePrinterOption(string? value) =>
        string.Join(" ", (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static void NormalizeModel(PrinterConsumableFormViewModel model)
    {
        model.Name = model.Name?.Trim() ?? string.Empty;
        if (model.Type is ConsumableType.Toner or ConsumableType.Tinta)
            model.Name = StripLegacyColorSuffix(model.Name);

        model.ProductCode = string.IsNullOrWhiteSpace(model.ProductCode) ? null : model.ProductCode.Trim();
        model.CompatiblePrintersText = model.CompatiblePrintersText?.Trim() ?? string.Empty;
    }

    private async Task SaveConsumableImageAsync(PrinterConsumable model, IFormFile? imageOverride = null)
    {
        var image = imageOverride ?? model.ProductImage;
        if (image is not { Length: > 0 })
            return;

        var extension = await DetectImageExtensionAsync(image);
        if (extension == null)
            return;

        var imageDirectory = GetConsumableImageDirectory();
        Directory.CreateDirectory(imageDirectory);

        foreach (var allowedExtension in AllowedImageExtensions)
        {
            var existing = Path.Combine(imageDirectory, $"consumable-{model.Id}{allowedExtension}");
            if (System.IO.File.Exists(existing))
                System.IO.File.Delete(existing);
        }

        var fileName = $"consumable-{model.Id}{extension}";
        var fullPath = Path.Combine(imageDirectory, fileName);
        await using var stream = System.IO.File.Create(fullPath);
        await image.CopyToAsync(stream);
        model.ImageUrl = $"/consumable-images/{fileName}";
    }

    private void ValidateProductImage(PrinterConsumableFormViewModel model)
    {
        if (model.ProductImage is not { Length: > 0 })
            return;

        if (model.ProductImage.Length > 2 * 1024 * 1024)
            ModelState.AddModelError(nameof(PrinterConsumableFormViewModel.ProductImage), "Slika smije imati najviše 2 MB.");

        if (!AllowedImageExtensions.Contains(Path.GetExtension(model.ProductImage.FileName), StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(PrinterConsumableFormViewModel.ProductImage), "Dopuštene su JPG, PNG i WEBP slike.");
            return;
        }

        if (!ModelState.IsValid)
            return;

        // Detekcija se radi i po zaglavlju, ali ova brza provjera hvata očite pogreške prije spremanja.
    }

    private string? ResolveConsumableImageUrl(int id)
    {
        var imageDirectory = GetConsumableImageDirectory();
        foreach (var extension in AllowedImageExtensions)
        {
            var fileName = $"consumable-{id}{extension}";
            if (System.IO.File.Exists(Path.Combine(imageDirectory, fileName)))
                return $"/consumable-images/{fileName}";
        }

        return null;
    }

    private string GetConsumableImageDirectory() =>
        _configuration["ResolvedConsumableImagesPath"]
        ?? Path.Combine(_environment.ContentRootPath, "data", "consumable-images");

    private static async Task<string?> DetectImageExtensionAsync(IFormFile image)
    {
        if (image.Length < 12)
            return null;

        var header = new byte[12];
        await using var stream = image.OpenReadStream();
        var read = await stream.ReadAsync(header.AsMemory(0, header.Length));
        if (read < header.Length)
            return null;

        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return ".jpg";

        if (header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return ".png";

        if (Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && Encoding.ASCII.GetString(header, 8, 4) == "WEBP")
            return ".webp";

        return null;
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
            item.ProductKindText, item.ColorStatesSummary, GetAvailabilityText(item)
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
            sheet.Cells[row, 1].Value = ExcelExportHelper.SafeCellValue(g.Printer);
            sheet.Cells[row, 2].Value = ExcelExportHelper.SafeCellValue(g.Site);
            sheet.Cells[row, 3].Value = ExcelExportHelper.SafeCellValue(g.ConsumableName);
            sheet.Cells[row, 4].Value = ExcelExportHelper.SafeCellValue(g.ProductCode);
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
            sheet.Cells[orderRow, 7].Value = ExcelExportHelper.SafeCellValue(g.ConsumableName);
            sheet.Cells[orderRow, 8].Value = ExcelExportHelper.SafeCellValue(g.ProductCode);
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
            details.Cells[row, 3].Value = ExcelExportHelper.SafeCellValue(x.ConsumableName);
            details.Cells[row, 4].Value = ExcelExportHelper.SafeCellValue(x.ProductCode);
            details.Cells[row, 5].Value = GetEnumDisplayName(x.ConsumableType);
            details.Cells[row, 6].Value = GetEnumDisplayName(x.Color);
            details.Cells[row, 7].Value = x.Quantity;
            details.Cells[row, 8].Value = ExcelExportHelper.SafeCellValue(x.PrinterName);
            details.Cells[row, 9].Value = ExcelExportHelper.SafeCellValue(x.SiteName);
            details.Cells[row, 10].Value = ExcelExportHelper.SafeCellValue(x.PerformedBy);
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
            sheet.Cells[row, 1].Value = ExcelExportHelper.SafeCellValue(g.PrinterName);
            sheet.Cells[row, 2].Value = ExcelExportHelper.SafeCellValue(g.SiteName);
            sheet.Cells[row, 3].Value = ExcelExportHelper.SafeCellValue(g.ConsumableName);
            sheet.Cells[row, 4].Value = ExcelExportHelper.SafeCellValue(g.ProductCode);
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
            sheet.Cells[row, 1].Value = ExcelExportHelper.SafeCellValue(g.ConsumableName);
            sheet.Cells[row, 2].Value = ExcelExportHelper.SafeCellValue(g.ProductCode);
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
