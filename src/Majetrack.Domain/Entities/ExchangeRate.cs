using Majetrack.Domain.Enums;

namespace Majetrack.Domain.Entities;

public class ExchangeRate
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public Currency SourceCurrency { get; set; }
    public Currency TargetCurrency { get; set; }
    public decimal Rate { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}
