namespace StudyHub.Api.Domain;

public class Tag
{
    public int Id { get; set; }

    /// <summary>Always stored trimmed and lower-case.</summary>
    public string Name { get; set; } = string.Empty;

    public List<Material> Materials { get; set; } = new();
}
