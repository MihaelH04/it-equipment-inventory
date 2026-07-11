namespace ITEquipmentInventory.Models.ViewModels
{
    public class SiteBulkEditViewModel
    {
        public List<int> SelectedIds { get; set; } = new();
        public string? Name { get; set; }
        public string? Code { get; set; }
    }
}