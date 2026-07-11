using OfficeOpenXml;
using OfficeOpenXml.Table;

namespace ITEquipmentInventory.Services;

public static class ExcelExportHelper
{
    public static byte[] CreateExcel(string worksheetName, string[] headers, IEnumerable<object?[]> rows)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add(SafeWorksheetName(worksheetName));

        for (int i = 0; i < headers.Length; i++)
        {
            worksheet.Cells[1, i + 1].Value = headers[i];
            worksheet.Cells[1, i + 1].Style.Font.Bold = true;
        }

        var rowIndex = 2;
        foreach (var row in rows)
        {
            for (int colIndex = 0; colIndex < headers.Length; colIndex++)
            {
                worksheet.Cells[rowIndex, colIndex + 1].Value = colIndex < row.Length ? row[colIndex] : null;
            }
            rowIndex++;
        }

        var lastRow = Math.Max(rowIndex - 1, 2);
        var range = worksheet.Cells[1, 1, lastRow, headers.Length];
        var table = worksheet.Tables.Add(range, "Tablica" + DateTime.Now.Ticks);
        table.TableStyle = TableStyles.Medium2;

        worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
        worksheet.View.FreezePanes(2, 1);

        return package.GetAsByteArray();
    }

    public static string FileName(string name)
    {
        return $"{name}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
    }

    private static string SafeWorksheetName(string name)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "Izvoz" : cleaned[..Math.Min(cleaned.Length, 31)];
    }
}
