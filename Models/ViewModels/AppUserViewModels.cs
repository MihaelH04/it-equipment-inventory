using System.ComponentModel.DataAnnotations;
using ITEquipmentInventory.Models;

namespace ITEquipmentInventory.Models.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Unesi korisničko ime.")]
    [StringLength(50)]
    [Display(Name = "Korisničko ime")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Unesi lozinku.")]
    [StringLength(100)]
    [DataType(DataType.Password)]
    [Display(Name = "Lozinka")]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

public class CreateAppUserViewModel
{
    [Required(ErrorMessage = "Unesi korisničko ime.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Korisničko ime mora imati između 3 i 50 znakova.")]
    [RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "Korisničko ime smije sadržavati samo slova, brojeve, točku, crticu i donju crtu.")]
    [Display(Name = "Korisničko ime")]
    public string UserName { get; set; } = string.Empty;

    [StringLength(120)]
    [Display(Name = "Ime i prezime")]
    public string? FullName { get; set; }

    [Required(ErrorMessage = "Unesi lozinku.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Lozinka mora imati barem 8 znakova.")]
    [DataType(DataType.Password)]
    [Display(Name = "Lozinka")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Uloga")]
    public AppUserRole Role { get; set; } = AppUserRole.User;

    [Display(Name = "Aktivan")]
    public bool IsActive { get; set; } = true;
}
