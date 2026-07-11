using System.Security.Claims;
using ITEquipmentInventory.Data;
using ITEquipmentInventory.Models;
using ITEquipmentInventory.Models.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ITEquipmentInventory.Controllers;

public class AccountController : Controller
{
    private readonly AppDbContext _context;
    private readonly string _profileImagesPath;
    private readonly PasswordHasher<AppUser> _passwordHasher = new();

    public AccountController(AppDbContext context, IWebHostEnvironment environment, IConfiguration configuration)
    {
        _context = context;
        _profileImagesPath = configuration["ResolvedProfileImagesPath"]
            ?? Path.Combine(environment.ContentRootPath, "data", "profile-images");
        Directory.CreateDirectory(_profileImagesPath);
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        if (!await _context.AppUsers.AnyAsync())
            return RedirectToAction(nameof(BootstrapAdmin));
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!await _context.AppUsers.AnyAsync())
            return RedirectToAction(nameof(BootstrapAdmin));
        if (!ModelState.IsValid)
            return View(model);

        var userName = model.UserName.Trim();
        var user = await _context.AppUsers.FirstOrDefaultAsync(u => u.UserName == userName);
        if (user == null || !user.IsActive ||
            _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, model.Password) == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(string.Empty, "Neispravno korisničko ime ili lozinka.");
            return View(model);
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        user.LastLoginIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _context.SaveChangesAsync();
        await SignInUserAsync(user);

