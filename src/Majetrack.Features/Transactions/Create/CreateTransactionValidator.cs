using FluentValidation;
using Majetrack.Domain.Enums;

namespace Majetrack.Features.Transactions.Create;

/// <summary>
/// Validates <see cref="CreateTransactionRequest"/> using FluentValidation rules.
/// Enforces required fields, enum parsing, numeric constraints, and conditional
/// requirements based on transaction type.
/// </summary>
public class CreateTransactionValidator : AbstractValidator<CreateTransactionRequest>
{
    /// <summary>
    /// Transaction types that require asset-related fields (AssetId, Quantity, PricePerUnit).
    /// </summary>
    private static readonly HashSet<TransactionType> AssetRequiredTypes =
    [
        TransactionType.Buy,
        TransactionType.Sell,
        TransactionType.Interest,
        TransactionType.Dividend,
    ];

    /// <summary>
    /// Initializes validation rules for creating a transaction.
    /// </summary>
    public CreateTransactionValidator()
    {
        RuleFor(x => x.TransactionType)
            .NotEmpty().WithMessage("TransactionType is required.")
            .Must(BeAValidTransactionType).WithMessage("TransactionType must be one of: Buy, Sell, Deposit, Withdrawal, Interest, Dividend.");

        RuleFor(x => x.TransactionDate)
            .NotEmpty().WithMessage("TransactionDate is required.")
            .Must(BeAValidDate).WithMessage("TransactionDate must be a valid date in YYYY-MM-DD format.");

        RuleFor(x => x.TotalAmount)
            .NotNull().WithMessage("TotalAmount is required.")
            .GreaterThan(0).WithMessage("TotalAmount must be greater than zero.");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("Currency is required.")
            .Must(BeAValidCurrency).WithMessage("Currency must be one of: CZK, EUR, USD.");

        RuleFor(x => x.Platform)
            .NotEmpty().WithMessage("Platform is required.")
            .Must(BeAValidPlatform).WithMessage("Platform must be one of: Xtb, Etoro, Investown.");

        RuleFor(x => x.Fee)
            .GreaterThanOrEqualTo(0).WithMessage("Fee must be zero or positive.")
            .When(x => x.Fee is not null);

        // Conditional: asset-related fields required for Buy, Sell, Interest, Dividend
        When(x => IsAssetRequiredType(x.TransactionType), () =>
        {
            RuleFor(x => x.AssetId)
                .NotNull().WithMessage("AssetId is required for this transaction type.");

            RuleFor(x => x.Quantity)
                .NotNull().WithMessage("Quantity is required for this transaction type.")
                .GreaterThan(0).WithMessage("Quantity must be greater than zero.")
                .When(x => x.Quantity is not null || IsAssetRequiredType(x.TransactionType));

            RuleFor(x => x.PricePerUnit)
                .NotNull().WithMessage("PricePerUnit is required for this transaction type.")
                .GreaterThan(0).WithMessage("PricePerUnit must be greater than zero.")
                .When(x => x.PricePerUnit is not null || IsAssetRequiredType(x.TransactionType));
        });

        // Note length
        RuleFor(x => x.Note)
            .MaximumLength(1000).WithMessage("Note must not exceed 1000 characters.")
            .When(x => x.Note is not null);
    }

    private static bool BeAValidTransactionType(string? value)
        => value is not null && Enum.TryParse<TransactionType>(value, ignoreCase: false, out _);

    private static bool BeAValidCurrency(string? value)
        => value is not null && Enum.TryParse<Currency>(value, ignoreCase: false, out _);

    private static bool BeAValidPlatform(string? value)
        => value is not null && Enum.TryParse<Platform>(value, ignoreCase: false, out _);

    private static bool BeAValidDate(string? value)
        => value is not null && DateOnly.TryParse(value, out _);

    private static bool IsAssetRequiredType(string? transactionType)
        => transactionType is not null
           && Enum.TryParse<TransactionType>(transactionType, ignoreCase: false, out var tt)
           && AssetRequiredTypes.Contains(tt);
}
