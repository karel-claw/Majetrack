using Majetrack.Domain.Enums;

namespace Majetrack.Domain.Entities;

public class Transaction
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? AssetId { get; set; }
    public TransactionType TransactionType { get; set; }
    public DateOnly TransactionDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal PricePerUnit { get; set; }
    public decimal TotalAmount { get; set; }
    public Currency Currency { get; set; }
    public Platform Platform { get; set; }
    public decimal Fee { get; set; }
    public string? Note { get; set; }
    public string? ExternalId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
