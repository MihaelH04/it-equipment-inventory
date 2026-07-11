using ITEquipmentInventory.Data;
using ITEquipmentInventory.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using System.Globalization;

namespace ITEquipmentInventory.Controllers
{
    [Authorize(Roles = "Admin")]
public class ImportController : Controller
    {
        private const long MaxExcelUploadBytes = 10 * 1024 * 1024;
        private readonly string[] _allowedExcelExtensions = { ".xlsx" };

        private readonly AppDbContext _context;

        public ImportController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult ImportSites()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportSites(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Nisi odabrao datoteku.";
                return View();
            }

            var validationError = ValidateExcelFile(file);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                TempData["Error"] = validationError;
                return View();
            }

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            var uvezeno = 0;
            var azurirano = 0;
            var preskoceno = 0;
            var greske = new List<string>();

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            using var package = new ExcelPackage(stream);

            if (package.Workbook.Worksheets.Count == 0)
            {
                TempData["Error"] = "Excel file ne sadrži nijedan list.";
                return View();
            }

            var ws = package.Workbook.Worksheets[0];

            if (ws == null || ws.Dimension == null)
            {
                TempData["Error"] = "Excel file je prazan ili neispravan.";
                return View();
            }

            var rowCount = ws.Dimension.Rows;
            var importedSet = new HashSet<string>();

            var allSites = await _context.Sites.ToListAsync();

