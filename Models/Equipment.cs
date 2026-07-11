using System.ComponentModel.DataAnnotations;

namespace ITEquipmentInventory.Models
{
    public class Equipment
    {
        public int Id { get; set; }

        [Display(Name = "Inventurni broj")]
        public string? InventoryNumber { get; set; }

        [Display(Name = "Naziv")]
        public string? Name { get; set; }

        [Display(Name = "Serijski broj")]
        public string? SerialNumber { get; set; }

        [Display(Name = "Vrsta opreme")]
        public EquipmentType EquipmentType { get; set; }

        [Display(Name = "Status")]
        public EquipmentStatus Status { get; set; }

        public int? CurrentEmployeeId { get; set; }

        [Display(Name = "Zaposlenik")]
        public Employee? CurrentEmployee { get; set; }

        public int? CurrentSiteId { get; set; }

        [Display(Name = "Radni nalog / lokacija")]
        public Site? CurrentSite { get; set; }

        [Display(Name = "Datum zaduženja")]
        public DateTime? AssignedAt { get; set; }

        [Display(Name = "Datum razduženja")]
        public DateTime? ReturnedAt { get; set; }

        [Display(Name = "Predao")]
        public string? HandedOverBy { get; set; }
    }
}
