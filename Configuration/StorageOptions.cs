namespace ITEquipmentInventory.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string ProfileImagesPath { get; set; } = string.Empty;
    public string ConsumableImagesPath { get; set; } = string.Empty;
    public string GeneratedDocumentsPath { get; set; } = string.Empty;
    public string LibreOfficeProfilePath { get; set; } = string.Empty;
    public string TemplatesPath { get; set; } = string.Empty;
    public string DataProtectionKeysPath { get; set; } = string.Empty;
    public string LibreOfficeExecutable { get; set; } = string.Empty;
}