            for (int row = 2; row <= rowCount; row++)
            {
                var sifra = ws.Cells[row, 1].Text.Trim();
                var naziv = ToTitleCaseHr(ws.Cells[row, 2].Text.Trim());
                var lokacija = ToTitleCaseHr(ws.Cells[row, 3].Text.Trim());
                var statusTxt = ws.Cells[row, 4].Text.Trim();

                if (string.IsNullOrWhiteSpace(sifra) && string.IsNullOrWhiteSpace(naziv))
                    continue;

                if (string.IsNullOrWhiteSpace(sifra))
                {
                    greske.Add($"Redak {row}: nedostaje Šifra.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(naziv))
                {
                    greske.Add($"Redak {row}: nedostaje Naziv.");
                    continue;
                }

                var normalizedCode = NormalizeText(sifra);

                if (importedSet.Contains(normalizedCode))
                {
                    preskoceno++;
                    continue;
                }

                var status = statusTxt.ToLower() switch
                {
                    "neaktivno" => SiteStatus.Neaktivno,
                    _ => SiteStatus.Aktivno
                };

                var existingSite = allSites.FirstOrDefault(s => NormalizeText(s.Code) == normalizedCode);

                if (existingSite != null)
                {
                    existingSite.Name = naziv;
                    existingSite.Location = lokacija;
                    existingSite.Status = status;
                    azurirano++;
                }
                else
                {
                    var site = new Site
                    {
                        Code = sifra,
                        Name = naziv,
                        Location = lokacija,
                        Status = status
                    };

                    _context.Sites.Add(site);
                    allSites.Add(site);
                    uvezeno++;
                }

                importedSet.Add(normalizedCode);
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = $"Uvezeno: {uvezeno}, Ažurirano: {azurirano}, Preskočeno: {preskoceno}.";
            if (greske.Any())
                TempData["Greske"] = string.Join(" | ", greske);

            return RedirectToAction(nameof(ImportSites));
        }

        [HttpGet]
        public IActionResult ImportEmployees()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportEmployees(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Nisi odabrao datoteku.";
                return View();
            }

            var validationError = ValidateExcelFile(file);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                TempData["Error"] = validationError;
                return View();
            }

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            var uvezeno = 0;
            var azurirano = 0;
            var preskoceno = 0;
            var greske = new List<string>();

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            using var package = new ExcelPackage(stream);

            if (package.Workbook.Worksheets.Count == 0)
            {
                TempData["Error"] = "Excel file ne sadrži nijedan list.";
                return View();
            }

            var ws = package.Workbook.Worksheets[0];

            if (ws == null || ws.Dimension == null)
            {
                TempData["Error"] = "Excel file je prazan ili neispravan.";
                return View();
            }

            var rowCount = ws.Dimension.Rows;
            var importedSet = new HashSet<string>();

            var allSites = await _context.Sites.ToListAsync();
            var allEmployees = await _context.Employees.ToListAsync();

            for (int row = 2; row <= rowCount; row++)
            {
                var sifra = ws.Cells[row, 1].Text.Trim();
                var imePrezime = ToTitleCaseHr(ws.Cells[row, 2].Text.Trim());
                var radniNalog = ws.Cells[row, 3].Text.Trim();
                var statusTxt = ws.Cells[row, 4].Text.Trim();

                if (string.IsNullOrWhiteSpace(sifra) && string.IsNullOrWhiteSpace(imePrezime))
                    continue;

                if (string.IsNullOrWhiteSpace(sifra))
                {
                    greske.Add($"Redak {row}: nedostaje Šifra radnika.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(imePrezime))
                {
                    greske.Add($"Redak {row}: nedostaje Ime i prezime.");
                    continue;
                }

                var normalizedCode = NormalizeText(sifra);

                if (importedSet.Contains(normalizedCode))
                {
                    preskoceno++;
                    continue;
                }

                var status = statusTxt.ToLower() switch
                {
                    "neaktivan" => EmployeeStatus.Neaktivan,
                    _ => EmployeeStatus.Aktivan
                };

                Site? site = null;
                if (!string.IsNullOrWhiteSpace(radniNalog))
                {
                    var normalizedSite = NormalizeText(radniNalog);

                    site = allSites.FirstOrDefault(s =>
                        NormalizeText(s.Name) == normalizedSite ||
                        NormalizeText(s.Code) == normalizedSite);
                }

                var existingEmployee = allEmployees.FirstOrDefault(e => NormalizeText(e.WorkerCode) == normalizedCode);

                if (existingEmployee != null)
                {
                    existingEmployee.FullName = imePrezime;
                    existingEmployee.SiteId = site?.Id;
                    existingEmployee.Status = status;
                    azurirano++;
                }
                else
                {
                    var employee = new Employee
                    {
                        WorkerCode = sifra,
                        FullName = imePrezime,
                        SiteId = site?.Id,
                        Status = status
                    };

                    _context.Employees.Add(employee);
                    allEmployees.Add(employee);
                    uvezeno++;
                }

                importedSet.Add(normalizedCode);
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = $"Uvezeno: {uvezeno}, Ažurirano: {azurirano}, Preskočeno: {preskoceno}.";
            if (greske.Any())
                TempData["Greske"] = string.Join(" | ", greske);

            return RedirectToAction(nameof(ImportEmployees));
        }

        [HttpGet]
        public IActionResult ImportEquipment()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportEquipment(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Nisi odabrao datoteku.";
                return View();
            }

            var validationError = ValidateExcelFile(file);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                TempData["Error"] = validationError;
                return View();
            }

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            var uvezeno = 0;
            var azurirano = 0;
            var preskoceno = 0;
            var greske = new List<string>();

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            using var package = new ExcelPackage(stream);

            if (package.Workbook.Worksheets.Count == 0)
            {
                TempData["Error"] = "Excel file ne sadrži nijedan list.";
                return View();
            }

            var ws = package.Workbook.Worksheets[0];

            if (ws == null || ws.Dimension == null)
            {
                TempData["Error"] = "Excel file je prazan ili neispravan.";
                return View();
            }

            var rowCount = ws.Dimension.Rows;
            var importedSet = new HashSet<string>();

            var allSites = await _context.Sites.ToListAsync();
            var allEmployees = await _context.Employees.ToListAsync();
            var allEquipment = await _context.Equipment.ToListAsync();

            for (int row = 2; row <= rowCount; row++)
            {
                var inventoryNumber = ws.Cells[row, 1].Text.Trim();
                var serialNumber = ws.Cells[row, 2].Text.Trim();
                var typeTxt = ws.Cells[row, 3].Text.Trim();
                var name = ToTitleCaseHr(ws.Cells[row, 4].Text.Trim());
                var statusTxt = ws.Cells[row, 5].Text.Trim();
                var siteTxt = ws.Cells[row, 6].Text.Trim();
                var employeeTxt = ws.Cells[row, 7].Text.Trim();
                var assignedAtTxt = ws.Cells[row, 8].Text.Trim();
                var handedOverBy = ToTitleCaseHr(ws.Cells[row, 9].Text.Trim());

                if (string.IsNullOrWhiteSpace(inventoryNumber) && string.IsNullOrWhiteSpace(name))
                    continue;

                if (string.IsNullOrWhiteSpace(inventoryNumber))
                {
                    greske.Add($"Redak {row}: nedostaje Inventurni broj.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    greske.Add($"Redak {row}: nedostaje Naziv.");
                    continue;
                }

                var normalizedInventory = NormalizeText(inventoryNumber);

                if (importedSet.Contains(normalizedInventory))
                {
                    preskoceno++;
                    continue;
                }

                var equipmentType = MapEquipmentType(typeTxt);
                var status = MapEquipmentStatus(statusTxt);

                Site? site = null;
                if (!string.IsNullOrWhiteSpace(siteTxt))
                {
                    var normalizedSite = NormalizeText(siteTxt);

                    site = allSites.FirstOrDefault(s =>
                        NormalizeText(s.Name) == normalizedSite ||
                        NormalizeText(s.Code) == normalizedSite);
                }

                Employee? employee = null;
                if (!string.IsNullOrWhiteSpace(employeeTxt))
                {
                    var normalizedEmployee = NormalizeText(employeeTxt);

                    employee = allEmployees.FirstOrDefault(e =>
                        NormalizeText(e.FullName) == normalizedEmployee ||
                        NormalizeText(e.WorkerCode) == normalizedEmployee);
                }

                DateTime? assignedAt = ParseExcelDate(assignedAtTxt);

                var existingEquipment = allEquipment.FirstOrDefault(e =>
                    NormalizeText(e.InventoryNumber ?? string.Empty) == normalizedInventory);

                if (existingEquipment != null)
                {
                    existingEquipment.SerialNumber = serialNumber;
                    existingEquipment.EquipmentType = equipmentType;
                    existingEquipment.Name = name;
                    existingEquipment.Status = status;
                    existingEquipment.CurrentSiteId = site?.Id;
                    existingEquipment.CurrentEmployeeId = employee?.Id;
                    existingEquipment.AssignedAt = assignedAt;
                    existingEquipment.HandedOverBy = string.IsNullOrWhiteSpace(handedOverBy) ? null : handedOverBy;

                    azurirano++;
                }
                else
                {
                    var equipment = new Equipment
                    {
                        InventoryNumber = inventoryNumber,
                        SerialNumber = serialNumber,
                        EquipmentType = equipmentType,
                        Name = name,
                        Status = status,
                        CurrentSiteId = site?.Id,
                        CurrentEmployeeId = employee?.Id,
                        AssignedAt = assignedAt,
                        HandedOverBy = string.IsNullOrWhiteSpace(handedOverBy) ? null : handedOverBy
                    };

                    _context.Equipment.Add(equipment);
                    allEquipment.Add(equipment);
                    uvezeno++;
                }

                importedSet.Add(normalizedInventory);
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = $"Uvezeno: {uvezeno}, Ažurirano: {azurirano}, Preskočeno: {preskoceno}.";
            if (greske.Any())
                TempData["Greske"] = string.Join(" | ", greske);

            return RedirectToAction(nameof(ImportEquipment));
        }

        private string? ValidateExcelFile(IFormFile file)
        {
            if (file.Length > MaxExcelUploadBytes)
                return "Excel datoteka je prevelika. Maksimalno je dopušteno 10 MB.";

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!_allowedExcelExtensions.Contains(extension))
                return "Dozvoljen je samo .xlsx Excel format.";

            return null;
        }

        private EquipmentType MapEquipmentType(string input)
        {
            var value = NormalizeEnumText(input);

            return value switch
            {
                "laptop" => EquipmentType.Laptop,
                "desktop" => EquipmentType.PC,
                "PC" => EquipmentType.PC,
                "desktop racunalo" => EquipmentType.PC,
                "desktopračunalo" => EquipmentType.PC,
                "desktop računalo" => EquipmentType.PC,
                "pc" => EquipmentType.PC,
                "monitor" => EquipmentType.Monitor,
                "printer" => EquipmentType.Printer,
                "tablet" => EquipmentType.Tablet,
                "mobitel" => EquipmentType.Mobitel,
                "router" => EquipmentType.Router,
                "switch" => EquipmentType.Switch,
                _ => EquipmentType.Ostalo
            };
        }

        private EquipmentStatus MapEquipmentStatus(string input)
        {
            var value = NormalizeEnumText(input);

            return value switch
            {
                "zaduzeno" => EquipmentStatus.Zaduzeno,
                "zaduženo" => EquipmentStatus.Zaduzeno,
                "servis" => EquipmentStatus.Servis,
                "otpisano" => EquipmentStatus.Otpisano,
                _ => EquipmentStatus.Dostupno
            };
        }

        private string ToTitleCaseHr(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var culture = new CultureInfo("hr-HR");

            var cleaned = string.Join(" ",
                input.Trim()
                     .ToLower(culture)
                     .Split(' ', StringSplitOptions.RemoveEmptyEntries));

            return culture.TextInfo.ToTitleCase(cleaned);
        }

        private string NormalizeText(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            return string.Join(" ",
                input.Trim()
                     .ToLower(new CultureInfo("hr-HR"))
                     .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private string NormalizeEnumText(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var normalized = input.Trim().ToLower(new CultureInfo("hr-HR"));
            normalized = normalized.Replace("_", " ");
            normalized = string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));

            return normalized;
        }

        private DateTime? ParseExcelDate(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            var culture = new CultureInfo("hr-HR");
            var formats = new[]
            {
                "dd.MM.yyyy",
                "d.M.yyyy",
                "dd.MM.yyyy HH:mm",
                "d.M.yyyy H:mm",
                "dd.MM.yyyy H:mm",
                "d.M.yyyy HH:mm"
            };

            if (DateTime.TryParseExact(input.Trim(), formats, culture, DateTimeStyles.None, out var parsed))
                return parsed;

            if (DateTime.TryParse(input, culture, DateTimeStyles.None, out parsed))
                return parsed;

            return null;
        }
    }
}