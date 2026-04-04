using ErrorOr;
using FluentValidation;
using Majetrack.Domain.Entities;
using Majetrack.Domain.Enums;
using Majetrack.Features.Shared.Extensions;
using Majetrack.Features.Shared.Services;
using Majetrack.Infrastructure.ExternalServices.CnbExchangeRateProvider;
using Majetrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Majetrack.Features.Transactions.Create;

/// <summary>
/// Orchestrates the creation of a new transaction. Validates the request,
/// resolves and verifies asset ownership, fetches FX rate if needed, and persists.
/// </summary>
public class CreateTransactionFeature
{
    private readonly MajetrackDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IValidator<CreateTransactionRequest> _validator;
    private readonly IExchangeRateProvider _fxProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="CreateTransactionFeature"/>.
    /// </summary>
    /// <param name="db">The database context for persistence.</param>
    /// <param name="currentUser">Provides the authenticated user's identity.</param>
    /// <param name="validator">The FluentValidation validator for the request.</param>
    /// <param name="fxProvider">Provides exchange rate data for non-CZK transactions.</param>
    public CreateTransactionFeature(
        MajetrackDbContext db,
        ICurrentUser currentUser,
        IValidator<CreateTransactionRequest> validator,
        IExchangeRateProvider fxProvider)
    {
        _db = db;
        _currentUser = currentUser;
        _validator = validator;
        _fxProvider = fxProvider;
    }

    /// <summary>
    /// Transaction types that require asset resolution and ownership checks.
    /// </summary>
    private static readonly HashSet<TransactionType> AssetRequiredTypes =
    [
        TransactionType.Buy,
        TransactionType.Sell,
        TransactionType.Interest,
        TransactionType.Dividend,
    ];

    /// <summary>
    /// Executes the create transaction workflow:
    /// authenticate → validate → resolve asset → persist → return ID.
    /// </summary>
    /// <param name="request">The create transaction request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="ErrorOr{Guid}"/> containing the new transaction ID on success,
    /// or one or more errors on failure.
    /// </returns>
    public async Task<ErrorOr<Guid>> ExecuteAsync(CreateTransactionRequest request, CancellationToken ct = default)
    {
        // Step 1: Authenticate
        if (_currentUser.UserId is not { } userId)
        {
            return TransactionErrors.Unauthenticated;
        }

        // Step 2: Validate
        var validation = await request.ValidateRequest(_validator, ct);
        if (validation.IsError)
        {
            return validation.Errors;
        }

        // Parse enums (safe after validation)
        var transactionType = Enum.Parse<TransactionType>(request.TransactionType!);
        var currency = Enum.Parse<Currency>(request.Currency!);
        var platform = Enum.Parse<Platform>(request.Platform!);
        var transactionDate = DateOnly.Parse(request.TransactionDate!);

        // Step 3: Resolve asset (for asset-required types)
        Guid? resolvedAssetId = null;
        if (AssetRequiredTypes.Contains(transactionType))
        {
            var asset = await _db.Assets
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == request.AssetId, ct);

            // Asset not found → 404
            if (asset is null)
            {
                return TransactionErrors.AssetNotFound;
            }

            // 🚨 Asset ownership check — must be owned by current user
            if (asset.UserId != userId)
            {
                return TransactionErrors.AssetNotFound; // Same 404 to avoid info leakage
            }

            resolvedAssetId = asset.Id;
        }

        // Step 4: Resolve FX rate (skip for CZK — it's the base currency)
        decimal? fxRateToCzk = null;
        if (currency != Currency.CZK)
        {
            try
            {
                fxRateToCzk = await _fxProvider.GetRateAsync(currency.ToString(), Currency.CZK.ToString(), ct);
            }
            catch
            {
                return TransactionErrors.FxRateUnavailable;
            }

            if (fxRateToCzk is null)
            {
                return TransactionErrors.FxRateUnavailable;
            }
        }

        // Step 5: Persist
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AssetId = resolvedAssetId,
            TransactionType = transactionType,
            TransactionDate = transactionDate,
            Quantity = request.Quantity ?? 0,
            PricePerUnit = request.PricePerUnit ?? 0,
            TotalAmount = request.TotalAmount!.Value,
            Currency = currency,
            Platform = platform,
            Fee = request.Fee ?? 0,
            Note = request.Note,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.Transactions.Add(transaction);
        await _db.SaveChangesAsync(ct);

        return transaction.Id;
    }
}
