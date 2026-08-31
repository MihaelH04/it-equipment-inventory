using ITEquipmentInventory.Data;
using ITEquipmentInventory.Models;
using ITEquipmentInventory.Models.ViewModels;
using ITEquipmentInventory.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace ITEquipmentInventory.Controllers;

[Authorize]
public class EquipmentReturnController : Controller
{
    private readonly AppDbContext _context;
    private readonly RecycleBinService _recycleBin;

    public EquipmentReturnController(AppDbContext context, RecycleBinService recycleBin)
    {
        _context = context;
        _recycleBin = recycleBin;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string searchString, string sortOrder, int page = 1)
    {
        var result = await GetFilteredReturnsAsync(searchString, sortOrder, page);
        await LoadStatsAsync();
        SetSortViewBags(searchString, sortOrder);
        SetPaginationViewBags(result);
        return View(result.Items);
    }

    [HttpGet]
    public async Task<IActionResult> IndexTable(string searchString, string sortOrder, int page = 1)
    {
        var result = await GetFilteredReturnsAsync(searchString, sortOrder, page);
        SetSortViewBags(searchString, sortOrder);
        SetPaginationViewBags(result);
        return PartialView("_EquipmentReturnTable", result.Items);
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel(string searchString, string sortOrder)
    {
        var items = await GetFilteredReturnsAsync(searchString, sortOrder, null);

        var bytes = ExcelExportHelper.CreateExcel(
            "Razduženja",
            new[]
            {
                "Inventurni broj", "Serijski broj", "Vrsta", "Naziv", "Radni nalog",
                "Zaposlenik", "Datum zaduženja", "Datum razduženja", "Tko je zadužio", "Razdužio", "Napomena"
            },
            items.Items.Select(x => new object?[]
            {
                x.InventoryNumber,
                x.SerialNumber,
                x.EquipmentType.ToString(),
                x.Name,
                string.IsNullOrWhiteSpace(x.PreviousSiteCode) ? x.PreviousSiteName : x.PreviousSiteCode + " - " + x.PreviousSiteName,
                string.IsNullOrWhiteSpace(x.PreviousEmployeeCode) ? x.PreviousEmployeeName : x.PreviousEmployeeCode + " - " + x.PreviousEmployeeName,
                x.AssignedAt?.ToString("dd.MM.yyyy"),
                x.ReturnedAt.ToString("dd.MM.yyyy"),
                x.PreviousHandedOverBy,
                x.HandedOverBy,
                x.Note
            }));

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ExcelExportHelper.FileName("Razduzenja"));
    }

