namespace HRNexus.Application.Common.Interfaces;

public interface IFileStorage
{
    /// <summary>Saves a file stream and returns its relative URL path (e.g. "uploads/documents/abc.pdf").</summary>
    Task<string> SaveAsync(Stream stream, string fileName, string folder, CancellationToken ct = default);
    void Delete(string relativePath);
}
