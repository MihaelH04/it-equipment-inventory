using ITEquipmentInventory.Data;
using ITEquipmentInventory.Models;
using ITEquipmentInventory.Models.ViewModels;
using ITEquipmentInventory.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace ITEquipmentInventory.Controllers;

[Authorize(Roles = "Admin")]
public class EmployeesController : Controller
{
    private readonly AppDbContext _context;
    private readonly RecycleBinService _recycleBin;

    public EmployeesController(AppDbContext context, RecycleBinService recycleBin)
    {
        _context = context;
        _recycleBin = recycleBin;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string searchString, string statusFilter, string siteFilter, string sortBy, string sortDir)
    {
        var employees = await GetFilteredEmployeesAsync(searchString, statusFilter, siteFilter, sortBy, sortDir);

        await LoadEmployeeStatsAsync();
        SetEmployeeListViewBags(searchString, statusFilter, siteFilter, sortBy, sortDir);

        return View(employees);
    }

    [HttpGet]
    public async Task<IActionResult> IndexTable(string searchString, string statusFilter, string siteFilter, string sortBy, string sortDir)
    {
        var employees = await GetFilteredEmployeesAsync(searchString, statusFilter, siteFilter, sortBy, sortDir);

        SetEmployeeListViewBags(searchString, statusFilter, siteFilter, sortBy, sortDir);

        return PartialView("_EmployeesTable", employees);
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel(string searchString, string statusFilter, string siteFilter, string sortBy, string sortDir)
    {
        var employees = await GetFilteredEmployeesAsync(searchString, statusFilter, siteFilter, sortBy, sortDir);

        var bytes = ExcelExportHelper.CreateExcel(
            "Zaposlenici",
            new[] { "Šifra radnika", "Ime i prezime", "Radni nalog", "Šifra radnog naloga", "Status" },
            employees.Select(e => new object?[]
            {
                e.WorkerCode,
                e.FullName,
                e.Site?.Name,
                e.Site?.Code,
                e.Status.ToString()
            }));

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ExcelExportHelper.FileName("Zaposlenici"));
    }

