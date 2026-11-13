using System.Text.RegularExpressions;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Phase 7i guard: the product is English only. Fails the build if multilingual fields or the
/// language-preference endpoints reappear in backend source. Migrations are history and excluded.
/// Reintroducing a second language needs a new phase document and an ADR (docs/architecture).
/// The forbidden names are assembled from fragments so this file never matches itself.
/// </summary>
public class EnglishOnlyGuardTests
{
    private static readonly string[] Forbidden =
    [
        "Name" + "Zh",
        "Name" + "Vi",
        "Preferred" + "Language",
        "Update" + "Language",
    ];

    private static string FindBackendSrc()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HRNexus.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src");
    }

    [Theory]
    [MemberData(nameof(ForbiddenNames))]
    public void BackendSource_HasNoMultilingualMembers(string forbidden)
    {
        var src = FindBackendSrc();
        var hits = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !Regex.IsMatch(f, @"[\\/](Migrations|bin|obj)[\\/]"))
            .Where(f => File.ReadAllText(f).Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(src, f))
            .ToList();

        Assert.True(hits.Count == 0, $"'{forbidden}' must not appear in backend source (English only): {string.Join(", ", hits)}");
    }

    public static TheoryData<string> ForbiddenNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Forbidden) data.Add(name);
        return data;
    }
}
