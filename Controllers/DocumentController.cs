using System.Diagnostics;
using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ITEquipmentInventory.Data;
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
        private readonly IWebHostEnvironment _environment;

        public DocumentController(AppDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
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
                PredaoInformatica = ResolveDefaultHandedOverBy(
                    equipment.HandedOverBy,
                    handedOverByOptions
                ),
                DatumZaduzenja = equipment.AssignedAt ?? DateTime.Now,
                PrimioImePrezime = equipment.CurrentEmployee?.FullName ?? string.Empty,
                NazivRadnogMjesta = string.Empty,
                NazivMjestaTroska = equipment.CurrentSite?.Name ?? string.Empty,
                SN = equipment.SerialNumber ?? string.Empty
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

            model.PredaoInformatica = NormalizeNullableText(model.PredaoInformatica) ?? string.Empty;

            if (!handedOverByOptions.Any(x =>
                    !string.IsNullOrWhiteSpace(x.Value) &&
                    string.Equals(x.Value, model.PredaoInformatica, StringComparison.OrdinalIgnoreCase)))
            {
                ModelState.AddModelError(
                    nameof(model.PredaoInformatica),
                    "Predati može samo aktivni administrator iz korisnika."
                );

                return View("Generate", model);
            }

            var templatePath = GetTemplatePath(equipment.EquipmentType);
            if (string.IsNullOrWhiteSpace(templatePath) || !System.IO.File.Exists(templatePath))
            {
                ModelState.AddModelError(string.Empty, $"Template nije pronađen: {templatePath}");
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
                    ReplaceText(doc, "{{DATUM}}", model.DatumZaduzenja?.ToString("dd.MM.yyyy.") ?? "");
                    ReplaceText(doc, "{{PREDAO_INFORMATICAR}}", model.PredaoInformatica ?? "");
                    ReplaceText(doc, "{{PRIMIO_IME_PREZIME}}", model.PrimioImePrezime ?? "");
                    ReplaceText(doc, "{{NAZIV_RADNOG_MJESTA}}", model.NazivRadnogMjesta ?? "");
                    ReplaceText(doc, "{{NAZIV_MJESTA_TROSKA}}", model.NazivMjestaTroska ?? "");

                    if (equipment.EquipmentType == EquipmentType.PC || equipment.EquipmentType == EquipmentType.Laptop)
                    {
                        ReplaceText(doc, "{{MODEL_RACUNALA}}", equipment.Name ?? "");
                        ReplaceText(doc, "{{SERIJSKI_BROJ_RACUNALA}}", equipment.SerialNumber ?? "");
                        ReplaceText(doc, "{{BROJ_OSNOVNOG_SREDSTVA}}", model.BrojOsnovnogSredstva ?? "");
                        ReplaceText(doc, "{{PRINTER_ILI_DR}}", model.PrinterIliDr ?? "");
                        ReplaceText(doc, "{{MICROSOFT_WINDOWS}}", model.MicrosoftWindows ?? "");
                        ReplaceText(doc, "{{MICROSOFT_OFFICE}}", model.MicrosoftOffice ?? "");
                        ReplaceText(doc, "{{ANTIVIRUSNI_PROGRAM}}", model.AntivirusniProgram ?? "");
                        ReplaceText(doc, "{{OSTALI_PROGRAMI}}", model.OstaliProgrami ?? "");
                    }
                    else if (equipment.EquipmentType == EquipmentType.Monitor)
                    {
                        ReplaceText(doc, "{{MODEL_MONITORA}}", equipment.Name ?? "");
                        ReplaceText(doc, "{{SERIJSKI_BROJ_MONITORA}}", equipment.SerialNumber ?? "");
                        ReplaceText(doc, "{{BROJ_OSNOVNOG_SREDSTVA}}", model.BrojOsnovnogSredstva ?? "");
                    }
                    else if (equipment.EquipmentType == EquipmentType.Tablet)
                    {
                        ReplaceText(doc, "{{MODEL_TABLETA}}", equipment.Name ?? "");
                        ReplaceText(doc, "{{BROJ_OSNOVNOG_SREDSTVA}}", model.BrojOsnovnogSredstva ?? "");
                        ReplaceText(doc, "{{SN}}", string.IsNullOrWhiteSpace(equipment.SerialNumber) ? (model.SN ?? "") : equipment.SerialNumber);
                        ReplaceText(doc, "{{DODATNA_OPREMA}}", model.DodatnaOprema ?? "");
                    }
                    else if (equipment.EquipmentType == EquipmentType.Mobitel)
                    {
                        ReplaceText(doc, "{{MODEL_UREDAJA}}", equipment.Name ?? "");
                        ReplaceText(doc, "{{IMEI}}", model.Imei ?? "");
                        ReplaceText(doc, "{{SN}}", string.IsNullOrWhiteSpace(equipment.SerialNumber) ? (model.SN ?? "") : equipment.SerialNumber);
                        ReplaceText(doc, "{{TARIFA_I_BROJ_MOB}}", model.TarifaIBrojMob ?? "");
                    }

                    doc.MainDocumentPart?.Document?.Save();
                }

                if (!System.IO.File.Exists(tempDocxPath))
                {
                    ModelState.AddModelError(string.Empty, "DOCX nije kreiran.");
                    return View("Generate", model);
                }

                var finalPdfPath = ConvertDocxToPdfWithLibreOffice(tempDocxPath, generatedFolder);

                if (!System.IO.File.Exists(finalPdfPath))
                {
                    ModelState.AddModelError(string.Empty, "PDF nije generiran.");
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
                ModelState.AddModelError(string.Empty, $"Greška kod generiranja PDF-a: {ex.Message}");
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

        private string ConvertDocxToPdfWithLibreOffice(string docxPath, string outDir)
        {
            if (!System.IO.File.Exists(docxPath))
                throw new Exception($"DOCX za konverziju ne postoji: {docxPath}");

            Directory.CreateDirectory(outDir);

            var soffice = ResolveLibreOfficePath();

            if (string.IsNullOrWhiteSpace(soffice))
            {
                throw new Exception(
                    "LibreOffice nije pronađen. Na Linuxu instaliraj: sudo apt install libreoffice libreoffice-writer");
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

            if (OperatingSystem.IsLinux())
            {
                var profileDir = "/srv/radnik/temp/libreoffice-profile";
                Directory.CreateDirectory(profileDir);

                var profileUri = new Uri(
                    profileDir.EndsWith("/") ? profileDir : profileDir + "/").AbsoluteUri;

                psi.ArgumentList.Add($"-env:UserInstallation={profileUri}");
            }

            psi.ArgumentList.Add("--headless");
            psi.ArgumentList.Add("--nologo");
            psi.ArgumentList.Add("--nofirststartwizard");
            psi.ArgumentList.Add("--convert-to");
            psi.ArgumentList.Add("pdf");
            psi.ArgumentList.Add("--outdir");
            psi.ArgumentList.Add(fullOutDir);
            psi.ArgumentList.Add(fullDocxPath);

            using var process = Process.Start(psi);

            if (process == null)
                throw new Exception("LibreOffice proces nije mogao biti pokrenut.");

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
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

                throw new Exception("LibreOffice konverzija je istekla nakon 60 sekundi.");
            }

            if (process.ExitCode != 0)
            {
                throw new Exception(
                    $"LibreOffice greška. ExitCode: {process.ExitCode}. STDERR: {stderr}. STDOUT: {stdout}");
            }

            for (var i = 0; i < 20; i++)
            {
                if (System.IO.File.Exists(expectedPdfPath))
                    return expectedPdfPath;

                Thread.Sleep(300);
            }

            throw new Exception(
                $"PDF nije stvoren. Očekivana putanja: {expectedPdfPath}. STDOUT: {stdout} STDERR: {stderr}");
        }

        private string? ResolveLibreOfficePath()
        {
            if (OperatingSystem.IsWindows())
            {
                var windowsPaths = new[]
                {
                    @"C:\Program Files\LibreOffice\program\soffice.exe",
                    @"C:\Program Files (x86)\LibreOffice\program\soffice.exe"
                };

                return windowsPaths.FirstOrDefault(System.IO.File.Exists);
            }

            if (OperatingSystem.IsLinux())
            {
                var linuxPaths = new[]
                {
                    "/usr/bin/libreoffice",
                    "/usr/bin/soffice",
                    "/snap/bin/libreoffice"
                };

                return linuxPaths.FirstOrDefault(System.IO.File.Exists);
            }

            return null;
        }

        private string GetGeneratedDocumentsFolder()
        {
            var folder = OperatingSystem.IsLinux() && Directory.Exists("/srv/radnik")
                ? "/srv/radnik/temp/GeneratedDocuments"
                : Path.Combine(_environment.ContentRootPath, "GeneratedDocuments");

            Directory.CreateDirectory(folder);
            return folder;
        }

        private string GetTemplatePath(EquipmentType type)
        {
            var folder = Path.Combine(_environment.ContentRootPath, "Templates");

            return type switch
            {
                EquipmentType.PC => Path.Combine(folder, "PCTemplate-3.docx"),
                EquipmentType.Laptop => Path.Combine(folder, "PCTemplate-3.docx"),
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