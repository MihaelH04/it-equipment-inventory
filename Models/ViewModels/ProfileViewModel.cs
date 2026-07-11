using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ITEquipmentInventory.Models.ViewModels;

public class ProfileViewModel
{
    [Required(ErrorMessage = "Unesi korisničko ime.")]
    [StringLength(50, MinimumLength = 3)]
    [RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "Korisničko ime smije sadržavati samo slova, brojeve, točku, crticu i donju crtu.")]
    [Display(Name = "Korisničko ime")]
    public string UserName { get; set; } = string.Empty;

    [StringLength(120)]
    [Display(Name = "Ime i prezime")]
    public string? FullName { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Datum rođenja")]
    public DateTime? DateOfBirth { get; set; }

    [DataType(DataType.Password)]
    [Display(Name = "Trenutna lozinka")]
    [Required(ErrorMessage = "Za spremanje profila unesi trenutnu lozinku.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Nova lozinka mora imati barem 8 znakova.")]
    [Display(Name = "Nova lozinka")]
    public string? NewPassword { get; set; }

    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "Lozinke se ne podudaraju.")]
    [Display(Name = "Ponovi novu lozinku")]
    public string? ConfirmNewPassword { get; set; }

    [Display(Name = "Profilna slika")]
    public IFormFile? ProfileImage { get; set; }

    public string? CurrentProfileImagePath { get; set; }

    [Display(Name = "Ukloni trenutačnu profilnu sliku")]
    public bool RemoveProfileImage { get; set; }
}
