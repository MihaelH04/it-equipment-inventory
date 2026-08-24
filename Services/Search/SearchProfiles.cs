using ITEquipmentInventory.Models;

namespace ITEquipmentInventory.Services.Search;

public static class SearchProfiles
{
    public static SearchField<Equipment>[] Equipment() =>
    [
        new(e => e.InventoryNumber, 100, true),
        new(e => e.SerialNumber, 95, true),
        new(e => e.Name, 80),
        new(e => e.CurrentEmployee != null ? e.CurrentEmployee.WorkerCode : null, 90, true),
        new(e => e.CurrentEmployee != null ? e.CurrentEmployee.FullName : null, 60),
        new(e => e.CurrentSite != null ? e.CurrentSite.Code : null, 85, true),
        new(e => e.CurrentSite != null ? e.CurrentSite.Name : null, 50),
        new(e => e.CurrentSite != null ? e.CurrentSite.Location : null, 30),
        new(e => e.EquipmentType == EquipmentType.Printer ? "printer pisac" :
            e.EquipmentType == EquipmentType.Laptop ? "laptop notebook prijenosno racunalo" :
            e.EquipmentType == EquipmentType.PC ? "pc desktop racunalo stolno racunalo" :
            e.EquipmentType == EquipmentType.Monitor ? "monitor display ekran" :
            e.EquipmentType == EquipmentType.Tablet ? "tablet" :
            e.EquipmentType == EquipmentType.Mobitel ? "mobitel telefon" :
            e.EquipmentType == EquipmentType.Router ? "router usmjerivac" :
            e.EquipmentType == EquipmentType.Switch ? "switch preklopnik" : "ostalo", 65),
        new(e => e.Status == EquipmentStatus.Dostupno ? "dostupno slobodno" :
            e.Status == EquipmentStatus.Zaduzeno ? "zaduzeno dodijeljeno" :
            e.Status == EquipmentStatus.Servis ? "servis popravak" : "otpisano", 20)
    ];

    public static SearchField<Site>[] Sites() =>
    [
        new(s => s.Code, 100, true),
        new(s => s.Name, 80),
        new(s => s.Location, 40),
        new(s => s.Status == SiteStatus.Aktivno ? "aktivno aktivan active" : "neaktivno neaktivan inactive", 20)
    ];

    public static SearchField<Employee>[] Employees() =>
    [
        new(e => e.WorkerCode, 100, true),
        new(e => e.FullName, 80),
        new(e => e.Site != null ? e.Site.Code : null, 75, true),
        new(e => e.Site != null ? e.Site.Name : null, 50),
        new(e => e.Site != null ? e.Site.Location : null, 30),
        new(e => e.Status == EmployeeStatus.Aktivan ? "aktivan aktivno active" : "neaktivan neaktivno inactive", 20)
    ];

    public static SearchField<Employee>[] EmployeeSites() =>
    [
        new(e => e.Site != null ? e.Site.Code : null, 100, true),
        new(e => e.Site != null ? e.Site.Name : null, 80),
        new(e => e.Site != null ? e.Site.Location : null, 40)
    ];

    public static SearchField<EquipmentReturn>[] Returns() =>
    [
        new(x => x.InventoryNumber, 100, true),
        new(x => x.SerialNumber, 95, true),
        new(x => x.Name, 80),
        new(x => x.PreviousEmployeeCode, 90, true),
        new(x => x.PreviousEmployeeName, 60),
        new(x => x.PreviousSiteCode, 85, true),
        new(x => x.PreviousSiteName, 50),
        new(x => x.PreviousSiteLocation, 30),
        new(x => x.HandedOverBy, 25),
        new(x => x.PreviousHandedOverBy, 20),
        new(x => x.Note, 10),
        new(x => x.EquipmentType == EquipmentType.Printer ? "printer pisac" :
            x.EquipmentType == EquipmentType.Laptop ? "laptop notebook prijenosno racunalo" :
            x.EquipmentType == EquipmentType.PC ? "pc desktop racunalo stolno racunalo" :
            x.EquipmentType == EquipmentType.Monitor ? "monitor display ekran" :
            x.EquipmentType == EquipmentType.Tablet ? "tablet" :
            x.EquipmentType == EquipmentType.Mobitel ? "mobitel telefon" :
            x.EquipmentType == EquipmentType.Router ? "router usmjerivac" :
            x.EquipmentType == EquipmentType.Switch ? "switch preklopnik" : "ostalo", 65)
    ];

    public static SearchField<ConsumableTransaction>[] ConsumableTransactions() =>
    [
        new(x => x.ProductCode, 100, true),
        new(x => x.ConsumableName, 80),
        new(x => x.PrinterName, 60),
        new(x => x.SiteName, 50),
        new(x => x.PerformedBy, 20),
        new(x => x.ConsumableType == ConsumableType.Toner ? "toner" :
            x.ConsumableType == ConsumableType.Tinta ? "tinta" :
            x.ConsumableType == ConsumableType.KutijaZaOdrzavanje ? "kutija za odrzavanje" : "ostalo", 40),
        new(x => x.TransactionType == ConsumableTransactionType.Naruceno ? "naruceno" :
            x.TransactionType == ConsumableTransactionType.Zaprimljeno ? "zaprimljeno primljeno" : "izdano potroseno", 30)
    ];

    public static SearchField<PrinterConsumable>[] Consumables() =>
    [
        new(x => x.ProductCode, 100, true),
        new(x => x.Name, 80),
        new(x => x.Type == ConsumableType.Toner ? "toner" :
            x.Type == ConsumableType.Tinta ? "tinta" :
            x.Type == ConsumableType.KutijaZaOdrzavanje ? "kutija za odrzavanje" : "ostalo", 50),
        new(x => x.IsOriginal ? "original originalni" : "zamjenski kompatibilni zamjena", 40),
        new(x => x.QuantityAvailable > 0 ? "dostupno na stanju" :
            x.QuantityOrdered > 0 ? "naruceno dolazi" : "nema nedostupno", 30)
    ];

    public static SearchField<ConsumableCompatiblePrinter>[] CompatiblePrinters() =>
    [
        new(x => x.PrinterName, 70)
    ];
}
