namespace ITEquipmentInventory.ViewModels;

public class DashboardViewModel
{
    public int TotalEquipment { get; set; }
    public int AssignedEquipment { get; set; }
    public int FreeEquipment { get; set; }
    public int EquipmentInService { get; set; }
    public int WarrantyExpiringSoon { get; set; }

    public int TotalEmployees { get; set; }
    public int ActiveEmployees { get; set; }
    public int TotalSites { get; set; }
    public int ActiveAssignments { get; set; }
}