    [HttpGet]
    public async Task<IActionResult> SearchReturnSuggestions(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return Json(new List<object>());

        var tokens = TokenizeSearch(term);

        var returnList = await _context.EquipmentReturns
            .AsNoTracking()
            .OrderByDescending(x => x.ReturnedAt)
            .Take(PaginationConstants.MaxSearchSuggestionResults)
            .ToListAsync();

        var suggestions = new List<(string Text, string Value)>();

        void AddSuggestionPair(string? text, string? value = null)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var cleanedText = string.Join(" ", text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrWhiteSpace(cleanedText))
                return;

            var cleanedValue = string.IsNullOrWhiteSpace(value)
                ? cleanedText
                : string.Join(" ", value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

            suggestions.Add((cleanedText, cleanedValue));
        }

        void AddMatchingSuggestionPair(string? text, string? value = null)
        {
            if (SuggestionFieldMatchesTokens(text, tokens))
                AddSuggestionPair(text, value);
        }

        foreach (var item in returnList.Where(x => ReturnMatchesTokens(x, tokens)).Take(40))
        {
            AddMatchingSuggestionPair(item.InventoryNumber, item.InventoryNumber);
            AddMatchingSuggestionPair(item.SerialNumber, item.SerialNumber);
            AddMatchingSuggestionPair(item.Name, item.Name);
            AddMatchingSuggestionPair(item.PreviousEmployeeName, item.PreviousEmployeeName);
            AddMatchingSuggestionPair(item.PreviousEmployeeCode, item.PreviousEmployeeCode);
            AddMatchingSuggestionPair(item.PreviousSiteName, item.PreviousSiteName);
            AddMatchingSuggestionPair(item.PreviousSiteCode, item.PreviousSiteCode);
            AddMatchingSuggestionPair(item.HandedOverBy, item.HandedOverBy);

            if (SuggestionFieldMatchesTokens(item.EquipmentType.ToString(), tokens))
                AddSuggestionPair(item.EquipmentType.ToString(), item.EquipmentType.ToString());

            var combinedText = string.Join(" | ", new[]
            {
                item.InventoryNumber,
                item.SerialNumber,
                item.EquipmentType.ToString(),
                item.Name,
                string.IsNullOrWhiteSpace(item.PreviousEmployeeCode) ? item.PreviousEmployeeName : item.PreviousEmployeeCode + " - " + item.PreviousEmployeeName,
                string.IsNullOrWhiteSpace(item.PreviousSiteCode) ? item.PreviousSiteName : item.PreviousSiteCode + " - " + item.PreviousSiteName,
                item.ReturnedAt.ToString("dd.MM.yyyy")
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

            var combinedValue = !string.IsNullOrWhiteSpace(item.InventoryNumber)
                ? item.InventoryNumber
                : !string.IsNullOrWhiteSpace(item.SerialNumber)
                    ? item.SerialNumber
                    : item.Name;

            AddSuggestionPair(combinedText, combinedValue);
        }

        var results = suggestions
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .GroupBy(x => x.Text, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => g.First())
            .Take(15)
            .Select(x => new { text = x.Text, value = x.Value })
            .ToList();

        return Json(results);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Restore(int id)
    {
        var item = await _context.EquipmentReturns.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null)
        {
            TempData["Error"] = "Zapis razduženja nije pronađen.";
            return RedirectToAction(nameof(Index));
        }

        var equipment = await _context.Equipment.FirstOrDefaultAsync(x => x.Id == item.EquipmentId);
        if (equipment == null)
        {
            TempData["Error"] = "Povezana oprema nije pronađena.";
            return RedirectToAction(nameof(Index));
        }

        equipment.Status = item.PreviousStatus;
        equipment.CurrentSiteId = item.PreviousSiteId;
        equipment.CurrentEmployeeId = item.PreviousEmployeeId;
        equipment.AssignedAt = item.AssignedAt;
        equipment.ReturnedAt = null;
        equipment.HandedOverBy = item.PreviousHandedOverBy;

        _context.EquipmentReturns.Remove(item);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Razduženje je uspješno vraćeno u tablicu opreme.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit(int id)
    {
        var item = await _context.EquipmentReturns.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null)
        {
            TempData["Error"] = "Zapis razduženja više ne postoji ili je već obrisan.";
            return RedirectToAction(nameof(Index));
        }

        return View(new EquipmentReturnEditViewModel
        {
            Id = item.Id,
            InventoryNumber = item.InventoryNumber,
            SerialNumber = item.SerialNumber,
            EquipmentType = item.EquipmentType,
            Name = item.Name,
            PreviousSiteCode = item.PreviousSiteCode,
            PreviousSiteName = item.PreviousSiteName,
            PreviousEmployeeCode = item.PreviousEmployeeCode,
            PreviousEmployeeName = item.PreviousEmployeeName,
            AssignedAt = item.AssignedAt,
            ReturnedAt = item.ReturnedAt,
            HandedOverBy = item.HandedOverBy,
            Note = item.Note
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit(int id, EquipmentReturnEditViewModel model)
    {
        if (id != model.Id)
            return NotFound();

        if (!ModelState.IsValid)
            return View(model);

        var existing = await _context.EquipmentReturns.FirstOrDefaultAsync(x => x.Id == id);
        if (existing == null)
        {
            TempData["Error"] = "Zapis razduženja više ne postoji ili je već obrisan.";
            return RedirectToAction(nameof(Index));
        }

        existing.InventoryNumber = model.InventoryNumber;
        existing.SerialNumber = model.SerialNumber;
        existing.Name = model.Name;
        existing.EquipmentType = model.EquipmentType;
        existing.PreviousSiteCode = model.PreviousSiteCode;
        existing.PreviousSiteName = model.PreviousSiteName;
        existing.PreviousEmployeeCode = model.PreviousEmployeeCode;
        existing.PreviousEmployeeName = model.PreviousEmployeeName;
        existing.AssignedAt = model.AssignedAt;
        existing.ReturnedAt = model.ReturnedAt;
        existing.HandedOverBy = model.HandedOverBy;
        existing.Note = model.Note;

        await _context.SaveChangesAsync();

        TempData["Success"] = "Zapis razduženja je spremljen.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _context.EquipmentReturns.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null)
        {
            TempData["Error"] = "Zapis razduženja više ne postoji ili je već obrisan.";
            return RedirectToAction(nameof(Index));
        }

        return View(item);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var item = await _context.EquipmentReturns.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null)
        {
            TempData["Error"] = "Zapis razduženja više ne postoji ili je već obrisan.";
            return RedirectToAction(nameof(Index));
        }

        _recycleBin.MoveEquipmentReturn(item);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Zapis razduženja je premješten u Nedavno obrisano.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<PagedResult<EquipmentReturn>> GetFilteredReturnsAsync(string searchString, string sortOrder, int? page)
    {
        var query = _context.EquipmentReturns.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchString))
        {
            foreach (var token in TokenizeSearch(searchString))
            {
                query = query.Where(x => (x.InventoryNumber ?? string.Empty).Contains(token) || (x.SerialNumber != null && x.SerialNumber.Contains(token)) ||
                    (x.Name ?? string.Empty).Contains(token) || (x.PreviousSiteCode != null && x.PreviousSiteCode.Contains(token)) ||
                    (x.PreviousSiteName != null && x.PreviousSiteName.Contains(token)) ||
                    (x.PreviousEmployeeCode != null && x.PreviousEmployeeCode.Contains(token)) ||
                    (x.PreviousEmployeeName != null && x.PreviousEmployeeName.Contains(token)) ||
                    (x.HandedOverBy != null && x.HandedOverBy.Contains(token)) || (x.Note != null && x.Note.Contains(token)));
            }
        }

        query = sortOrder switch
        {
            "inventory_desc" => query.OrderByDescending(x => x.InventoryNumber),
            "inventory_asc" => query.OrderBy(x => x.InventoryNumber),
            "serial_desc" => query.OrderByDescending(x => x.SerialNumber),
            "serial_asc" => query.OrderBy(x => x.SerialNumber),
            "type_desc" => query.OrderByDescending(x => x.EquipmentType),
            "type_asc" => query.OrderBy(x => x.EquipmentType),
            "name_desc" => query.OrderByDescending(x => x.Name),
            "name_asc" => query.OrderBy(x => x.Name),
            "site_desc" => query.OrderByDescending(x => x.PreviousSiteName),
            "site_asc" => query.OrderBy(x => x.PreviousSiteName),
            "employee_desc" => query.OrderByDescending(x => x.PreviousEmployeeName),
            "employee_asc" => query.OrderBy(x => x.PreviousEmployeeName),
            "assignedat_desc" => query.OrderByDescending(x => x.AssignedAt),
            "assignedat_asc" => query.OrderBy(x => x.AssignedAt),
            "returnedat_asc" => query.OrderBy(x => x.ReturnedAt),
            _ => query.OrderByDescending(x => x.ReturnedAt)
        };

        var totalCount = await query.CountAsync();
        if (page is null)
            return new PagedResult<EquipmentReturn> { Items = await query.ToListAsync(), CurrentPage = 1, TotalPages = 1, TotalCount = totalCount };
        var currentPage = Math.Max(1, page.Value);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PaginationConstants.DefaultPageSize));
        currentPage = Math.Min(currentPage, totalPages);
        var items = await query.Skip((currentPage - 1) * PaginationConstants.DefaultPageSize).Take(PaginationConstants.DefaultPageSize).ToListAsync();
        return new PagedResult<EquipmentReturn> { Items = items, CurrentPage = currentPage, TotalPages = totalPages, TotalCount = totalCount };
    }

    private void SetPaginationViewBags<T>(PagedResult<T> result)
    {
        ViewBag.CurrentPage = result.CurrentPage;
        ViewBag.PageSize = result.PageSize;
        ViewBag.TotalPages = result.TotalPages;
        ViewBag.FilteredCount = result.TotalCount;
    }

    private bool ReturnMatchesTokens(EquipmentReturn item, List<string> searchTokens)
    {
        if (searchTokens == null || searchTokens.Count == 0)
            return true;

        var fields = new List<string>();
        AddSearchVariants(fields, item.InventoryNumber);
        AddSearchVariants(fields, item.SerialNumber);
        AddSearchVariants(fields, item.Name);
        AddSearchVariants(fields, item.PreviousSiteCode);
        AddSearchVariants(fields, item.PreviousSiteName);
        AddSearchVariants(fields, item.PreviousSiteLocation);
        AddSearchVariants(fields, item.PreviousEmployeeCode);
        AddSearchVariants(fields, item.PreviousEmployeeName);
        AddSearchVariants(fields, item.PreviousHandedOverBy);
        AddSearchVariants(fields, item.HandedOverBy);
        AddSearchVariants(fields, item.Note);
        AddSearchVariants(fields, item.ReturnedAt.ToString("dd.MM.yyyy"));
        if (item.AssignedAt.HasValue)
            AddSearchVariants(fields, item.AssignedAt.Value.ToString("dd.MM.yyyy"));

        foreach (var token in searchTokens)
        {
            if (!fields.Any(f => f.Contains(token)))
                return false;
        }

        return true;
    }

    private void AddMatchingSuggestion(List<string> suggestions, string? value, List<string> tokens)
    {
        if (SuggestionFieldMatchesTokens(value, tokens))
            AddSuggestion(suggestions, value);
    }

    private void AddSuggestion(List<string> suggestions, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var cleaned = string.Join(" ", value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (!string.IsNullOrWhiteSpace(cleaned))
            suggestions.Add(cleaned);
    }

    private bool SuggestionFieldMatchesTokens(string? value, List<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(value) || tokens == null || tokens.Count == 0)
            return false;

        var variants = new List<string>();
        AddSearchVariants(variants, value);

        return tokens.All(token => variants.Any(v => v.Contains(token)));
    }

    private void SetSortViewBags(string searchString, string sortOrder)
    {
        ViewBag.SearchString = searchString;
        ViewBag.CurrentSort = sortOrder;
        ViewBag.InventorySort = sortOrder == "inventory_asc" ? "inventory_desc" : "inventory_asc";
        ViewBag.SerialSort = sortOrder == "serial_asc" ? "serial_desc" : "serial_asc";
        ViewBag.TypeSort = sortOrder == "type_asc" ? "type_desc" : "type_asc";
        ViewBag.NameSort = sortOrder == "name_asc" ? "name_desc" : "name_asc";
        ViewBag.SiteSort = sortOrder == "site_asc" ? "site_desc" : "site_asc";
        ViewBag.EmployeeSort = sortOrder == "employee_asc" ? "employee_desc" : "employee_asc";
        ViewBag.AssignedAtSort = sortOrder == "assignedat_asc" ? "assignedat_desc" : "assignedat_asc";
        ViewBag.ReturnedAtSort = sortOrder == "returnedat_asc" ? "returnedat_desc" : "returnedat_asc";
    }

    private async Task LoadStatsAsync()
    {
        var all = await _context.EquipmentReturns.ToListAsync();
        ViewBag.TotalCount = all.Count;
        ViewBag.TodayCount = all.Count(x => x.ReturnedAt.Date == DateTime.Today);
        ViewBag.WithEmployeeCount = all.Count(x => !string.IsNullOrWhiteSpace(x.PreviousEmployeeName));
        ViewBag.WithSiteCount = all.Count(x => !string.IsNullOrWhiteSpace(x.PreviousSiteName));
    }

    private void AddSearchVariants(List<string> fields, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var normalized = NormalizeSearch(value);
        var noDiacritics = RemoveDiacritics(normalized);

        if (!string.IsNullOrWhiteSpace(normalized))
            fields.Add(normalized);

        if (!string.IsNullOrWhiteSpace(noDiacritics))
            fields.Add(noDiacritics);

        foreach (var token in TokenizeSearch(value))
        {
            if (!string.IsNullOrWhiteSpace(token))
                fields.Add(token);
        }
    }

    private List<string> TokenizeSearch(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new List<string>();

        var normalized = RemoveDiacritics(NormalizeSearch(input));

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct()
            .ToList();
    }

    private string NormalizeSearch(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        return string.Join(" ",
            input.Trim()
                 .ToLower(new CultureInfo("hr-HR"))
                 .Replace("_", " ")
                 .Replace("-", " ")
                 .Replace(".", " ")
                 .Replace(",", " ")
                 .Replace("/", " ")
                 .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private string RemoveDiacritics(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (char c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }
}
