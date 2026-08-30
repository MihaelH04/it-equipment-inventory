using System.ComponentModel.DataAnnotations;
using ITEquipmentInventory.Models;

namespace ITEquipmentInventory.ViewModels
{
    public class DocumentGenerateViewModel
    {
        public int EquipmentId { get; set; }

        public Equipment? Equipment { get; set; }

        [Display(Name = "Predao informatičar")]
        public string? HandedOverByTechnician { get; set; }

        [Display(Name = "Datum zaduženja")]
        public DateTime? AssignedAt { get; set; }

        [Display(Name = "Primio ime i prezime")]
        public string? RecipientFullName { get; set; }

        [Display(Name = "Naziv radnog mjesta")]
        public string? JobTitle { get; set; }

        [Display(Name = "Naziv mjesta troška")]
        public string? CostCenterName { get; set; }

        [Display(Name = "Broj osnovnog sredstva")]
        public string? AssetNumber { get; set; }

        [Display(Name = "Printer ili dr.")]
        public string? PrinterOrOther { get; set; }

        [Display(Name = "Microsoft Windows")]
        public string? MicrosoftWindows { get; set; }

        [Display(Name = "Microsoft Office")]
        public string? MicrosoftOffice { get; set; }

        [Display(Name = "Antivirusni program")]
        public string? AntivirusProgram { get; set; }

        [Display(Name = "Ostali programi")]
        public string? OtherPrograms { get; set; }

        [Display(Name = "Dodatna oprema")]
        public string? AdditionalEquipment { get; set; }

        [Display(Name = "IMEI")]
        public string? Imei { get; set; }

        [Display(Name = "Tarifa i broj mob.")]
        public string? MobilePlanAndNumber { get; set; }

        [Display(Name = "SN")]
        public string? SerialNumber { get; set; }
    }
}
