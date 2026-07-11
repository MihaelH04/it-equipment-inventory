using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace ITEquipmentInventory.Models.ViewModels
{
    public class EquipmentBulkReturnDecisionViewModel
    {
        public List<int> SelectedIds { get; set; } = new();

        public int SelectedCount => SelectedIds?.Count ?? 0;

        public List<Equipment> SelectedEquipment { get; set; } = new();

        [Required(ErrorMessage = "Odaberi što želiš napraviti.")]
        public string ActionType { get; set; } = "free";

        public int? NewEmployeeId { get; set; }

        public int? NewSiteId { get; set; }

        public string? HandedOverBy { get; set; }

        [DataType(DataType.Date)]
        public DateTime EffectiveDate { get; set; } = DateTime.Today;

        public List<SelectListItem> Employees { get; set; } = new();

        public List<SelectListItem> Sites { get; set; } = new();

        public List<SelectListItem> HandedOverByOptions { get; set; } = new();
    }
}
