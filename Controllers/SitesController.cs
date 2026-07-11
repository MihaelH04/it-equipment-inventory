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

[Authorize(Roles = "Admin")]
public class SitesController : Controller
{
    private readonly AppDbContext _context;
    private readonly RecycleBinService _recycleBin;

    public SitesController(AppDbContext context, RecycleBinService recycleBin)
    {
        _context = context;
        _recycleBin = recycleBin;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string searchString, string sortBy, string sortDir, string statusFilter)
    {
        var sites = await GetFilteredSitesAsync(searchString, sortBy, sortDir, statusFilter);
        await LoadSiteStatsAsync();
        SetSiteListViewBags(searchString, sortBy, sortDir, statusFilter);

        return View(sites);
    }

    [HttpGet]
    public async Task<IActionResult> IndexTable(string searchString, string sortBy, string sortDir, string statusFilter)
    {
        var sites = await GetFilteredSitesAsync(searchString, sortBy, sortDir, statusFilter);
        SetSiteListViewBags(searchString, sortBy, sortDir, statusFilter);

        return PartialView("_SitesTable", sites);
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel(string searchString, string sortBy, string sortDir, string statusFilter)
    {
        var sites = await GetFilteredSitesAsync(searchString, sortBy, sortDir, statusFilter);

        var bytes = ExcelExportHelper.CreateExcel(
            "Radni nalozi",
            new[] { "Naziv", "Šifra", "Lokacija", "Status" },
            sites.Select(s => new object?[]
            {
                s.Name,
                s.Code,
                s.Location,
                s.Status.ToString()
            }));

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ExcelExportHelper.FileName("Radni_nalozi"));
    }

