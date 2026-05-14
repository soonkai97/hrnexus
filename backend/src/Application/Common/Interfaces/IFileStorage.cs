namespace NightclubWorkforce.Application.Common.Interfaces;

public interface IFileStorage
{
    /// <summary>Saves a file stream and returns its relative URL path (e.g. "uploads/locations/abc.jpg").</summary>
    Task<string> SaveAsync(Stream stream, string fileName, string folder, CancellationToken ct = default);
    void Delete(string relativePath);
}
