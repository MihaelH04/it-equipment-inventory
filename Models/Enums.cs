namespace ITEquipmentInventory.Models;

public enum EmployeeStatus
{
    Aktivan = 1,
    Neaktivan = 2
}

public enum SiteStatus
{
    Aktivno = 1,
    Neaktivno = 2
}

public enum EquipmentStatus
{
    Dostupno = 1,
    Zaduzeno = 2,
    Servis = 4,
    Otpisano = 5
}

public enum EquipmentType
{
    Laptop = 1,
    PC = 2,
    Monitor = 3,
    Printer = 4,
    Tablet = 5,
    Mobitel = 6,
    Router = 7,
    Switch = 8,
    Ostalo = 9
}

public enum AppUserRole
{
    Admin = 1,
    User = 2
}
