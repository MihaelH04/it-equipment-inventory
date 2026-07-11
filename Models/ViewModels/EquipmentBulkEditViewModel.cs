using ITEquipmentInventory.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace ITEquipmentInventory.Models.ViewModels
{
    public class EquipmentBulkEditViewModel
    {
        public List<int> SelectedIds { get; set; } = new();

        public int SelectedCount => SelectedIds?.Count ?? 0;

        public string? HandedOverBy { get; set; }

        public EquipmentStatus? Status { get; set; }

        public int? CurrentSiteId { get; set; }

        public int? CurrentEmployeeId { get; set; }

        [DataType(DataType.Date)]
        public DateTime? AssignedAt { get; set; }

        [DataType(DataType.Date)]
        public DateTime? ReturnedAt { get; set; }

        public List<SelectListItem> Sites { get; set; } = new();

        public List<SelectListItem> Employees { get; set; } = new();

        public List<SelectListItem> HandedOverByOptions { get; set; } = new();
    }
}
