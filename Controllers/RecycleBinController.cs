using ITEquipmentInventory.Data;
using ITEquipmentInventory.Services;
using ITEquipmentInventory.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ITEquipmentInventory.Controllers;

[Authorize(Roles = "Admin")]
public class RecycleBinController : Controller
{
    private readonly AppDbContext _context;
    private readonly RecycleBinService _recycleBin;

    public RecycleBinController(AppDbContext context, RecycleBinService recycleBin)
    {
        _context = context;
        _recycleBin = recycleBin;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? searchString, int page = 1, int pageSize = PaginationConstants.DefaultPageSize)
    {
        pageSize = PaginationConstants.AllowedPageSizes.Contains(pageSize) ? pageSize : PaginationConstants.DefaultPageSize;
        await _recycleBin.PurgeExpiredAsync();

        var query = _context.DeletedItems.AsNoTracking()
            .Where(x => x.ExpiresAtUtc >= DateTime.UtcNow);

        if (!string.IsNullOrWhiteSpace(searchString))
        {
            var term = searchString.Trim();
            query = query.Where(x =>
                x.EntityLabel.Contains(term) ||
                x.DisplayName.Contains(term) ||
                (x.DeletedBy != null && x.DeletedBy.Contains(term)));
        }

        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(Math.Max(1, page), totalPages);
        ViewBag.SearchString = searchString;
        ViewBag.CurrentPage = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalPages = totalPages;
        ViewBag.FilteredCount = totalCount;
        var items = await query.OrderByDescending(x => x.DeletedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return View(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id)
    {
        var result = await _recycleBin.RestoreAsync(id);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePermanently(int id)
    {
        var item = await _context.DeletedItems.FindAsync(id);
        if (item == null)
        {
            TempData["Error"] = "Stavka nije pronađena.";
            return RedirectToAction(nameof(Index));
        }

        _context.DeletedItems.Remove(item);
        await _context.SaveChangesAsync();
        TempData["Success"] = "Stavka je trajno uklonjena iz koša.";
        return RedirectToAction(nameof(Index));
    }
}
