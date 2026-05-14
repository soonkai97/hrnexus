namespace NightclubWorkforce.Domain.Entities;

public class Location
{
    public string Id { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? ImagePath { get; private set; }
    public bool IsDeleted { get; private set; }

    private Location() { }

    public static Location Create(string name) =>
        new() { Id = Guid.NewGuid().ToString(), Name = name, IsDeleted = false };

    public void Update(string name) => Name = name;
    public void SetImage(string relativePath) => ImagePath = relativePath;
    public void Delete() => IsDeleted = true;
}
