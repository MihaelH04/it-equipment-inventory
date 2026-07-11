using ITEquipmentInventory.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ITEquipmentInventory.Models.ViewModels
{
    public class EmployeeBulkEditViewModel
    {
        public List<int> SelectedIds { get; set; } = new();

        public int SelectedCount => SelectedIds?.Count ?? 0;

        public EmployeeStatus? Status { get; set; }

        public int? SiteId { get; set; }

        public List<SelectListItem> Sites { get; set; } = new();
    }
}