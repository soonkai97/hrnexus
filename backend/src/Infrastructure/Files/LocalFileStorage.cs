using HRNexus.Application.Common.Interfaces;
using Microsoft.AspNetCore.Hosting;

namespace HRNexus.Infrastructure.Files;

public class LocalFileStorage(IWebHostEnvironment env) : IFileStorage
{
    public async Task<string> SaveAsync(Stream stream, string fileName, string folder, CancellationToken ct = default)
    {
        var ext       = Path.GetExtension(fileName);
        var safe      = $"{Guid.NewGuid():N}{ext}";
        var dir       = Path.Combine(env.WebRootPath, "uploads", folder);
        Directory.CreateDirectory(dir);
        var fullPath  = Path.Combine(dir, safe);
        await using var fs = File.Create(fullPath);
        await stream.CopyToAsync(fs, ct);
        return $"uploads/{folder}/{safe}";
    }

    public void Delete(string relativePath)
    {
        var fullPath = Path.Combine(env.WebRootPath, relativePath);
        if (File.Exists(fullPath)) File.Delete(fullPath);
    }
}
