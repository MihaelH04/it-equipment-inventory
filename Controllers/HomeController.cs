using System.Diagnostics;
using ITEquipmentInventory.Data;
using ITEquipmentInventory.Models;
using ITEquipmentInventory.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ITEquipmentInventory.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _context;

    public HomeController(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var model = new DashboardViewModel
        {
            TotalEquipment = await _context.Equipment.CountAsync(),
            AssignedEquipment = await _context.Equipment.CountAsync(e => e.Status == EquipmentStatus.Zaduzeno),
            FreeEquipment = await _context.Equipment.CountAsync(e => e.Status == EquipmentStatus.Dostupno),
            EquipmentInService = await _context.Equipment.CountAsync(e => e.Status == EquipmentStatus.Servis),

            TotalEmployees = await _context.Employees.CountAsync(),
            ActiveEmployees = await _context.Employees.CountAsync(e => e.Status == EmployeeStatus.Aktivan),
            TotalSites = await _context.Sites.CountAsync(),
        };

        return View(model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}