    [HttpGet]
    public async Task<IActionResult> SearchEmployeeSuggestions(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return Json(new List<object>());

        var tokens = TokenizeSearch(term);

        var employees = await _context.Employees
            .Include(e => e.Site)
            .AsNoTracking()
            .OrderBy(e => e.WorkerCode)
            .Take(2000)
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

        foreach (var employee in employees.Where(e => EmployeeMatchesUniversalSearch(e, tokens)).Take(40))
        {
            var siteText = employee.Site == null
                ? null
                : string.IsNullOrWhiteSpace(employee.Site.Code)
                    ? employee.Site.Name
                    : employee.Site.Code + " - " + employee.Site.Name;

            AddMatchingSuggestionPair(employee.WorkerCode, employee.WorkerCode);
            AddMatchingSuggestionPair(employee.FullName, employee.FullName);
            AddMatchingSuggestionPair(employee.Site?.Name, employee.Site?.Name);
            AddMatchingSuggestionPair(employee.Site?.Code, employee.Site?.Code);
            AddMatchingSuggestionPair(siteText, employee.Site?.Code ?? employee.Site?.Name);

            if (SuggestionFieldMatchesTokens(NormalizeEmployeeStatus(employee.Status), tokens))
                AddSuggestionPair(employee.Status.ToString(), employee.Status.ToString());

            var combinedText = string.Join(" | ", new[]
            {
                employee.WorkerCode,
                employee.FullName,
                siteText,
                employee.Status.ToString()
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

            AddSuggestionPair(combinedText, !string.IsNullOrWhiteSpace(employee.WorkerCode) ? employee.WorkerCode : employee.FullName);
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
    public IActionResult DeleteSelected(int[] selectedIds)
    {
        if (selectedIds == null || selectedIds.Length == 0)
        {
            TempData["Error"] = "Odaberi barem jednog zaposlenika za brisanje.";
            return RedirectToAction(nameof(Index));
        }

        TempData["SelectedEmployeeDeleteIds"] = string.Join(",", selectedIds);
        return RedirectToAction(nameof(DeleteSelectedConfirm));
    }

    [HttpGet]
    public async Task<IActionResult> DeleteSelectedConfirm()
    {
        var selectedIdsRaw = TempData["SelectedEmployeeDeleteIds"] as string;

        if (string.IsNullOrWhiteSpace(selectedIdsRaw))
        {
            TempData["Error"] = "Nema odabranih zaposlenika za brisanje.";
            return RedirectToAction(nameof(Index));
        }

        TempData.Keep("SelectedEmployeeDeleteIds");

        var selectedIds = selectedIdsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToList();

        var employees = await _context.Employees
            .Include(e => e.Site)
            .Where(e => selectedIds.Contains(e.Id))
            .OrderBy(e => e.WorkerCode)
            .ToListAsync();

        if (!employees.Any())
        {
            TempData["Error"] = "Odabrani zaposlenici nisu pronađeni.";
            return RedirectToAction(nameof(Index));
        }

        return View(employees);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSelectedConfirmed(int[] selectedIds)
    {
        if (selectedIds == null || selectedIds.Length == 0)
        {
            TempData["Error"] = "Nema odabranih zaposlenika za brisanje.";
            return RedirectToAction(nameof(Index));
        }

        var employees = await _context.Employees
            .Where(e => selectedIds.Contains(e.Id))
            .ToListAsync();

        if (!employees.Any())
        {
            TempData["Error"] = "Odabrani zaposlenici nisu pronađeni.";
            return RedirectToAction(nameof(Index));
        }

        foreach (var employee in employees)
        {
            var equipmentIds = await _context.Equipment
                .Where(x => x.CurrentEmployeeId == employee.Id)
                .Select(x => x.Id)
                .ToListAsync();
            _recycleBin.MoveEmployee(employee, equipmentIds);
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = $"U Nedavno obrisano premješteno zaposlenika: {employees.Count}";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult BulkEdit(int[] selectedIds)
    {
        if (selectedIds == null || selectedIds.Length == 0)
        {
            TempData["Error"] = "Odaberi barem jednog zaposlenika za grupno uređivanje.";
            return RedirectToAction(nameof(Index));
        }

        TempData["SelectedEmployeeIds"] = string.Join(",", selectedIds);
        return RedirectToAction(nameof(BulkEdit));
    }

    [HttpGet]
    public async Task<IActionResult> BulkEdit()
    {
        var selectedIdsRaw = TempData["SelectedEmployeeIds"] as string;

        if (string.IsNullOrWhiteSpace(selectedIdsRaw))
        {
            TempData["Error"] = "Nema odabranih zaposlenika za grupno uređivanje.";
            return RedirectToAction(nameof(Index));
        }

        var selectedIds = selectedIdsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToList();

        TempData.Keep("SelectedEmployeeIds");

        var vm = new EmployeeBulkEditViewModel
        {
            SelectedIds = selectedIds
        };

        await LoadBulkEditLookupDataAsync(vm);
        return View("BulkEdit", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkEditSave(EmployeeBulkEditViewModel model)
    {
        if (model.SelectedIds == null || model.SelectedIds.Count == 0)
        {
            TempData["Error"] = "Nema odabranih zaposlenika za spremanje.";
            return RedirectToAction(nameof(Index));
        }

        var employees = await _context.Employees
            .Where(e => model.SelectedIds.Contains(e.Id))
            .ToListAsync();

        if (!employees.Any())
        {
            TempData["Error"] = "Odabrani zaposlenici nisu pronađeni.";
            return RedirectToAction(nameof(Index));
        }

        foreach (var employee in employees)
        {
            if (model.Status.HasValue)
                employee.Status = model.Status.Value;

            if (model.SiteId.HasValue)
                employee.SiteId = model.SiteId.Value;
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = $"Ažurirano zaposlenika: {employees.Count}";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await LoadSiteLookupDataAsync();
        await SetSelectedSiteTextAsync(null);
        return View(new Employee());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Employee employee)
    {
        if (!ModelState.IsValid)
        {
            await LoadSiteLookupDataAsync();
            await SetSelectedSiteTextAsync(employee.SiteId);
            return View(employee);
        }

        _context.Employees.Add(employee);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Zaposlenik je uspješno dodan.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var employee = await _context.Employees
            .Include(e => e.Site)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (employee == null)
        {
            TempData["Error"] = "Zaposlenik više ne postoji ili je već obrisan.";
            return RedirectToAction(nameof(Index));
        }

        await LoadSiteLookupDataAsync();
        await SetSelectedSiteTextAsync(employee.SiteId);

        return View(employee);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Employee employee)
    {
        if (id != employee.Id)
            return NotFound();

        if (!ModelState.IsValid)
        {
            await LoadSiteLookupDataAsync();
            await SetSelectedSiteTextAsync(employee.SiteId);
            return View(employee);
        }

        _context.Update(employee);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Zaposlenik je uspješno ažuriran.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var employee = await _context.Employees
            .Include(e => e.Site)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (employee == null)
        {
            TempData["Error"] = "Zaposlenik više ne postoji ili je već obrisan.";
            return RedirectToAction(nameof(Index));
        }

        return View(employee);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var employee = await _context.Employees.FindAsync(id);

        if (employee == null)
        {
            TempData["Error"] = "Zaposlenik više ne postoji ili je već obrisan.";
            return RedirectToAction(nameof(Index));
        }

        var equipmentIds = await _context.Equipment
            .Where(x => x.CurrentEmployeeId == employee.Id)
            .Select(x => x.Id)
            .ToListAsync();

        _recycleBin.MoveEmployee(employee, equipmentIds);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Zaposlenik je premješten u Nedavno obrisano.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<Employee>> GetFilteredEmployeesAsync(string searchString, string statusFilter, string siteFilter, string sortBy, string sortDir)
    {
        var query = _context.Employees
            .Include(e => e.Site)
            .AsSplitQuery()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(statusFilter) &&
            Enum.TryParse<EmployeeStatus>(statusFilter, true, out var parsedStatus) &&
            Enum.IsDefined(typeof(EmployeeStatus), parsedStatus))
        {
            query = query.Where(e => e.Status == parsedStatus);
        }

        bool desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        query = (sortBy ?? "sifra").ToLower() switch
        {
            "ime" => desc
                ? query.OrderByDescending(e => e.FullName)
                : query.OrderBy(e => e.FullName),

            "nalog" => desc
                ? query.OrderByDescending(e => e.Site != null ? e.Site.Name : "")
                : query.OrderBy(e => e.Site != null ? e.Site.Name : ""),

            "status" => desc
                ? query.OrderByDescending(e => e.Status)
                : query.OrderBy(e => e.Status),

            _ => desc
                ? query.OrderByDescending(e => e.WorkerCode)
                : query.OrderBy(e => e.WorkerCode)
        };

        var employees = await query.ToListAsync();

        if (!string.IsNullOrWhiteSpace(siteFilter))
        {
            var normalizedSiteFilter = RemoveDiacritics(NormalizeSearch(siteFilter));

            employees = employees
                .Where(e =>
                    RemoveDiacritics(NormalizeSearch(e.Site?.Name)).Contains(normalizedSiteFilter) ||
                    RemoveDiacritics(NormalizeSearch(e.Site?.Code)).Contains(normalizedSiteFilter))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(searchString))
        {
            var searchTokens = TokenizeSearch(searchString);

            employees = employees
                .Where(e => EmployeeMatchesUniversalSearch(e, searchTokens))
                .ToList();
        }

        return employees;
    }

    private async Task LoadEmployeeStatsAsync()
    {
        var ukupno = await _context.Employees.CountAsync();
        var aktivni = await _context.Employees.CountAsync(e => e.Status == EmployeeStatus.Aktivan);
        var neaktivni = ukupno - aktivni;

        ViewBag.Aktivni = aktivni;
        ViewBag.Neaktivni = neaktivni;
        ViewBag.Ukupno = ukupno;
    }

    private void SetEmployeeListViewBags(string searchString, string statusFilter, string siteFilter, string sortBy, string sortDir)
    {
        ViewBag.SearchString = searchString;
        ViewBag.StatusFilter = statusFilter;
        ViewBag.SiteFilter = siteFilter;
        ViewBag.SortBy = string.IsNullOrWhiteSpace(sortBy) ? "sifra" : sortBy;
        ViewBag.SortDir = string.IsNullOrWhiteSpace(sortDir) ? "asc" : sortDir;
    }

    private async Task LoadBulkEditLookupDataAsync(EmployeeBulkEditViewModel vm)
    {
        vm.Sites = await _context.Sites
            .OrderBy(s => s.Name)
            .Select(s => new SelectListItem
            {
                Value = s.Id.ToString(),
                Text = s.Name
            })
            .ToListAsync();
    }

    private async Task LoadSiteLookupDataAsync()
    {
        var sites = await _context.Sites
            .OrderBy(s => s.Name)
            .ToListAsync();

        ViewBag.Sites = sites.Select(s => new SelectListItem
        {
            Value = s.Id.ToString(),
            Text = s.Name
        }).ToList();
    }

    private async Task SetSelectedSiteTextAsync(int? siteId)
    {
        ViewBag.SelectedSiteText = siteId.HasValue
            ? await _context.Sites
                .Where(s => s.Id == siteId.Value)
                .Select(s => s.Name)
                .FirstOrDefaultAsync() ?? string.Empty
            : string.Empty;
    }

    private bool EmployeeMatchesUniversalSearch(Employee employee, List<string> tokens)
    {
        if (tokens == null || tokens.Count == 0)
            return true;

        var searchValues = BuildEmployeeSearchValues(employee);

        foreach (var token in tokens)
        {
            bool matched = searchValues.Any(v => v.Contains(token));
            if (!matched)
                return false;
        }

        return true;
    }

    private List<string> BuildEmployeeSearchValues(Employee employee)
    {
        var values = new List<string>();

        AddSearchVariants(values, employee.WorkerCode);
        AddSearchVariants(values, employee.FullName);
        AddSearchVariants(values, employee.Site?.Name);
        AddSearchVariants(values, employee.Site?.Code);
        AddSearchVariants(values, NormalizeEmployeeStatus(employee.Status));

        var combined = string.Join(" ", values.Distinct());
        AddSearchVariants(values, combined);

        return values.Distinct().ToList();
    }

    private bool SuggestionFieldMatchesTokens(string? value, List<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(value) || tokens == null || tokens.Count == 0)
            return false;

        var variants = new List<string>();
        AddSearchVariants(variants, value);

        return tokens.All(token => variants.Any(v => v.Contains(token)));
    }

    private void AddSearchVariants(List<string> values, string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return;

        var normalized = NormalizeSearch(input);
        var noDiacritics = RemoveDiacritics(normalized);

        if (!string.IsNullOrWhiteSpace(normalized))
            values.Add(normalized);

        if (!string.IsNullOrWhiteSpace(noDiacritics))
            values.Add(noDiacritics);

        foreach (var token in TokenizeSearch(input))
        {
            if (!string.IsNullOrWhiteSpace(token))
                values.Add(token);
        }
    }

    private List<string> TokenizeSearch(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new List<string>();

        var normalized = RemoveDiacritics(NormalizeSearch(input));

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();
    }

    private string NormalizeEmployeeStatus(EmployeeStatus status)
    {
        return status switch
        {
            EmployeeStatus.Aktivan => "aktivan aktivno active",
            EmployeeStatus.Neaktivan => "neaktivan neaktivno inactive",
            _ => status.ToString().ToLower()
        };
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