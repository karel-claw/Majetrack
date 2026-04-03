using Majetrack.Domain.Enums;

namespace Majetrack.Domain.Entities;

public class Asset
{
    public Guid Id { get; set; }
    public string? Ticker { get; set; }
    public string Name { get; set; } = string.Empty;
    public AssetType AssetType { get; set; }
    public string? Exchange { get; set; }
    public Currency Currency { get; set; }
    public Platform Platform { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
