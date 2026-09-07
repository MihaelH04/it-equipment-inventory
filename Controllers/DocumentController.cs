using System.Diagnostics;
using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ITEquipmentInventory.Data;
using ITEquipmentInventory.Configuration;
using ITEquipmentInventory.Models;
using ITEquipmentInventory.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ITEquipmentInventory.Controllers
{
    [Authorize(Roles = "Admin")]
public class DocumentController : Controller
    {
        private readonly AppDbContext _context;
        private readonly StoragePaths _storagePaths;
        private readonly ILogger<DocumentController> _logger;

        public DocumentController(
            AppDbContext context,
            StoragePaths storagePaths,
            ILogger<DocumentController> logger)
        {
            _context = context;
            _storagePaths = storagePaths;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Generate(int id)
        {
            var equipment = await _context.Equipment
                .Include(e => e.CurrentEmployee)
                .Include(e => e.CurrentSite)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (equipment == null)
                return NotFound();

            var handedOverByOptions = await LoadHandedOverByOptionsAsync();

            var model = new DocumentGenerateViewModel
            {
                EquipmentId = equipment.Id,
                Equipment = equipment,
                HandedOverByTechnician = ResolveDefaultHandedOverBy(
                    equipment.HandedOverBy,
                    handedOverByOptions
                ),
                AssignedAt = equipment.AssignedAt ?? DateTime.Now,
                RecipientFullName = equipment.CurrentEmployee?.FullName ?? string.Empty,
                JobTitle = string.Empty,
                CostCenterName = equipment.CurrentSite?.Name ?? string.Empty,
                SerialNumber = equipment.SerialNumber ?? string.Empty
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GeneratePdf(DocumentGenerateViewModel model)
        {
            var equipment = await _context.Equipment
                .Include(e => e.CurrentEmployee)
                .Include(e => e.CurrentSite)
                .FirstOrDefaultAsync(e => e.Id == model.EquipmentId);

            if (equipment == null)
                return NotFound("Oprema nije pronađena.");

            model.Equipment = equipment;
            var handedOverByOptions = await LoadHandedOverByOptionsAsync();

            model.HandedOverByTechnician = NormalizeNullableText(model.HandedOverByTechnician) ?? string.Empty;

            if (!handedOverByOptions.Any(x =>
                    !string.IsNullOrWhiteSpace(x.Value) &&
                    string.Equals(x.Value, model.HandedOverByTechnician, StringComparison.OrdinalIgnoreCase)))
            {
                ModelState.AddModelError(
                    nameof(model.HandedOverByTechnician),
                    "Predati može samo aktivni administrator iz korisnika."
                );

                return View("Generate", model);
            }

            var templatePath = GetTemplatePath(equipment.EquipmentType);
            if (string.IsNullOrWhiteSpace(templatePath) || !System.IO.File.Exists(templatePath))
            {
                _logger.LogError(
                    "Document template was not found for equipment {EquipmentId} ({EquipmentType}). Expected path: {TemplatePath}.",
                    equipment.Id,
                    equipment.EquipmentType,
                    templatePath);
                ModelState.AddModelError(string.Empty, "Predložak dokumenta nije pronađen za ovu vrstu opreme.");
                return View("Generate", model);
            }

            var generatedFolder = GetGeneratedDocumentsFolder();

            var downloadBaseFileName = BuildAssignmentDocumentFileName(equipment);
            var tempBaseFileName = $"{downloadBaseFileName}_{Guid.NewGuid():N}";
            var tempDocxPath = Path.Combine(generatedFolder, tempBaseFileName + ".docx");

            try
            {
                System.IO.File.Copy(templatePath, tempDocxPath, true);

                using (var doc = WordprocessingDocument.Open(tempDocxPath, true))
                {
                    ReplaceText(doc, "{{DATUM}}", model.AssignedAt?.ToString("dd.MM.yyyy.") ?? "");
                    ReplaceText(doc, "{{PREDAO_INFORMATICAR}}", model.HandedOverByTechnician ?? "");
                    ReplaceText(doc, "{{PRIMIO_IME_PREZIME}}", model.RecipientFullName ?? "");
                    ReplaceText(doc, "{{NAZIV_RADNOG_MJESTA}}", model.JobTitle ?? "");
                    ReplaceText(doc, "{{NAZIV_MJESTA_TROSKA}}", model.CostCenterName ?? "");

                    if (equipment.EquipmentType == EquipmentType.PC || equipment.EquipmentType == EquipmentType.Laptop)
                    {
                        ReplaceText(doc, "{{MODEL_RACUNALA}}", equipment.Name ?? "");
                        ReplaceText(doc, "{{SERIJSKI_BROJ_RACUNALA}}", equipment.SerialNumber ?? "");
                        ReplaceText(doc, "{{BROJ_OSNOVNOG_SREDSTVA}}", model.AssetNumber ?? "");
                        ReplaceText(doc, "{{PRINTER_ILI_DR}}", model.PrinterOrOther ?? "");
                        ReplaceText(doc, "{{MICROSOFT_WINDOWS}}", model.MicrosoftWindows ?? "");
                        ReplaceText(doc, "{{MICROSOFT_OFFICE}}", model.MicrosoftOffice ?? "");
                        ReplaceText(doc, "{{ANTIVIRUSNI_PROGRAM}}", model.AntivirusProgram ?? "");
                        ReplaceText(doc, "{{OSTALI_PROGRAMI}}", model.OtherPrograms ?? "");
                    }
                    else if (equipment.EquipmentType == EquipmentType.Monitor)
                    {
                        ReplaceText(doc, "{{MODEL_MONITORA}}", equipment.Name ?? "");
                        ReplaceText(doc, "{{SERIJSKI_BROJ_MONITORA}}", equipment.SerialNumber ?? "");
                        ReplaceText(doc, "{{BROJ_OSNOVNOG_SREDSTVA}}", model.AssetNumber ?? "");
                    }
                    else if (equipment.EquipmentType == EquipmentType.Tablet)
                    {
                        ReplaceText(doc, "{{MODEL_TABLETA}}", equipment.Name ?? "");
                        ReplaceText(doc, "{{BROJ_OSNOVNOG_SREDSTVA}}", model.AssetNumber ?? "");
                        ReplaceText(doc, "{{SN}}", string.IsNullOrWhiteSpace(equipment.SerialNumber) ? (model.SerialNumber ?? "") : equipment.SerialNumber);
                        ReplaceText(doc, "{{DODATNA_OPREMA}}", model.AdditionalEquipment ?? "");
                    }
                    else if (equipment.EquipmentType == EquipmentType.Mobitel)
                    {
                        ReplaceText(doc, "{{MODEL_UREDAJA}}", equipment.Name ?? "");
                        ReplaceText(doc, "{{IMEI}}", model.Imei ?? "");
                        ReplaceText(doc, "{{SN}}", string.IsNullOrWhiteSpace(equipment.SerialNumber) ? (model.SerialNumber ?? "") : equipment.SerialNumber);
                        ReplaceText(doc, "{{TARIFA_I_BROJ_MOB}}", model.MobilePlanAndNumber ?? "");
                    }

                    doc.MainDocumentPart?.Document?.Save();
                }

                if (!System.IO.File.Exists(tempDocxPath) || new FileInfo(tempDocxPath).Length == 0)
                {
                    _logger.LogError("Generated DOCX is missing or empty for equipment {EquipmentId}.", equipment.Id);
                    ModelState.AddModelError(string.Empty, "Dokument nije moguće generirati. Provjerite predložak dokumenta.");
                    return View("Generate", model);
                }

                var finalPdfPath = await ConvertDocxToPdfWithLibreOfficeAsync(tempDocxPath, generatedFolder);

                if (!System.IO.File.Exists(finalPdfPath) || new FileInfo(finalPdfPath).Length == 0)
                {
                    _logger.LogError("Generated PDF is missing or empty for equipment {EquipmentId}.", equipment.Id);
                    ModelState.AddModelError(string.Empty,
                        "PDF nije moguće generirati. Provjerite instalaciju LibreOfficea i konfiguraciju dokument predložaka.");
                    return View("Generate", model);
                }

                var bytes = await System.IO.File.ReadAllBytesAsync(finalPdfPath);
                var downloadName = downloadBaseFileName + ".pdf";

                TryDeleteFile(tempDocxPath);
                TryDeleteFile(finalPdfPath);

                return File(bytes, "application/pdf", downloadName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PDF generation failed for equipment {EquipmentId}.", equipment.Id);
                ModelState.AddModelError(string.Empty,
                    "PDF nije moguće generirati. Provjerite instalaciju LibreOfficea i konfiguraciju dokument predložaka.");
                TryDeleteFile(tempDocxPath);
                return View("Generate", model);
            }
        }

        private async Task<List<SelectListItem>> LoadHandedOverByOptionsAsync()
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
                    Text = "-- Odaberi admina --"
                }
            };

            options.AddRange(names.Select(name => new SelectListItem
            {
                Value = name,
                Text = name
            }));

            ViewBag.HandedOverByOptions = options;

            return options;
        }

        private string ResolveDefaultHandedOverBy(
            string? currentValue,
            List<SelectListItem> handedOverByOptions)
        {
            var validValues = handedOverByOptions
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .Select(x => x.Value!)
                .ToList();

            if (!string.IsNullOrWhiteSpace(currentValue) &&
                validValues.Any(x => string.Equals(
                    x,
                    currentValue.Trim(),
                    StringComparison.OrdinalIgnoreCase)))
            {
                return currentValue.Trim();
            }

            return validValues.FirstOrDefault() ?? string.Empty;
        }

        private string? NormalizeNullableText(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }

        private void ReplaceText(WordprocessingDocument doc, string placeholder, string value)
        {
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null)
                return;

            foreach (var text in body.Descendants<Text>())
            {
                if (text.Text.Contains(placeholder))
                {
                    text.Text = text.Text.Replace(placeholder, value ?? string.Empty);
                }
            }
        }

        private async Task<string> ConvertDocxToPdfWithLibreOfficeAsync(string docxPath, string outDir)
        {
            if (!System.IO.File.Exists(docxPath) || new FileInfo(docxPath).Length == 0)
                throw new InvalidOperationException("DOCX za konverziju ne postoji ili je prazan.");

            Directory.CreateDirectory(outDir);

            var soffice = ResolveLibreOfficePath();

            if (string.IsNullOrWhiteSpace(soffice) || !System.IO.File.Exists(soffice))
            {
                _logger.LogError(
                    "LibreOffice executable was not found. Configured value: {ConfiguredExecutable}.",
                    _storagePaths.LibreOfficeExecutable);
                throw new InvalidOperationException("LibreOffice executable nije pronađen.");
            }

            var fullDocxPath = Path.GetFullPath(docxPath);
            var fullOutDir = Path.GetFullPath(outDir);
            var expectedPdfPath = Path.Combine(
                fullOutDir,
                Path.GetFileNameWithoutExtension(fullDocxPath) + ".pdf");

            if (System.IO.File.Exists(expectedPdfPath))
                System.IO.File.Delete(expectedPdfPath);

            var psi = new ProcessStartInfo
            {
                FileName = soffice,
                WorkingDirectory = fullOutDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Directory.CreateDirectory(_storagePaths.LibreOfficeProfilePath);
            var profileUri = new Uri(
                Path.EndsInDirectorySeparator(_storagePaths.LibreOfficeProfilePath)
                    ? _storagePaths.LibreOfficeProfilePath
                    : _storagePaths.LibreOfficeProfilePath + Path.DirectorySeparatorChar).AbsoluteUri;
            psi.ArgumentList.Add($"-env:UserInstallation={profileUri}");

            psi.ArgumentList.Add("--headless");
            psi.ArgumentList.Add("--nologo");
            psi.ArgumentList.Add("--nofirststartwizard");
            psi.ArgumentList.Add("--convert-to");
            psi.ArgumentList.Add("pdf");
            psi.ArgumentList.Add("--outdir");
            psi.ArgumentList.Add(fullOutDir);
            psi.ArgumentList.Add(fullDocxPath);

            _logger.LogInformation(
                "Starting LibreOffice PDF conversion. Executable: {Executable}; Input: {InputDocx}; Output directory: {OutputDirectory}.",
                soffice,
                fullDocxPath,
                fullOutDir);

            using var process = Process.Start(psi);

            if (process == null)
                throw new InvalidOperationException("LibreOffice proces nije mogao biti pokrenut.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var exited = process.WaitForExit(60000);

            if (!exited)
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                }

                await Task.WhenAll(stdoutTask, stderrTask);
                throw new TimeoutException("LibreOffice konverzija je istekla nakon 60 sekundi.");
            }

            await Task.WhenAll(stdoutTask, stderrTask);
            var stdout = stdoutTask.Result;
            var stderr = stderrTask.Result;

            _logger.LogInformation(
                "LibreOffice PDF conversion ended. Exit code: {ExitCode}; STDOUT: {Stdout}; STDERR: {Stderr}.",
                process.ExitCode,
                stdout,
                stderr);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"LibreOffice returned exit code {process.ExitCode}.");
            }

            for (var i = 0; i < 20; i++)
            {
                if (System.IO.File.Exists(expectedPdfPath) && new FileInfo(expectedPdfPath).Length > 0)
                    return expectedPdfPath;

                Thread.Sleep(300);
            }

            throw new InvalidOperationException("LibreOffice nije stvorio PDF datoteku.");
        }

        private string? ResolveLibreOfficePath()
        {
            var configuredPath = _storagePaths.LibreOfficeExecutable;
            if (Path.IsPathFullyQualified(configuredPath) && System.IO.File.Exists(configuredPath))
                return Path.GetFullPath(configuredPath);

            if (!string.IsNullOrWhiteSpace(configuredPath) &&
                !string.Equals(configuredPath, "soffice", StringComparison.OrdinalIgnoreCase))
            {
                var contentRootCandidate = _storagePaths.ResolveFromContentRoot(configuredPath);
                if (System.IO.File.Exists(contentRootCandidate))
                    return contentRootCandidate;
            }

            var pathExecutable = FindExecutableOnPath(configuredPath);
            if (pathExecutable != null)
                return pathExecutable;

            if (OperatingSystem.IsWindows())
            {
                var standardLocations = new[]
                {
                    @"C:\Program Files\LibreOffice\program\soffice.exe",
                    @"C:\Program Files (x86)\LibreOffice\program\soffice.exe"
                };

                return standardLocations.FirstOrDefault(System.IO.File.Exists);
            }

            return configuredPath;
        }

        private static string? FindExecutableOnPath(string executableName)
        {
            if (string.IsNullOrWhiteSpace(executableName) ||
                executableName.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                executableName.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                return null;
            }

            var fileNames = OperatingSystem.IsWindows() && !executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? new[] { executableName + ".exe", executableName }
                : new[] { executableName };

            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                         .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                foreach (var fileName in fileNames)
                {
                    var candidate = Path.Combine(directory, fileName);
                    if (System.IO.File.Exists(candidate))
                        return candidate;
                }
            }

            return null;
        }

        private string GetGeneratedDocumentsFolder()
        {
            Directory.CreateDirectory(_storagePaths.GeneratedDocumentsPath);
            return _storagePaths.GeneratedDocumentsPath;
        }

        private string GetTemplatePath(EquipmentType type)
        {
            var folder = _storagePaths.TemplatesPath;

            return type switch
            {
                EquipmentType.PC => Path.Combine(folder, "PCTemplate-3.docx"),
                EquipmentType.Laptop => Path.Combine(folder, "LaptopTemplate.docx"),
                EquipmentType.Monitor => Path.Combine(folder, "MonitorTemplate-2.docx"),
                EquipmentType.Tablet => Path.Combine(folder, "TabletTemplate-4.docx"),
                EquipmentType.Mobitel => Path.Combine(folder, "MobitelTemplate.docx"),
                _ => string.Empty
            };
        }


        private string BuildAssignmentDocumentFileName(Equipment equipment)
        {
            var inventoryNumber = SafeFileNamePart(equipment.InventoryNumber, "bez_inventure");
            var employeeName = SafeFileNamePart(equipment.CurrentEmployee?.FullName, "bez_zaposlenika");
            var workerCode = SafeFileNamePart(equipment.CurrentEmployee?.WorkerCode, "bez_sifre_radnika");

            return $"zaduzenje_{inventoryNumber}_{employeeName}_{workerCode}";
        }

        private string SafeFileNamePart(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                value = fallback;

            var normalized = RemoveDiacritics(value.Trim().ToLower(new CultureInfo("hr-HR")));
            var builder = new StringBuilder();
            var previousWasSeparator = false;

            foreach (var ch in normalized)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                    previousWasSeparator = false;
                }
                else if (!previousWasSeparator)
                {
                    builder.Append('_');
                    previousWasSeparator = true;
                }
            }

            var cleaned = builder.ToString().Trim('_');
            return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
        }

        private string RemoveDiacritics(string text)
        {
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

        private void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
