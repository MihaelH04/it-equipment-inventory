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
public class EquipmentController : Controller
{
    private readonly AppDbContext _context;
    private readonly RecycleBinService _recycleBin;

    public EquipmentController(AppDbContext context, RecycleBinService recycleBin)
    {
        _context = context;
        _recycleBin = recycleBin;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string searchString, string sortOrder, string statusFilter)
    {
        var equipmentList = await GetFilteredEquipmentAsync(searchString, sortOrder, statusFilter);
        await LoadEquipmentStatsAsync();
        SetSortAndFilterViewBags(searchString, sortOrder, statusFilter);

        return View(equipmentList);
    }

    [HttpGet]
    public async Task<IActionResult> IndexTable(string searchString, string sortOrder, string statusFilter)
    {
        var equipmentList = await GetFilteredEquipmentAsync(searchString, sortOrder, statusFilter);
        SetSortAndFilterViewBags(searchString, sortOrder, statusFilter);

        return PartialView("_EquipmentTable", equipmentList);
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel(string searchString, string sortOrder, string statusFilter)
    {
        var equipmentList = await GetFilteredEquipmentAsync(searchString, sortOrder, statusFilter);

        var bytes = ExcelExportHelper.CreateExcel(
            "Oprema",
            new[]
            {
                "Inventurni broj", "Serijski broj", "Vrsta", "Naziv", "Status",
                "Radni nalog", "Zaposlenik", "Datum zaduženja", "Predao"
            },
            equipmentList.Select(e => new object?[]
            {
                e.InventoryNumber,
                e.SerialNumber,
                e.EquipmentType.ToString(),
                e.Name,
                e.Status == EquipmentStatus.Zaduzeno ? "Zaduženo" : e.Status.ToString(),
                e.CurrentSite == null ? null : string.IsNullOrWhiteSpace(e.CurrentSite.Code) ? e.CurrentSite.Name : e.CurrentSite.Code + " - " + e.CurrentSite.Name,
                e.CurrentEmployee == null ? null : string.IsNullOrWhiteSpace(e.CurrentEmployee.WorkerCode) ? e.CurrentEmployee.FullName : e.CurrentEmployee.WorkerCode + " - " + e.CurrentEmployee.FullName,
                e.AssignedAt?.ToString("dd.MM.yyyy"),
                e.HandedOverBy
            }));

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ExcelExportHelper.FileName("Oprema"));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteSelected(int[] selectedIds)
    {
        if (selectedIds == null || selectedIds.Length == 0)
        {
            TempData["Error"] = "Odaberi barem jednu stavku za brisanje.";
            return RedirectToAction(nameof(Index));
        }

        TempData["SelectedEquipmentDeleteIds"] = string.Join(",", selectedIds);
        return RedirectToAction(nameof(DeleteSelectedConfirm));
    }

    [HttpGet]
    public async Task<IActionResult> DeleteSelectedConfirm()
    {
        var selectedIdsRaw = TempData["SelectedEquipmentDeleteIds"] as string;

        if (string.IsNullOrWhiteSpace(selectedIdsRaw))
        {
            TempData["Error"] = "Nema odabranih stavki za brisanje.";
            return RedirectToAction(nameof(Index));
        }

        TempData.Keep("SelectedEquipmentDeleteIds");

        var selectedIds = selectedIdsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToList();

        var items = await _context.Equipment
            .Include(e => e.CurrentSite)
            .Include(e => e.CurrentEmployee)
            .Where(e => selectedIds.Contains(e.Id))
            .OrderBy(e => e.InventoryNumber)
            .ToListAsync();

        if (!items.Any())
        {
            TempData["Error"] = "Odabrane stavke nisu pronađene.";
            return RedirectToAction(nameof(Index));
        }

        return View(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSelectedConfirmed(int[] selectedIds)
    {
        if (selectedIds == null || selectedIds.Length == 0)
        {
            TempData["Error"] = "Nema odabranih stavki za brisanje.";
            return RedirectToAction(nameof(Index));
        }

        var items = await _context.Equipment
            .Where(e => selectedIds.Contains(e.Id))
            .ToListAsync();

        if (!items.Any())
        {
            TempData["Error"] = "Odabrane stavke nisu pronađene.";
            return RedirectToAction(nameof(Index));
        }

        foreach (var item in items)
            _recycleBin.MoveEquipment(item);

        await _context.SaveChangesAsync();

        TempData["Success"] = $"U Nedavno obrisano premješteno stavki: {items.Count}";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult BulkEdit(int[] selectedIds)
    {
        if (selectedIds == null || selectedIds.Length == 0)
        {
            TempData["Error"] = "Odaberi barem jednu stavku za grupno uređivanje.";
            return RedirectToAction(nameof(Index));
        }

        TempData["SelectedEquipmentIds"] = string.Join(",", selectedIds);
        return RedirectToAction(nameof(BulkEdit));
    }

    [HttpGet]
    public async Task<IActionResult> BulkEdit()
    {
        var selectedIdsRaw = TempData["SelectedEquipmentIds"] as string;

        if (string.IsNullOrWhiteSpace(selectedIdsRaw))
        {
            TempData["Error"] = "Nema odabranih stavki za grupno uređivanje.";
            return RedirectToAction(nameof(Index));
        }

        var selectedIds = selectedIdsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToList();

        TempData.Keep("SelectedEquipmentIds");

        var vm = new EquipmentBulkEditViewModel
        {
            SelectedIds = selectedIds
        };

        await LoadBulkEditLookupDataAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkEditSave(EquipmentBulkEditViewModel model)
    {
        if (model.SelectedIds == null || model.SelectedIds.Count == 0)
        {
            TempData["Error"] = "Nema odabranih stavki za spremanje.";
            return RedirectToAction(nameof(Index));
        }

        model.HandedOverBy = NormalizeNullableText(model.HandedOverBy);

        if (!await IsValidHandedOverByAdminAsync(model.HandedOverBy))
        {
            ModelState.AddModelError(
                nameof(model.HandedOverBy),
                "Odabrana osoba mora biti aktivni administrator iz korisnika."
            );

            await LoadBulkEditLookupDataAsync(model);
            TempData["SelectedEquipmentIds"] = string.Join(",", model.SelectedIds);
            return View("BulkEdit", model);
        }

        var items = await _context.Equipment
            .Where(e => model.SelectedIds.Contains(e.Id))
            .ToListAsync();

        if (!items.Any())
        {
            TempData["Error"] = "Odabrane stavke nisu pronađene.";
            return RedirectToAction(nameof(Index));
        }

        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(model.HandedOverBy))
                item.HandedOverBy = model.HandedOverBy;

            if (model.Status.HasValue)
                item.Status = model.Status.Value;

            if (model.CurrentSiteId.HasValue)
                item.CurrentSiteId = model.CurrentSiteId;

            if (model.CurrentEmployeeId.HasValue)
                item.CurrentEmployeeId = model.CurrentEmployeeId;

            if (model.AssignedAt.HasValue)
                item.AssignedAt = model.AssignedAt.Value.Date;

            if (model.ReturnedAt.HasValue)
                item.ReturnedAt = model.ReturnedAt.Value.Date;

            if (model.Status.HasValue && model.Status.Value == EquipmentStatus.Dostupno)
            {
                item.CurrentSiteId = null;
                item.CurrentEmployeeId = null;

                if (!model.ReturnedAt.HasValue)
                    item.ReturnedAt = DateTime.Today;
            }

            if (model.Status.HasValue && model.Status.Value == EquipmentStatus.Zaduzeno)
            {
                item.ReturnedAt = null;
            }
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = $"Ažurirano stavki: {items.Count}";
        return RedirectToAction(nameof(Index));
    }

  [HttpPost]
[ValidateAntiForgeryToken]
public IActionResult BulkReturn(int[] selectedIds)
{
    if (selectedIds == null || selectedIds.Length == 0)
    {
        TempData["Error"] = "Odaberi barem jednu stavku za razduživanje.";
        return RedirectToAction(nameof(Index));
    }

    TempData["SelectedEquipmentReturnIds"] = string.Join(",", selectedIds);
    return RedirectToAction(nameof(BulkReturnDecision));
}

[HttpGet]
public async Task<IActionResult> BulkReturnDecision()
{
    var selectedIdsRaw = TempData["SelectedEquipmentReturnIds"] as string;

    if (string.IsNullOrWhiteSpace(selectedIdsRaw))
    {
        TempData["Error"] = "Nema odabranih stavki za razduživanje.";
        return RedirectToAction(nameof(Index));
    }

    TempData.Keep("SelectedEquipmentReturnIds");

    var selectedIds = selectedIdsRaw
        .Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(int.Parse)
        .ToList();

    var items = await _context.Equipment
        .Include(e => e.CurrentSite)
        .Include(e => e.CurrentEmployee)
        .Where(e => selectedIds.Contains(e.Id))
        .OrderBy(e => e.InventoryNumber)
        .ToListAsync();

    if (!items.Any())
    {
        TempData["Error"] = "Odabrane stavke nisu pronađene.";
        return RedirectToAction(nameof(Index));
    }

    var vm = new EquipmentBulkReturnDecisionViewModel
    {
        SelectedIds = selectedIds,
        SelectedEquipment = items,
        EffectiveDate = DateTime.Today,
        ActionType = "free"
    };

    await LoadBulkReturnDecisionLookupDataAsync(vm);
    return View(vm);
}

[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> BulkReturnDecisionSave(EquipmentBulkReturnDecisionViewModel model)
{
    if (model.SelectedIds == null || model.SelectedIds.Count == 0)
    {
        TempData["Error"] = "Nema odabranih stavki za razduživanje.";
        return RedirectToAction(nameof(Index));
    }

    var items = await _context.Equipment
        .Include(e => e.CurrentSite)
        .Include(e => e.CurrentEmployee)
        .Where(e => model.SelectedIds.Contains(e.Id))
        .ToListAsync();

    if (!items.Any())
    {
        TempData["Error"] = "Odabrane stavke nisu pronađene.";
        return RedirectToAction(nameof(Index));
    }

    var effectiveDate = model.EffectiveDate.Date;

    if (model.ActionType == "transfer" && !model.NewEmployeeId.HasValue)
    {
        ModelState.AddModelError(nameof(model.NewEmployeeId), "Odaberi novog zaposlenika.");
    }

    model.HandedOverBy = NormalizeNullableText(model.HandedOverBy);

    if (!await IsValidHandedOverByAdminAsync(model.HandedOverBy))
    {
        ModelState.AddModelError(
            nameof(model.HandedOverBy),
            "Odabrana osoba mora biti aktivni administrator iz korisnika."
        );
    }

    if (!ModelState.IsValid)
    {
        model.SelectedEquipment = items;
        await LoadBulkReturnDecisionLookupDataAsync(model);
        return View("BulkReturnDecision", model);
    }

    foreach (var item in items)
    {
        var returnRecord = new EquipmentReturn
        {
            EquipmentId = item.Id,
            InventoryNumber = item.InventoryNumber,
            SerialNumber = item.SerialNumber,
            EquipmentType = item.EquipmentType,
            Name = item.Name,
            PreviousStatus = item.Status,
            PreviousSiteId = item.CurrentSiteId,
            PreviousSiteCode = item.CurrentSite?.Code,
            PreviousSiteName = item.CurrentSite?.Name,
            PreviousSiteLocation = item.CurrentSite?.Location,
            PreviousEmployeeId = item.CurrentEmployeeId,
            PreviousEmployeeCode = item.CurrentEmployee?.WorkerCode,
            PreviousEmployeeName = item.CurrentEmployee?.FullName,
            AssignedAt = item.AssignedAt,
            ReturnedAt = effectiveDate,
            PreviousHandedOverBy = item.HandedOverBy,
            HandedOverBy = string.IsNullOrWhiteSpace(model.HandedOverBy) ? item.HandedOverBy : model.HandedOverBy
        };

        _context.EquipmentReturns.Add(returnRecord);

        if (model.ActionType == "transfer")
        {
            item.CurrentEmployeeId = model.NewEmployeeId;
            item.CurrentSiteId = model.NewSiteId;
            item.AssignedAt = effectiveDate;
            item.ReturnedAt = null;
            item.Status = EquipmentStatus.Zaduzeno;

            if (!string.IsNullOrWhiteSpace(model.HandedOverBy))
                item.HandedOverBy = model.HandedOverBy;
        }
        else
        {
            item.Status = EquipmentStatus.Dostupno;
            item.CurrentSiteId = null;
            item.CurrentEmployeeId = null;
            item.AssignedAt = null;
            item.ReturnedAt = effectiveDate;
            item.HandedOverBy = null;
        }
    }

    await _context.SaveChangesAsync();

    TempData["Success"] = model.ActionType == "transfer"
        ? $"Razduženo i ponovno zaduženo stavki: {items.Count}"
        : $"Razduženo stavki: {items.Count}";

    TempData.Remove("SelectedEquipmentReturnIds");
    return RedirectToAction(nameof(Index));
}

private async Task LoadBulkReturnDecisionLookupDataAsync(EquipmentBulkReturnDecisionViewModel vm)
{
    vm.Sites = await _context.Sites
        .OrderBy(s => s.Name)
        .Select(s => new SelectListItem
        {
            Value = s.Id.ToString(),
            Text = string.IsNullOrWhiteSpace(s.Code) ? s.Name : s.Code + " - " + s.Name
        })
        .ToListAsync();

    vm.Employees = await _context.Employees
        .OrderBy(e => e.FullName)
        .Select(e => new SelectListItem
        {
            Value = e.Id.ToString(),
            Text = string.IsNullOrWhiteSpace(e.WorkerCode) ? e.FullName : e.WorkerCode + " - " + e.FullName
        })
        .ToListAsync();

    vm.HandedOverByOptions = await GetHandedOverByAdminOptionsAsync("-- Odaberi admina --");
}


    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await LoadLookupDataAsync();
        await SetSelectedTextsAsync(null, null);

        return View(new Equipment
        {
            AssignedAt = DateTime.Today
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Equipment equipment)
    {
        if (equipment.AssignedAt.HasValue)
            equipment.AssignedAt = equipment.AssignedAt.Value.Date;

        if (equipment.ReturnedAt.HasValue)
            equipment.ReturnedAt = equipment.ReturnedAt.Value.Date;

        equipment.HandedOverBy = NormalizeNullableText(equipment.HandedOverBy);

        if (equipment.Status == EquipmentStatus.Zaduzeno && string.IsNullOrWhiteSpace(equipment.HandedOverBy))
        {
            ModelState.AddModelError(
                nameof(equipment.HandedOverBy),
                "Za zaduženje opreme odaberi aktivnog administratora iz korisnika."
            );
        }
        else if (!await IsValidHandedOverByAdminAsync(equipment.HandedOverBy))
        {
            ModelState.AddModelError(
                nameof(equipment.HandedOverBy),
                "Zadužiti može samo aktivni administrator iz korisnika."
            );
        }

        if (equipment.Status == EquipmentStatus.Dostupno)
        {
            equipment.CurrentSiteId = null;
            equipment.CurrentEmployeeId = null;

            if (!equipment.ReturnedAt.HasValue)
                equipment.ReturnedAt = DateTime.Today;
        }

        if (equipment.Status == EquipmentStatus.Zaduzeno)
        {
            equipment.ReturnedAt = null;
        }

        if (!ModelState.IsValid)
        {
            await LoadLookupDataAsync();
            await SetSelectedTextsAsync(equipment.CurrentSiteId, equipment.CurrentEmployeeId);
            return View(equipment);
        }

        _context.Equipment.Add(equipment);
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var equipment = await _context.Equipment
            .Include(e => e.CurrentSite)
            .Include(e => e.CurrentEmployee)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (equipment == null)
        {
            TempData["Error"] = "Oprema više ne postoji ili je već obrisana.";
            return RedirectToAction(nameof(Index));
        }

        await LoadLookupDataAsync();
        await SetSelectedTextsAsync(equipment.CurrentSiteId, equipment.CurrentEmployeeId);

        return View(equipment);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Equipment equipment)
    {
        if (id != equipment.Id)
            return NotFound();

        if (equipment.AssignedAt.HasValue)
            equipment.AssignedAt = equipment.AssignedAt.Value.Date;

        if (equipment.ReturnedAt.HasValue)
            equipment.ReturnedAt = equipment.ReturnedAt.Value.Date;

        equipment.HandedOverBy = NormalizeNullableText(equipment.HandedOverBy);

        if (equipment.Status == EquipmentStatus.Zaduzeno && string.IsNullOrWhiteSpace(equipment.HandedOverBy))
        {
            ModelState.AddModelError(
                nameof(equipment.HandedOverBy),
                "Za zaduženje opreme odaberi aktivnog administratora iz korisnika."
            );
        }
        else if (!await IsValidHandedOverByAdminAsync(equipment.HandedOverBy))
        {
            ModelState.AddModelError(
                nameof(equipment.HandedOverBy),
                "Zadužiti može samo aktivni administrator iz korisnika."
            );
        }

        if (equipment.Status == EquipmentStatus.Dostupno)
        {
            equipment.CurrentSiteId = null;
            equipment.CurrentEmployeeId = null;

            if (!equipment.ReturnedAt.HasValue)
                equipment.ReturnedAt = DateTime.Today;
        }

        if (equipment.Status == EquipmentStatus.Zaduzeno)
        {
            equipment.ReturnedAt = null;
        }

        if (!ModelState.IsValid)
        {
            await LoadLookupDataAsync();
            await SetSelectedTextsAsync(equipment.CurrentSiteId, equipment.CurrentEmployeeId);
            return View(equipment);
        }

        _context.Update(equipment);
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var equipment = await _context.Equipment
            .Include(e => e.CurrentSite)
            .Include(e => e.CurrentEmployee)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (equipment == null)
        {
            TempData["Error"] = "Oprema više ne postoji ili je već obrisana.";
            return RedirectToAction(nameof(Index));
        }

        return View(equipment);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var equipment = await _context.Equipment.FindAsync(id);
        if (equipment == null)
        {
            TempData["Error"] = "Oprema više ne postoji ili je već obrisana.";
            return RedirectToAction(nameof(Index));
        }

        _recycleBin.MoveEquipment(equipment);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Oprema je premještena u Nedavno obrisano.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> SearchSites(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return Json(new List<object>());

        var tokens = TokenizeSearch(term);

        var sites = await _context.Sites
            .OrderBy(s => s.Name)
            .Take(200)
            .ToListAsync();

        var results = sites
            .Where(s => SiteMatchesAutocomplete(s, tokens))
            .Take(10)
            .Select(s => new
            {
                id = s.Id,
                text = string.IsNullOrWhiteSpace(s.Code) ? s.Name : s.Code + " - " + s.Name
            })
            .ToList();

        return Json(results);
    }

    [HttpGet]
    public async Task<IActionResult> SearchEmployees(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return Json(new List<object>());

        var tokens = TokenizeSearch(term);

        var employees = await _context.Employees
            .OrderBy(e => e.FullName)
            .Take(300)
            .ToListAsync();

        var results = employees
            .Where(e => EmployeeMatchesAutocomplete(e, tokens))
            .Take(10)
            .Select(e => new
            {
                id = e.Id,
                text = string.IsNullOrWhiteSpace(e.WorkerCode) ? e.FullName : e.WorkerCode + " - " + e.FullName
            })
            .ToList();

        return Json(results);
    }

    [HttpGet]
    public async Task<IActionResult> SearchEquipmentSuggestions(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return Json(new List<object>());

        var tokens = TokenizeSearch(term);

        var equipmentList = await _context.Equipment
            .Include(e => e.CurrentSite)
            .Include(e => e.CurrentEmployee)
            .AsNoTracking()
            .OrderBy(e => e.InventoryNumber)
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

        void AddMatchingSuggestionPair(string? text, List<string> searchTokens, string? value = null)
        {
            if (SuggestionFieldMatchesTokens(text, searchTokens))
                AddSuggestionPair(text, value);
        }

        foreach (var item in equipmentList.Where(e => RecordMatchesAllTokens(e, tokens)).Take(40))
        {
            var employeeText = item.CurrentEmployee == null
                ? null
                : string.IsNullOrWhiteSpace(item.CurrentEmployee.WorkerCode)
                    ? item.CurrentEmployee.FullName
                    : item.CurrentEmployee.WorkerCode + " - " + item.CurrentEmployee.FullName;

            var siteText = item.CurrentSite == null
                ? null
                : string.IsNullOrWhiteSpace(item.CurrentSite.Code)
                    ? item.CurrentSite.Name
                    : item.CurrentSite.Code + " - " + item.CurrentSite.Name;

            AddMatchingSuggestionPair(item.InventoryNumber, tokens, item.InventoryNumber);
            AddMatchingSuggestionPair(item.SerialNumber, tokens, item.SerialNumber);
            AddMatchingSuggestionPair(item.Name, tokens, item.Name);
            AddMatchingSuggestionPair(item.CurrentEmployee?.FullName, tokens, item.CurrentEmployee?.FullName);
            AddMatchingSuggestionPair(item.CurrentEmployee?.WorkerCode, tokens, item.CurrentEmployee?.WorkerCode);
            AddMatchingSuggestionPair(employeeText, tokens, item.CurrentEmployee?.WorkerCode ?? item.CurrentEmployee?.FullName);
            AddMatchingSuggestionPair(item.CurrentSite?.Name, tokens, item.CurrentSite?.Name);
            AddMatchingSuggestionPair(item.CurrentSite?.Code, tokens, item.CurrentSite?.Code);
            AddMatchingSuggestionPair(siteText, tokens, item.CurrentSite?.Code ?? item.CurrentSite?.Name);

            if (SuggestionFieldMatchesTokens(item.EquipmentType + " " + NormalizeEquipmentTypeText(item.EquipmentType), tokens))
                AddSuggestionPair(item.EquipmentType.ToString(), item.EquipmentType.ToString());

            if (SuggestionFieldMatchesTokens(item.Status + " " + NormalizeEquipmentStatusText(item.Status), tokens))
                AddSuggestionPair(item.Status == EquipmentStatus.Zaduzeno ? "Zaduženo" : item.Status.ToString(), item.Status.ToString());

            var combinedText = string.Join(" | ", new[]
            {
                item.InventoryNumber,
                string.IsNullOrWhiteSpace(item.SerialNumber) ? null : "SN:" + item.SerialNumber,
                item.EquipmentType.ToString(),
                item.Name,
                employeeText,
                siteText
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

            // Ovo je najbitnije:
            // u padajućem izborniku se prikazuje cijeli opis,
            // ali nakon odabira TAB-om u search se upisuje samo inventurni broj.
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
            .Select(x => new
            {
                text = x.Text,
                value = x.Value
            })
            .ToList();

        return Json(results);
    }

    private async Task<List<Equipment>> GetFilteredEquipmentAsync(string searchString, string sortOrder, string statusFilter)
    {
        var query = _context.Equipment
            .Include(e => e.CurrentSite)
            .Include(e => e.CurrentEmployee)
            .AsSplitQuery()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(statusFilter) &&
            Enum.TryParse<EquipmentStatus>(statusFilter, true, out var parsedStatus) &&
            Enum.IsDefined(typeof(EquipmentStatus), parsedStatus))
        {
            query = query.Where(e => e.Status == parsedStatus);
        }

        query = sortOrder switch
        {
            "inventory_desc" => query.OrderByDescending(e => e.InventoryNumber),
            "inventory_asc" => query.OrderBy(e => e.InventoryNumber),
            "serial_desc" => query.OrderByDescending(e => e.SerialNumber),
            "serial_asc" => query.OrderBy(e => e.SerialNumber),
            "type_desc" => query.OrderByDescending(e => e.EquipmentType),
            "type_asc" => query.OrderBy(e => e.EquipmentType),
            "name_desc" => query.OrderByDescending(e => e.Name),
            "name_asc" => query.OrderBy(e => e.Name),
            "status_desc" => query.OrderByDescending(e => e.Status),
            "status_asc" => query.OrderBy(e => e.Status),
            "site_desc" => query.OrderByDescending(e => e.CurrentSite != null ? e.CurrentSite.Name : ""),
            "site_asc" => query.OrderBy(e => e.CurrentSite != null ? e.CurrentSite.Name : ""),
            "employee_desc" => query.OrderByDescending(e => e.CurrentEmployee != null ? e.CurrentEmployee.FullName : ""),
            "employee_asc" => query.OrderBy(e => e.CurrentEmployee != null ? e.CurrentEmployee.FullName : ""),
            "assignedat_desc" => query.OrderByDescending(e => e.AssignedAt),
            "assignedat_asc" => query.OrderBy(e => e.AssignedAt),
            _ => query.OrderBy(e => e.InventoryNumber)
        };

        var equipmentList = await query.ToListAsync();

        if (!string.IsNullOrWhiteSpace(searchString))
        {
            var rawSearch = searchString.Trim();
            var searchTokens = TokenizeSearch(rawSearch);

            equipmentList = equipmentList
                .Where(e => RecordMatchesAllTokens(e, searchTokens))
                .ToList();
        }

        return equipmentList;
    }

    private void SetSortAndFilterViewBags(string searchString, string sortOrder, string statusFilter)
    {
        ViewBag.SearchString = searchString;
        ViewBag.CurrentSort = sortOrder;
        ViewBag.CurrentStatusFilter = statusFilter;

        ViewBag.InventorySort = sortOrder == "inventory_asc" ? "inventory_desc" : "inventory_asc";
        ViewBag.SerialSort = sortOrder == "serial_asc" ? "serial_desc" : "serial_asc";
        ViewBag.TypeSort = sortOrder == "type_asc" ? "type_desc" : "type_asc";
        ViewBag.NameSort = sortOrder == "name_asc" ? "name_desc" : "name_asc";
        ViewBag.StatusSort = sortOrder == "status_asc" ? "status_desc" : "status_asc";
        ViewBag.SiteSort = sortOrder == "site_asc" ? "site_desc" : "site_asc";
        ViewBag.EmployeeSort = sortOrder == "employee_asc" ? "employee_desc" : "employee_asc";
        ViewBag.AssignedAtSort = sortOrder == "assignedat_asc" ? "assignedat_desc" : "assignedat_asc";
    }

    private async Task LoadEquipmentStatsAsync()
    {
        var allEquipment = await _context.Equipment.ToListAsync();

        ViewBag.TotalCount = allEquipment.Count;
        ViewBag.AvailableCount = allEquipment.Count(e => e.Status == EquipmentStatus.Dostupno);
        ViewBag.AssignedCount = allEquipment.Count(e => e.Status == EquipmentStatus.Zaduzeno);
        ViewBag.ServiceCount = allEquipment.Count(e => e.Status == EquipmentStatus.Servis);
        ViewBag.WrittenOffCount = allEquipment.Count(e => e.Status == EquipmentStatus.Otpisano);
    }

    private async Task LoadBulkEditLookupDataAsync(EquipmentBulkEditViewModel vm)
    {
        vm.Sites = await _context.Sites
            .OrderBy(s => s.Name)
            .Select(s => new SelectListItem
            {
                Value = s.Id.ToString(),
                Text = string.IsNullOrWhiteSpace(s.Code) ? s.Name : s.Code + " - " + s.Name
            })
            .ToListAsync();

        vm.Employees = await _context.Employees
            .OrderBy(e => e.FullName)
            .Select(e => new SelectListItem
            {
                Value = e.Id.ToString(),
                Text = string.IsNullOrWhiteSpace(e.WorkerCode) ? e.FullName : e.WorkerCode + " - " + e.FullName
            })
            .ToListAsync();

        vm.HandedOverByOptions = await GetHandedOverByAdminOptionsAsync("-- ne mijenjaj --");
    }

    private async Task LoadLookupDataAsync()
    {
        var sites = await _context.Sites.OrderBy(s => s.Name).ToListAsync();
        var employees = await _context.Employees.OrderBy(e => e.FullName).ToListAsync();

        ViewBag.Sites = sites.Select(s => new SelectListItem
        {
            Value = s.Id.ToString(),
            Text = string.IsNullOrWhiteSpace(s.Code) ? s.Name : s.Code + " - " + s.Name
        }).ToList();

        ViewBag.Employees = employees.Select(e => new SelectListItem
        {
            Value = e.Id.ToString(),
            Text = string.IsNullOrWhiteSpace(e.WorkerCode) ? e.FullName : e.WorkerCode + " - " + e.FullName
        }).ToList();

        ViewBag.HandedOverByOptions = await GetHandedOverByAdminOptionsAsync("-- Odaberi admina --");
    }

    private async Task SetSelectedTextsAsync(int? siteId, int? employeeId)
    {
        ViewBag.SelectedSiteText = siteId.HasValue
            ? await _context.Sites
                .Where(s => s.Id == siteId.Value)
                .Select(s => string.IsNullOrWhiteSpace(s.Code) ? s.Name : s.Code + " - " + s.Name)
                .FirstOrDefaultAsync() ?? string.Empty
            : string.Empty;

        ViewBag.SelectedEmployeeText = employeeId.HasValue
            ? await _context.Employees
                .Where(e => e.Id == employeeId.Value)
                .Select(e => string.IsNullOrWhiteSpace(e.WorkerCode) ? e.FullName : e.WorkerCode + " - " + e.FullName)
                .FirstOrDefaultAsync() ?? string.Empty
            : string.Empty;
    }


    private async Task<List<SelectListItem>> GetHandedOverByAdminOptionsAsync(string emptyText)
    {
        var admins = await _context.AppUsers
            .Where(u => u.IsActive && u.Role == AppUserRole.Admin)
            .OrderBy(u => u.FullName ?? u.UserName)
            .Select(u => new
            {
                u.FullName,
                u.UserName
            })
            .ToListAsync();

        var names = admins
            .Select(u => string.IsNullOrWhiteSpace(u.FullName)
                ? u.UserName
                : u.FullName.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        var options = new List<SelectListItem>
        {
            new SelectListItem
            {
                Value = "",
                Text = emptyText
            }
        };

        options.AddRange(names.Select(name => new SelectListItem
        {
            Value = name,
            Text = name
        }));

        return options;
    }

    private async Task<bool> IsValidHandedOverByAdminAsync(string? handedOverBy)
    {
        if (string.IsNullOrWhiteSpace(handedOverBy))
            return true;

        var value = handedOverBy.Trim();

        var admins = await _context.AppUsers
            .Where(u => u.IsActive && u.Role == AppUserRole.Admin)
            .Select(u => new
            {
                u.FullName,
                u.UserName
            })
            .ToListAsync();

        return admins.Any(u =>
        {
            var displayName = string.IsNullOrWhiteSpace(u.FullName)
                ? u.UserName
                : u.FullName.Trim();

            return string.Equals(
                displayName,
                value,
                StringComparison.OrdinalIgnoreCase
            );
        });
    }

    private string? NormalizeNullableText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private bool RecordMatchesAllTokens(Equipment e, List<string> searchTokens)
    {
        if (searchTokens == null || searchTokens.Count == 0)
            return true;

        var fields = BuildSearchFields(e);

        foreach (var token in searchTokens)
        {
            var tokenMatched = fields.Any(field => field.Contains(token));
            if (!tokenMatched)
                return false;
        }

        return true;
    }

    private List<string> BuildSearchFields(Equipment e)
    {
        var fields = new List<string>();

        AddSearchVariants(fields, e.InventoryNumber);
        AddSearchVariants(fields, e.SerialNumber);
        AddSearchVariants(fields, e.Name);
        AddSearchVariants(fields, e.CurrentSite?.Name);
        AddSearchVariants(fields, e.CurrentSite?.Code);
        AddSearchVariants(fields, e.CurrentEmployee?.FullName);
        AddSearchVariants(fields, e.CurrentEmployee?.WorkerCode);
        AddSearchVariants(fields, NormalizeEquipmentTypeText(e.EquipmentType));
        AddSearchVariants(fields, NormalizeEquipmentStatusText(e.Status));

        if (e.AssignedAt.HasValue)
        {
            AddSearchVariants(fields, e.AssignedAt.Value.ToString("dd.MM.yyyy"));
            AddSearchVariants(fields, e.AssignedAt.Value.ToString("d.M.yyyy"));
            AddSearchVariants(fields, e.AssignedAt.Value.ToString("MM.yyyy"));
            AddSearchVariants(fields, e.AssignedAt.Value.ToString("M.yyyy"));
            AddSearchVariants(fields, e.AssignedAt.Value.ToString("yyyy"));
            AddSearchVariants(fields, e.AssignedAt.Value.ToString("dd/MM/yyyy"));
            AddSearchVariants(fields, e.AssignedAt.Value.ToString("d/M/yyyy"));
            AddSearchVariants(fields, e.AssignedAt.Value.ToString("yyyy-MM-dd"));
        }

        if (e.ReturnedAt.HasValue)
        {
            AddSearchVariants(fields, e.ReturnedAt.Value.ToString("dd.MM.yyyy"));
            AddSearchVariants(fields, e.ReturnedAt.Value.ToString("d.M.yyyy"));
            AddSearchVariants(fields, e.ReturnedAt.Value.ToString("MM.yyyy"));
            AddSearchVariants(fields, e.ReturnedAt.Value.ToString("M.yyyy"));
            AddSearchVariants(fields, e.ReturnedAt.Value.ToString("yyyy"));
            AddSearchVariants(fields, e.ReturnedAt.Value.ToString("dd/MM/yyyy"));
            AddSearchVariants(fields, e.ReturnedAt.Value.ToString("d/M/yyyy"));
            AddSearchVariants(fields, e.ReturnedAt.Value.ToString("yyyy-MM-dd"));
        }

        var combined = string.Join(" ", fields.Distinct());
        AddSearchVariants(fields, combined);

        return fields.Distinct().ToList();
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

    private bool SiteMatchesAutocomplete(Site site, List<string> tokens)
    {
        if (tokens == null || tokens.Count == 0)
            return true;

        var values = new List<string>();
        AddSearchVariants(values, site.Name);
        AddSearchVariants(values, site.Code);
        AddSearchVariants(values, site.Location);

        foreach (var token in tokens)
        {
            if (!values.Any(v => v.Contains(token)))
                return false;
        }

        return true;
    }

    private bool EmployeeMatchesAutocomplete(Employee employee, List<string> tokens)
    {
        if (tokens == null || tokens.Count == 0)
            return true;

        var values = new List<string>();
        AddSearchVariants(values, employee.FullName);
        AddSearchVariants(values, employee.WorkerCode);

        foreach (var token in tokens)
        {
            if (!values.Any(v => v.Contains(token)))
                return false;
        }

        return true;
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

    private string NormalizeEquipmentTypeText(EquipmentType type)
    {
        return type switch
        {
            EquipmentType.Laptop => "laptop lap",
            EquipmentType.PC => "desktop desktopracunalo desktop racunalo pc racunalo računalo kompjuter komp",
            EquipmentType.Monitor => "monitor ekran mon",
            EquipmentType.Printer => "printer print pisač pisac",
            EquipmentType.Tablet => "tablet tab",
            EquipmentType.Mobitel => "mobitel telefon tel mob",
            EquipmentType.Router => "router rout",
            EquipmentType.Switch => "switch sw",
            EquipmentType.Ostalo => "ostalo",
            _ => type.ToString().ToLower()
        };
    }

    private string NormalizeEquipmentStatusText(EquipmentStatus status)
    {
        return status switch
        {
            EquipmentStatus.Dostupno => "dostupno slobodno free",
            EquipmentStatus.Zaduzeno => "zaduzeno zaduženo zauzeto assigned",
            EquipmentStatus.Servis => "servis service popravak",
            EquipmentStatus.Otpisano => "otpisano rashodovano",
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
