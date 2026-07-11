using System.ComponentModel.DataAnnotations;

namespace ITEquipmentInventory.Models;

public class AppUser : BaseEntity
{
    [Required]
    [StringLength(50)]
    [Display(Name = "Korisničko ime")]
    public string UserName { get; set; } = string.Empty;

    [StringLength(120)]
    [Display(Name = "Ime i prezime")]
    public string? FullName { get; set; }

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [StringLength(64)]
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    [Display(Name = "Uloga")]
    public AppUserRole Role { get; set; } = AppUserRole.User;

    [Display(Name = "Aktivan")]
    public bool IsActive { get; set; } = true;

    public int FailedLoginAttempts { get; set; }

    public DateTime? LockoutEndUtc { get; set; }

    public DateTime? LastLoginAtUtc { get; set; }

    public DateTime? LastFailedLoginAtUtc { get; set; }

    [StringLength(64)]
    public string? LastLoginIp { get; set; }

    [StringLength(64)]
    public string? LastFailedLoginIp { get; set; }


    [DataType(DataType.Date)]
    [Display(Name = "Datum rođenja")]
    public DateTime? DateOfBirth { get; set; }

    [StringLength(260)]
    [Display(Name = "Profilna slika")]
    public string? ProfileImagePath { get; set; }

    public ICollection<SecurityLog> SecurityLogs { get; set; } = new List<SecurityLog>();
}