    [HttpGet]
    public async Task<IActionResult> SearchSiteSuggestions(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return Json(new List<object>());

        var tokens = TokenizeSearch(term);

        var sites = await _context.Sites
            .AsNoTracking()
            .OrderBy(s => s.Name)
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

        foreach (var site in sites.Where(s => SiteMatchesSearch(s, tokens)).Take(40))
        {
            AddMatchingSuggestionPair(site.Name, site.Name);
            AddMatchingSuggestionPair(site.Code, site.Code);
            AddMatchingSuggestionPair(site.Location, site.Location);

            if ((site.Status == SiteStatus.Aktivno && tokens.Any(IsActiveStatusToken)) ||
                (site.Status == SiteStatus.Neaktivno && tokens.Any(IsInactiveStatusToken)))
            {
                AddSuggestionPair(site.Status.ToString(), site.Status.ToString());
            }

            var combinedText = string.Join(" | ", new[]
            {
                site.Code,
                site.Name,
                site.Location,
                site.Status.ToString()
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

            AddSuggestionPair(combinedText, !string.IsNullOrWhiteSpace(site.Code) ? site.Code : site.Name);
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
            TempData["Error"] = "Odaberi barem jedan radni nalog za brisanje.";
            return RedirectToAction(nameof(Index));
        }

        TempData["SelectedSiteDeleteIds"] = string.Join(",", selectedIds);
        return RedirectToAction(nameof(DeleteSelectedConfirm));
    }

    [HttpGet]
    public async Task<IActionResult> DeleteSelectedConfirm()
    {
        var selectedIdsRaw = TempData["SelectedSiteDeleteIds"] as string;

        if (string.IsNullOrWhiteSpace(selectedIdsRaw))
        {
            TempData["Error"] = "Nema odabranih radnih naloga za brisanje.";
            return RedirectToAction(nameof(Index));
        }

        TempData.Keep("SelectedSiteDeleteIds");

        var selectedIds = selectedIdsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToList();

        var sites = await _context.Sites
            .Where(s => selectedIds.Contains(s.Id))
            .OrderBy(s => s.Name)
            .ToListAsync();

        if (!sites.Any())
        {
            TempData["Error"] = "Odabrani radni nalozi nisu pronađeni.";
            return RedirectToAction(nameof(Index));
        }

        return View(sites);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSelectedConfirmed(int[] selectedIds)
    {
        if (selectedIds == null || selectedIds.Length == 0)
        {
            TempData["Error"] = "Nema odabranih radnih naloga za brisanje.";
            return RedirectToAction(nameof(Index));
        }

        var sites = await _context.Sites
            .Where(s => selectedIds.Contains(s.Id))
            .ToListAsync();

        if (!sites.Any())
        {
            TempData["Error"] = "Odabrani radni nalozi nisu pronađeni.";
            return RedirectToAction(nameof(Index));
        }

        foreach (var site in sites)
        {
            var employeeIds = await _context.Employees.Where(x => x.SiteId == site.Id).Select(x => x.Id).ToListAsync();
            var equipmentIds = await _context.Equipment.Where(x => x.CurrentSiteId == site.Id).Select(x => x.Id).ToListAsync();
            _recycleBin.MoveSite(site, employeeIds, equipmentIds);
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = $"U Nedavno obrisano premješteno radnih naloga: {sites.Count}";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult BulkEdit()
    {
        var selectedIdsRaw = TempData["SelectedSiteIds"] as string;

        if (string.IsNullOrWhiteSpace(selectedIdsRaw))
        {
            TempData["Error"] = "Nema odabranih radnih naloga za grupno uređivanje.";
            return RedirectToAction(nameof(Index));
        }

        var selectedIds = selectedIdsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToList();

        TempData.Keep("SelectedSiteIds");

        var vm = new SiteBulkEditViewModel
        {
            SelectedIds = selectedIds
        };

        return View("BulkEdit", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult BulkEdit(int[] selectedIds)
    {
        if (selectedIds == null || selectedIds.Length == 0)
        {
            TempData["Error"] = "Odaberi barem jedan radni nalog za grupno uređivanje.";
            return RedirectToAction(nameof(Index));
        }

        TempData["SelectedSiteIds"] = string.Join(",", selectedIds);
        return RedirectToAction(nameof(BulkEdit));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkEditSave(SiteBulkEditViewModel model)
    {
        if (model.SelectedIds == null || model.SelectedIds.Count == 0)
        {
            TempData["Error"] = "Nema odabranih radnih naloga za spremanje.";
            return RedirectToAction(nameof(Index));
        }

        var sites = await _context.Sites
            .Where(s => model.SelectedIds.Contains(s.Id))
            .ToListAsync();

        if (!sites.Any())
        {
            TempData["Error"] = "Odabrani radni nalozi nisu pronađeni.";
            return RedirectToAction(nameof(Index));
        }

        foreach (var site in sites)
        {
            if (!string.IsNullOrWhiteSpace(model.Name))
                site.Name = model.Name;

            if (!string.IsNullOrWhiteSpace(model.Code))
                site.Code = model.Code;
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = $"Ažurirano radnih naloga: {sites.Count}";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new Site { Status = SiteStatus.Aktivno });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Site site)
    {
        if (!ModelState.IsValid)
            return View(site);

        _context.Sites.Add(site);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Radni nalog je uspješno dodan.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var site = await _context.Sites.FirstOrDefaultAsync(s => s.Id == id);

        if (site == null)
        {
            TempData["Error"] = "Radni nalog više ne postoji ili je već obrisan.";
            return RedirectToAction(nameof(Index));
        }

        return View(site);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Site site)
    {
        if (id != site.Id)
            return NotFound();

        if (!ModelState.IsValid)
            return View(site);

        _context.Update(site);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Radni nalog je uspješno ažuriran.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var site = await _context.Sites.FirstOrDefaultAsync(s => s.Id == id);

        if (site == null)
        {
            TempData["Error"] = "Radni nalog više ne postoji ili je već obrisan.";
            return RedirectToAction(nameof(Index));
        }

        return View(site);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var site = await _context.Sites.FindAsync(id);

        if (site == null)
        {
            TempData["Error"] = "Radni nalog više ne postoji ili je već obrisan.";
            return RedirectToAction(nameof(Index));
        }

        var employeeIds = await _context.Employees.Where(x => x.SiteId == site.Id).Select(x => x.Id).ToListAsync();
        var equipmentIds = await _context.Equipment.Where(x => x.CurrentSiteId == site.Id).Select(x => x.Id).ToListAsync();

        _recycleBin.MoveSite(site, employeeIds, equipmentIds);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Radni nalog je premješten u Nedavno obrisano.";
        return RedirectToAction(nameof(Index));
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private async Task<List<Site>> GetFilteredSitesAsync(
        string searchString, string sortBy, string sortDir, string statusFilter)
    {
        var query = _context.Sites.AsQueryable();

        // Status filter
        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            if (statusFilter == "Aktivno")
                query = query.Where(s => s.Status == SiteStatus.Aktivno);
            else if (statusFilter == "Neaktivno")
                query = query.Where(s => s.Status == SiteStatus.Neaktivno);
        }

        bool desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        query = (sortBy ?? "naziv").ToLower() switch
        {
            "sifra" => desc
                ? query.OrderByDescending(s => s.Code)
                : query.OrderBy(s => s.Code),

            "lokacija" => desc
                ? query.OrderByDescending(s => s.Location)
                : query.OrderBy(s => s.Location),

            "status" => desc
                ? query.OrderByDescending(s => s.Status)
                : query.OrderBy(s => s.Status),

            _ => desc
                ? query.OrderByDescending(s => s.Name)
                : query.OrderBy(s => s.Name)
        };

        var sites = await query.ToListAsync();

        if (!string.IsNullOrWhiteSpace(searchString))
        {
            var tokens = TokenizeSearch(searchString);
            sites = sites.Where(s => SiteMatchesSearch(s, tokens)).ToList();
        }

        return sites;
    }

    private async Task LoadSiteStatsAsync()
    {
        ViewBag.Ukupno    = await _context.Sites.CountAsync();
        ViewBag.Aktivno   = await _context.Sites.CountAsync(s => s.Status == SiteStatus.Aktivno);
        ViewBag.Neaktivno = await _context.Sites.CountAsync(s => s.Status == SiteStatus.Neaktivno);
    }

    private void SetSiteListViewBags(
        string searchString, string sortBy, string sortDir, string statusFilter)
    {
        ViewBag.SearchString  = searchString;
        ViewBag.SortBy        = string.IsNullOrWhiteSpace(sortBy)   ? "naziv" : sortBy;
        ViewBag.SortDir       = string.IsNullOrWhiteSpace(sortDir)  ? "asc"   : sortDir;
        ViewBag.StatusFilter  = statusFilter ?? "";
    }

    private bool SiteMatchesSearch(Site site, List<string> tokens)
    {
        if (tokens == null || tokens.Count == 0) return true;

        var hasInactiveStatusToken = tokens.Any(IsInactiveStatusToken);
        var hasActiveStatusToken = !hasInactiveStatusToken && tokens.Any(IsActiveStatusToken);

        if (hasInactiveStatusToken && site.Status != SiteStatus.Neaktivno)
            return false;

        if (hasActiveStatusToken && site.Status != SiteStatus.Aktivno)
            return false;

        var searchableTokens = tokens
            .Where(t => !IsInactiveStatusToken(t) && !IsActiveStatusToken(t))
            .ToList();

        var values = new List<string>();
        AddSearchVariants(values, site.Name);
        AddSearchVariants(values, site.Code);
        AddSearchVariants(values, site.Location);

        foreach (var token in searchableTokens)
        {
            if (!values.Any(v => v.Contains(token)))
                return false;
        }

        return true;
    }

    private bool SuggestionFieldMatchesTokens(string? value, List<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(value) || tokens == null || tokens.Count == 0)
            return false;

        var variants = new List<string>();
        AddSearchVariants(variants, value);

        return tokens.All(token => variants.Any(v => v.Contains(token)));
    }

    private bool IsActiveStatusToken(string token)
    {
        return token is "akt" or "akti" or "aktiv" or "aktivno" or "aktivan" or "active";
    }

    private bool IsInactiveStatusToken(string token)
    {
        return token is "neakt" or "neakti" or "neaktiv" or "neaktivno" or "neaktivan" or "inactive";
    }

    private string NormalizeSiteStatus(SiteStatus status)
    {
        return status switch
        {
            SiteStatus.Aktivno => "aktivno aktivan akti active",
            SiteStatus.Neaktivno => "neaktivno neaktivan neakti inactive",
            _ => status.ToString().ToLower()
        };
    }

    private void AddSearchVariants(List<string> values, string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;

        var normalized    = NormalizeSearch(input);
        var noDiacritics  = RemoveDiacritics(normalized);

        if (!string.IsNullOrWhiteSpace(normalized))   values.Add(normalized);
        if (!string.IsNullOrWhiteSpace(noDiacritics)) values.Add(noDiacritics);

        foreach (var token in TokenizeSearch(input))
            if (!string.IsNullOrWhiteSpace(token)) values.Add(token);
    }

    private List<string> TokenizeSearch(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new List<string>();

        return RemoveDiacritics(NormalizeSearch(input))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();
    }

    private string NormalizeSearch(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        return string.Join(" ",
            input.Trim()
                 .ToLower(new CultureInfo("hr-HR"))
                 .Replace("_", " ").Replace("-", " ").Replace(".", " ")
                 .Replace(",", " ").Replace("/", " ")
                 .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private string RemoveDiacritics(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var sb = new StringBuilder();
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}