        if (IsBirthdayToday(user.DateOfBirth))
            SetBirthdayGreetingCookie();

        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);
        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> BootstrapAdmin()
    {
        if (await _context.AppUsers.AnyAsync())
            return RedirectToAction(nameof(Login));
        return View(new CreateAppUserViewModel { Role = AppUserRole.Admin, IsActive = true });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BootstrapAdmin(CreateAppUserViewModel model)
    {
        if (await _context.AppUsers.AnyAsync())
            return RedirectToAction(nameof(Login));
        model.Role = AppUserRole.Admin;
        model.IsActive = true;
        if (!ModelState.IsValid)
            return View(model);

        var user = CreateUserFromModel(model);
        _context.AppUsers.Add(user);
        await _context.SaveChangesAsync();
        await SignInUserAsync(user);
        return RedirectToAction("Index", "Home");
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> Users()
    {
        var users = await _context.AppUsers.AsNoTracking()
            .OrderByDescending(u => u.Role == AppUserRole.Admin)
            .ThenBy(u => u.UserName).ToListAsync();
        return View(users);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public IActionResult Create() => View(new CreateAppUserViewModel { IsActive = true });

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateAppUserViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);
        var normalizedUserName = model.UserName.Trim();
        if (await _context.AppUsers.AnyAsync(u => u.UserName == normalizedUserName))
        {
            ModelState.AddModelError(nameof(model.UserName), "Korisničko ime već postoji.");
            return View(model);
        }

        _context.AppUsers.Add(CreateUserFromModel(model));
        await _context.SaveChangesAsync();
        TempData["Success"] = "Korisnik je uspješno dodan.";
        return RedirectToAction(nameof(Users));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
            return RedirectToAction(nameof(Login));

        return View(new ProfileViewModel
        {
            UserName = user.UserName,
            FullName = user.FullName,
            DateOfBirth = user.DateOfBirth,
            CurrentProfileImagePath = user.ProfileImagePath
        });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel model)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
            return RedirectToAction(nameof(Login));

        model.CurrentProfileImagePath = user.ProfileImagePath;
        if (_passwordHasher.VerifyHashedPassword(user, user.PasswordHash, model.CurrentPassword) == PasswordVerificationResult.Failed)
            ModelState.AddModelError(nameof(model.CurrentPassword), "Trenutna lozinka nije ispravna.");

        var normalizedUserName = model.UserName.Trim();
        if (await _context.AppUsers.AnyAsync(x => x.Id != user.Id && x.UserName == normalizedUserName))
            ModelState.AddModelError(nameof(model.UserName), "Korisničko ime već postoji.");

        if (model.DateOfBirth.HasValue && model.DateOfBirth.Value.Date > DateTime.Today)
            ModelState.AddModelError(nameof(model.DateOfBirth), "Datum rođenja ne može biti u budućnosti.");

        if (model.ProfileImage is { Length: > 0 })
        {
            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };
            var extension = Path.GetExtension(model.ProfileImage.FileName);
            if (!allowedExtensions.Contains(extension) || !model.ProfileImage.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                ModelState.AddModelError(nameof(model.ProfileImage), "Dopuštene su JPG, PNG i WEBP slike.");
            if (model.ProfileImage.Length > 2 * 1024 * 1024)
                ModelState.AddModelError(nameof(model.ProfileImage), "Profilna slika smije imati najviše 2 MB.");
        }

        if (!ModelState.IsValid)
            return View(model);

        var oldImagePath = user.ProfileImagePath;
        user.UserName = normalizedUserName;
        user.FullName = string.IsNullOrWhiteSpace(model.FullName) ? null : model.FullName.Trim();
        user.DateOfBirth = model.DateOfBirth?.Date;

        if (!string.IsNullOrWhiteSpace(model.NewPassword))
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, model.NewPassword);
            user.SecurityStamp = Guid.NewGuid().ToString("N");
        }

        if (model.RemoveProfileImage)
        {
            user.ProfileImagePath = null;
            DeleteProfileImage(oldImagePath);
        }
        else if (model.ProfileImage is { Length: > 0 })
        {
            user.ProfileImagePath = await SaveProfileImageAsync(model.ProfileImage);
            DeleteProfileImage(oldImagePath);
        }

        await _context.SaveChangesAsync();
        await SignInUserAsync(user);

        // Ako je korisnik upravo spremio današnji datum rođenja,
        // čestitka će se prikazati odmah nakon spremanja profila.
        if (IsBirthdayToday(user.DateOfBirth))
            SetBirthdayGreetingCookie();

        TempData["Success"] = "Profil je uspješno spremljen.";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var user = await _context.AppUsers.FindAsync(id);
        if (user == null) { TempData["Error"] = "Korisnik nije pronađen."; return RedirectToAction(nameof(Users)); }
        if (string.Equals(User.Identity?.Name, user.UserName, StringComparison.OrdinalIgnoreCase))
        { TempData["Error"] = "Ne možeš sam sebi promijeniti aktivnost."; return RedirectToAction(nameof(Users)); }

        if (user.Role == AppUserRole.Admin && user.IsActive &&
            await _context.AppUsers.CountAsync(u => u.Role == AppUserRole.Admin && u.IsActive) <= 1)
        { TempData["Error"] = "Ne možeš deaktivirati zadnjeg aktivnog admina."; return RedirectToAction(nameof(Users)); }

        user.IsActive = !user.IsActive;
        await _context.SaveChangesAsync();
        TempData["Success"] = user.IsActive ? $"Korisnik '{user.UserName}' je aktiviran." : $"Korisnik '{user.UserName}' je deaktiviran.";
        return RedirectToAction(nameof(Users));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await _context.AppUsers.FindAsync(id);
        if (user == null) { TempData["Error"] = "Korisnik nije pronađen."; return RedirectToAction(nameof(Users)); }
        if (string.Equals(User.Identity?.Name, user.UserName, StringComparison.OrdinalIgnoreCase))
        { TempData["Error"] = "Ne možeš obrisati sam sebe."; return RedirectToAction(nameof(Users)); }
        if (user.Role == AppUserRole.Admin && user.IsActive &&
            await _context.AppUsers.CountAsync(u => u.Role == AppUserRole.Admin && u.IsActive) <= 1)
        { TempData["Error"] = "Ne možeš obrisati zadnjeg aktivnog admina."; return RedirectToAction(nameof(Users)); }

        var name = user.UserName;
        DeleteProfileImage(user.ProfileImagePath);
        _context.AppUsers.Remove(user);
        await _context.SaveChangesAsync();
        TempData["Success"] = $"Korisnik '{name}' je obrisan.";
        return RedirectToAction(nameof(Users));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied() => View();

    private AppUser CreateUserFromModel(CreateAppUserViewModel model)
    {
        var user = new AppUser
        {
            UserName = model.UserName.Trim(),
            FullName = string.IsNullOrWhiteSpace(model.FullName) ? null : model.FullName.Trim(),
            Role = model.Role,
            IsActive = model.IsActive
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, model.Password);
        return user;
    }

    private async Task<AppUser?> GetCurrentUserAsync()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idClaim, out var id) ? await _context.AppUsers.FindAsync(id) : null;
    }

    private async Task SignInUserAsync(AppUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Role, user.Role.ToString())
        };
        if (!string.IsNullOrWhiteSpace(user.FullName)) claims.Add(new Claim("FullName", user.FullName));
        if (!string.IsNullOrWhiteSpace(user.ProfileImagePath)) claims.Add(new Claim("ProfileImagePath", user.ProfileImagePath));
        if (user.DateOfBirth.HasValue) claims.Add(new Claim("DateOfBirth", user.DateOfBirth.Value.ToString("yyyy-MM-dd")));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties
            {
                IsPersistent = false, AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(3), IssuedUtc = DateTimeOffset.UtcNow
            });
    }

    private async Task<string> SaveProfileImageAsync(IFormFile image)
    {
        var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
        var fileName = $"profile-{Guid.NewGuid():N}{extension}";
        await using var stream = System.IO.File.Create(Path.Combine(_profileImagesPath, fileName));
        await image.CopyToAsync(stream);
        return "/profile-images/" + fileName;
    }

    private void DeleteProfileImage(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || !relativePath.StartsWith("/profile-images/", StringComparison.OrdinalIgnoreCase))
            return;
        var fileName = Path.GetFileName(relativePath);
        if (string.IsNullOrWhiteSpace(fileName))
            return;
        var fullPath = Path.Combine(_profileImagesPath, fileName);
        if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
    }

    private void SetBirthdayGreetingCookie()
    {
        Response.Cookies.Append(
            "ITEquipmentInventory.BirthdayGreeting",
            "1",
            new CookieOptions
            {
                HttpOnly = false,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps,
                MaxAge = TimeSpan.FromMinutes(10),
                Path = "/"
            });
    }

    private static bool IsBirthdayToday(DateTime? dateOfBirth) =>
        dateOfBirth.HasValue &&
        dateOfBirth.Value.Month == DateTime.Today.Month &&
        dateOfBirth.Value.Day == DateTime.Today.Day;
}
