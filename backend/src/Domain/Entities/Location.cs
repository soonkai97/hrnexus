namespace HRNexus.Domain.Entities;

public class Location
{
    public string Id { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    /// <summary>Optional Chinese name, shown when the UI language is Chinese (falls back to Name).</summary>
    public string? NameZh { get; private set; }
    /// <summary>Optional Vietnamese name, shown when the UI language is Vietnamese (falls back to Name).</summary>
    public string? NameVi { get; private set; }
    public string? ImagePath { get; private set; }
    public bool IsDeleted { get; private set; }

    private Location() { }

    public static Location Create(string name, string? nameZh = null, string? nameVi = null) =>
        new() { Id = Guid.NewGuid().ToString(), Name = name.Trim().ToUpperInvariant(), NameZh = Clean(nameZh), NameVi = Clean(nameVi), IsDeleted = false };

    public void Update(string name, string? nameZh = null, string? nameVi = null)
    {
        Name   = name.Trim().ToUpperInvariant();
        NameZh = Clean(nameZh);
        NameVi = Clean(nameVi);
    }
    public void SetImage(string relativePath) => ImagePath = relativePath;
    public void Delete() => IsDeleted = true;

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
