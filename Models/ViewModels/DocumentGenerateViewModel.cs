using System.ComponentModel.DataAnnotations;
using ITEquipmentInventory.Models;

namespace ITEquipmentInventory.ViewModels
{
    public class DocumentGenerateViewModel
    {
        public int EquipmentId { get; set; }

        public Equipment? Equipment { get; set; }

        [Display(Name = "Predao informatičar")]
        public string? PredaoInformatica { get; set; }

        [Display(Name = "Datum zaduženja")]
        public DateTime? DatumZaduzenja { get; set; }

        [Display(Name = "Primio ime i prezime")]
        public string? PrimioImePrezime { get; set; }

        [Display(Name = "Naziv radnog mjesta")]
        public string? NazivRadnogMjesta { get; set; }

        [Display(Name = "Naziv mjesta troška")]
        public string? NazivMjestaTroska { get; set; }

        [Display(Name = "Broj osnovnog sredstva")]
        public string? BrojOsnovnogSredstva { get; set; }

        [Display(Name = "Printer ili dr.")]
        public string? PrinterIliDr { get; set; }

        [Display(Name = "Microsoft Windows")]
        public string? MicrosoftWindows { get; set; }

        [Display(Name = "Microsoft Office")]
        public string? MicrosoftOffice { get; set; }

        [Display(Name = "Antivirusni program")]
        public string? AntivirusniProgram { get; set; }

        [Display(Name = "Ostali programi")]
        public string? OstaliProgrami { get; set; }

        [Display(Name = "Dodatna oprema")]
        public string? DodatnaOprema { get; set; }

        [Display(Name = "IMEI")]
        public string? Imei { get; set; }

        [Display(Name = "Tarifa i broj mob.")]
        public string? TarifaIBrojMob { get; set; }

        [Display(Name = "SN")]
        public string? SN { get; set; }
    }
}