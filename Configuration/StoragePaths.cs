using Microsoft.Extensions.Options;

namespace ITEquipmentInventory.Configuration;

public sealed class StoragePaths
{
    private readonly string _contentRootPath;

    public StoragePaths(IWebHostEnvironment environment, IOptions<StorageOptions> options)
    {
        _contentRootPath = environment.ContentRootPath;
        var storage = options.Value;

        ProfileImagesPath = ResolveRequired(storage.ProfileImagesPath, "Storage:ProfileImagesPath");
        ConsumableImagesPath = ResolveRequired(storage.ConsumableImagesPath, "Storage:ConsumableImagesPath");
        GeneratedDocumentsPath = ResolveRequired(storage.GeneratedDocumentsPath, "Storage:GeneratedDocumentsPath");
        LibreOfficeProfilePath = ResolveRequired(storage.LibreOfficeProfilePath, "Storage:LibreOfficeProfilePath");
        TemplatesPath = ResolveRequired(storage.TemplatesPath, "Storage:TemplatesPath");
        DataProtectionKeysPath = ResolveRequired(storage.DataProtectionKeysPath, "Storage:DataProtectionKeysPath");
        LibreOfficeExecutable = string.IsNullOrWhiteSpace(storage.LibreOfficeExecutable)
            ? "soffice"
            : storage.LibreOfficeExecutable;
    }

    public string ProfileImagesPath { get; }
    public string ConsumableImagesPath { get; }
    public string GeneratedDocumentsPath { get; }
    public string LibreOfficeProfilePath { get; }
    public string TemplatesPath { get; }
    public string DataProtectionKeysPath { get; }
    public string LibreOfficeExecutable { get; }

    public string ResolveFromContentRoot(string path) =>
        Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(_contentRootPath, path));

    private string ResolveRequired(string path, string configurationKey)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"{configurationKey} nije konfiguriran.");

        return ResolveFromContentRoot(path);
    }
}
