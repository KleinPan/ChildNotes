using System.IO;
using Avalonia.Platform.Storage;

namespace ChildNotes.Services;

public sealed class UploadService
{
    private readonly string _storageRoot;

    public UploadService(string storageRoot)
    {
        _storageRoot = storageRoot;
        Directory.CreateDirectory(_storageRoot);
    }

    public async Task<string?> SaveImageAsync(IStorageFile file)
    {
        var ext = Path.GetExtension(file.Name);
        var fileName = $"img_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(_storageRoot, fileName);
        await using var stream = await file.OpenReadAsync();
        await using var fs = File.Create(fullPath);
        await stream.CopyToAsync(fs);
        return fullPath;
    }

    public string? SaveLocalImage(string sourcePath)
    {
        if (!File.Exists(sourcePath)) return null;
        var ext = Path.GetExtension(sourcePath);
        var fileName = $"img_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(_storageRoot, fileName);
        File.Copy(sourcePath, fullPath, true);
        return fullPath;
    }
}
