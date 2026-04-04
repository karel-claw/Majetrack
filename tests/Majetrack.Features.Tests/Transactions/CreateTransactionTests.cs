using ErrorOr;
using FluentAssertions;
using FluentValidation;
using Majetrack.Domain.Entities;
using Majetrack.Domain.Enums;
using Majetrack.Features.Shared.Services;
using Majetrack.Features.Transactions.Create;
using Majetrack.Infrastructure.ExternalServices.CnbExchangeRateProvider;
using Majetrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Majetrack.Features.Tests.Transactions;

/// <summary>
/// Unit tests for POST /api/transactions — CreateTransactionFeature.
/// Covers happy paths, validation errors, authentication, asset resolution,
/// and FX rate provider scenarios per the T-12/T-14 test scenario document.
/// </summary>
public class CreateTransactionTests : IDisposable
{
    private readonly MajetrackDbContext _db;
    private readonly Mock<ICurrentUser> _currentUserMock;
    private readonly Mock<IExchangeRateProvider> _fxProviderMock;
    private readonly CreateTransactionValidator _validator;
    private readonly Guid _userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly Guid _otherUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private readonly Guid _assetId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    /// <summary>
    /// Initializes test infrastructure with InMemory database and mocked services.
    /// </summary>
    public CreateTransactionTests()
    {
        var options = new DbContextOptionsBuilder<MajetrackDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new MajetrackDbContext(options);
        _currentUserMock = new Mock<ICurrentUser>();
        _currentUserMock.Setup(x => x.UserId).Returns(_userId);
        _fxProviderMock = new Mock<IExchangeRateProvider>();
        _validator = new CreateTransactionValidator();

        // Seed an asset owned by _userId
        _db.Assets.Add(new Asset
        {
            Id = _assetId,
            UserId = _userId,
            Name = "Apple Inc.",
            Ticker = "AAPL",
            AssetType = AssetType.Stock,
            Currency = Currency.USD,
            Platform = Platform.Xtb,
            Exchange = "NASDAQ",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // Seed an asset owned by _otherUserId (for ownership check tests)
        _db.Assets.Add(new Asset
        {
            Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            UserId = _otherUserId,
            Name = "Tesla Inc.",
            Ticker = "TSLA",
            AssetType = AssetType.Stock,
            Currency = Currency.USD,
            Platform = Platform.Etoro,
            Exchange = "NASDAQ",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        _db.SaveChanges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private CreateTransactionFeature CreateFeature()
        => new(_db, _currentUserMock.Object, _validator, _fxProviderMock.Object);

    private static CreateTransactionRequest ValidBuyRequest(Guid assetId) => new(
        TransactionType: "Buy",
        TransactionDate: "2026-01-15",
        TotalAmount: 1500.00m,
        Currency: "USD",
        Platform: "Xtb",
        AssetId: assetId,
        Quantity: 10m,
        PricePerUnit: 150.00m,
        Fee: 1.50m,
        Note: "Bought 10 shares of AAPL");

    private static CreateTransactionRequest ValidDepositRequest() => new(
        TransactionType: "Deposit",
        TransactionDate: "2026-01-10",
        TotalAmount: 50000.00m,
        Currency: "CZK",
        Platform: "Xtb",
        AssetId: null,
        Quantity: null,
        PricePerUnit: null,
        Fee: 0m,
        Note: "Initial deposit");

    #region TC001-008: Happy Path

    /// <summary>
    /// TC001: Buy transaction — creates successfully, returns new transaction ID.
    /// </summary>
    [Fact]
    public async Task TC001_Buy_ReturnsCreatedWithTransactionId()
    {
        // Arrange
        _fxProviderMock.Setup(x => x.GetRateAsync("USD", "CZK", It.IsAny<CancellationToken>()))
            .ReturnsAsync(23.1m);
        var feature = CreateFeature();
        var request = ValidBuyRequest(_assetId);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBe(Guid.Empty);

        var saved = await _db.Transactions.FindAsync(result.Value);
        saved.Should().NotBeNull();
        saved!.TransactionType.Should().Be(Domain.Enums.TransactionType.Buy);
        saved.AssetId.Should().Be(_assetId);
        saved.UserId.Should().Be(_userId);
        saved.Quantity.Should().Be(10m);
        saved.PricePerUnit.Should().Be(150.00m);
        saved.TotalAmount.Should().Be(1500.00m);
        saved.Fee.Should().Be(1.50m);
        saved.Note.Should().Be("Bought 10 shares of AAPL");
    }

    /// <summary>
    /// TC002: Sell transaction — creates successfully.
    /// </summary>
    [Fact]
    public async Task TC002_Sell_ReturnsCreatedWithTransactionId()
    {
        // Arrange
        _fxProviderMock.Setup(x => x.GetRateAsync("USD", "CZK", It.IsAny<CancellationToken>()))
            .ReturnsAsync(23.1m);
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Sell",
            TransactionDate: "2026-02-20",
            TotalAmount: 2000.00m,
            Currency: "USD",
            Platform: "Xtb",
            AssetId: _assetId,
            Quantity: 5m,
            PricePerUnit: 400.00m,
            Fee: 2.00m,
            Note: "Sold 5 shares");

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeFalse();
        var saved = await _db.Transactions.FindAsync(result.Value);
        saved.Should().NotBeNull();
        saved!.TransactionType.Should().Be(Domain.Enums.TransactionType.Sell);
    }

    /// <summary>
    /// TC003: Deposit transaction — no asset required, creates successfully.
    /// </summary>
    [Fact]
    public async Task TC003_Deposit_ReturnsCreatedWithTransactionId()
    {
        // Arrange
        var feature = CreateFeature();
        var request = ValidDepositRequest();

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeFalse();
        var saved = await _db.Transactions.FindAsync(result.Value);
        saved.Should().NotBeNull();
        saved!.TransactionType.Should().Be(Domain.Enums.TransactionType.Deposit);
        saved.AssetId.Should().BeNull();
    }

    /// <summary>
    /// TC004: Withdrawal transaction — no asset required, creates successfully.
    /// </summary>
    [Fact]
    public async Task TC004_Withdrawal_ReturnsCreatedWithTransactionId()
    {
        // Arrange
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Withdrawal",
            TransactionDate: "2026-03-01",
            TotalAmount: 10000.00m,
            Currency: "CZK",
            Platform: "Investown",
            AssetId: null,
            Quantity: null,
            PricePerUnit: null,
            Fee: 0m,
            Note: null);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeFalse();
        var saved = await _db.Transactions.FindAsync(result.Value);
        saved.Should().NotBeNull();
        saved!.TransactionType.Should().Be(Domain.Enums.TransactionType.Withdrawal);
        saved.AssetId.Should().BeNull();
    }

    /// <summary>
    /// TC005: Interest transaction — requires asset, creates successfully.
    /// </summary>
    [Fact]
    public async Task TC005_Interest_ReturnsCreatedWithTransactionId()
    {
        // Arrange
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Interest",
            TransactionDate: "2026-01-31",
            TotalAmount: 50.00m,
            Currency: "CZK",
            Platform: "Investown",
            AssetId: _assetId,
            Quantity: 1m,
            PricePerUnit: 50.00m,
            Fee: 0m,
            Note: "Monthly interest");

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeFalse();
        var saved = await _db.Transactions.FindAsync(result.Value);
        saved.Should().NotBeNull();
        saved!.TransactionType.Should().Be(Domain.Enums.TransactionType.Interest);
    }

    /// <summary>
    /// TC006: Dividend transaction — requires asset, creates successfully.
    /// </summary>
    [Fact]
    public async Task TC006_Dividend_ReturnsCreatedWithTransactionId()
    {
        // Arrange
        _fxProviderMock.Setup(x => x.GetRateAsync("USD", "CZK", It.IsAny<CancellationToken>()))
            .ReturnsAsync(23.1m);
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Dividend",
            TransactionDate: "2026-03-15",
            TotalAmount: 25.00m,
            Currency: "USD",
            Platform: "Xtb",
            AssetId: _assetId,
            Quantity: 10m,
            PricePerUnit: 2.50m,
            Fee: 0m,
            Note: "Q1 dividend");

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeFalse();
        var saved = await _db.Transactions.FindAsync(result.Value);
        saved.Should().NotBeNull();
        saved!.TransactionType.Should().Be(Domain.Enums.TransactionType.Dividend);
    }

    /// <summary>
    /// TC007: Fee is zero — defaults correctly when explicitly set to 0.
    /// </summary>
    [Fact]
    public async Task TC007_FeeZero_DefaultsCorrectly()
    {
        // Arrange
        _fxProviderMock.Setup(x => x.GetRateAsync("USD", "CZK", It.IsAny<CancellationToken>()))
            .ReturnsAsync(23.1m);
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Buy",
            TransactionDate: "2026-01-15",
            TotalAmount: 1500.00m,
            Currency: "USD",
            Platform: "Xtb",
            AssetId: _assetId,
            Quantity: 10m,
            PricePerUnit: 150.00m,
            Fee: 0m,
            Note: "Zero fee trade");

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeFalse();
        var saved = await _db.Transactions.FindAsync(result.Value);
        saved!.Fee.Should().Be(0m);
    }

    /// <summary>
    /// TC008: Note omitted — persists as null.
    /// </summary>
    [Fact]
    public async Task TC008_NoteOmitted_PersistsAsNull()
    {
        // Arrange
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Deposit",
            TransactionDate: "2026-01-10",
            TotalAmount: 50000.00m,
            Currency: "CZK",
            Platform: "Xtb",
            AssetId: null,
            Quantity: null,
            PricePerUnit: null,
            Fee: null,
            Note: null);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeFalse();
        var saved = await _db.Transactions.FindAsync(result.Value);
        saved!.Note.Should().BeNull();
        saved.Fee.Should().Be(0m);
    }

    #endregion

    #region TC016-018: Buy Missing Required Asset Fields

    /// <summary>
    /// TC016: Buy missing AssetId — returns validation error.
    /// </summary>
    [Fact]
    public async Task TC016_BuyMissingAssetId_ReturnsValidationError()
    {
        // Arrange
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Buy",
            TransactionDate: "2026-01-15",
            TotalAmount: 1500.00m,
            Currency: "USD",
            Platform: "Xtb",
            AssetId: null,
            Quantity: 10m,
            PricePerUnit: 150.00m,
            Fee: 1.50m,
            Note: null);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e =>
            e.Type == ErrorType.Validation &&
            e.Code == "AssetId");
    }

    /// <summary>
    /// TC017: Buy missing Quantity — returns validation error.
    /// </summary>
    [Fact]
    public async Task TC017_BuyMissingQuantity_ReturnsValidationError()
    {
        // Arrange
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Buy",
            TransactionDate: "2026-01-15",
            TotalAmount: 1500.00m,
            Currency: "USD",
            Platform: "Xtb",
            AssetId: _assetId,
            Quantity: null,
            PricePerUnit: 150.00m,
            Fee: 1.50m,
            Note: null);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e =>
            e.Type == ErrorType.Validation &&
            e.Code == "Quantity");
    }

    /// <summary>
    /// TC018: Buy missing PricePerUnit — returns validation error.
    /// </summary>
    [Fact]
    public async Task TC018_BuyMissingPricePerUnit_ReturnsValidationError()
    {
        // Arrange
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Buy",
            TransactionDate: "2026-01-15",
            TotalAmount: 1500.00m,
            Currency: "USD",
            Platform: "Xtb",
            AssetId: _assetId,
            Quantity: 10m,
            PricePerUnit: null,
            Fee: 1.50m,
            Note: null);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e =>
            e.Type == ErrorType.Validation &&
            e.Code == "PricePerUnit");
    }

    #endregion

    #region TC022-026: Missing Required Fields

    /// <summary>
    /// TC022: Missing TransactionType — returns validation error.
    /// </summary>
    [Fact]
    public async Task TC022_MissingTransactionType_ReturnsValidationError()
    {
        // Arrange
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: null,
            TransactionDate: "2026-01-15",
            TotalAmount: 1500.00m,
            Currency: "USD",
            Platform: "Xtb",
            AssetId: null,
            Quantity: null,
            PricePerUnit: null,
            Fee: null,
            Note: null);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e =>
            e.Type == ErrorType.Validation &&
            e.Code == "TransactionType");
    }

    /// <summary>
    /// TC023: Missing TransactionDate — returns validation error.
    /// </summary>
    [Fact]
    public async Task TC023_MissingTransactionDate_ReturnsValidationError()
    {
        // Arrange
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Deposit",
            TransactionDate: null,
            TotalAmount: 1500.00m,
            Currency: "CZK",
            Platform: "Xtb",
            AssetId: null,
            Quantity: null,
            PricePerUnit: null,
            Fee: null,
            Note: null);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e =>
            e.Type == ErrorType.Validation &&
            e.Code == "TransactionDate");
    }

    /// <summary>
    /// TC024: Missing TotalAmount — returns validation error.
    /// </summary>
    [Fact]
    public async Task TC024_MissingTotalAmount_ReturnsValidationError()
    {
        // Arrange
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Deposit",
            TransactionDate: "2026-01-15",
            TotalAmount: null,
            Currency: "CZK",
            Platform: "Xtb",
            AssetId: null,
            Quantity: null,
            PricePerUnit: null,
            Fee: null,
            Note: null);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e =>
            e.Type == ErrorType.Validation &&
            e.Code == "TotalAmount");
    }

    /// <summary>
    /// TC025: Missing Currency — returns validation error.
    /// </summary>
    [Fact]
    public async Task TC025_MissingCurrency_ReturnsValidationError()
    {
        // Arrange
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Deposit",
            TransactionDate: "2026-01-15",
            TotalAmount: 1500.00m,
            Currency: null,
            Platform: "Xtb",
            AssetId: null,
            Quantity: null,
            PricePerUnit: null,
            Fee: null,
            Note: null);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e =>
            e.Type == ErrorType.Validation &&
            e.Code == "Currency");
    }

    /// <summary>
    /// TC026: Missing Platform — returns validation error.
    /// </summary>
    [Fact]
    public async Task TC026_MissingPlatform_ReturnsValidationError()
    {
        // Arrange
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Deposit",
            TransactionDate: "2026-01-15",
            TotalAmount: 1500.00m,
            Currency: "CZK",
            Platform: null,
            AssetId: null,
            Quantity: null,
            PricePerUnit: null,
            Fee: null,
            Note: null);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e =>
            e.Type == ErrorType.Validation &&
            e.Code == "Platform");
    }

    #endregion

    #region TC027-029: Invalid Enums

    /// <summary>
    /// TC027: Invalid TransactionType enum value — returns validation error.
    /// </summary>
    [Fact]
    public async Task TC027_InvalidTransactionType_ReturnsValidationError()
    {
        // Arrange
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Transfer",
            TransactionDate: "2026-01-15",
            TotalAmount: 1500.00m,
            Currency: "USD",
            Platform: "Xtb",
            AssetId: null,
            Quantity: null,
            PricePerUnit: null,
            Fee: null,
            Note: null);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e =>
            e.Type == ErrorType.Validation &&
            e.Code == "TransactionType");
    }

    /// <summary>
    /// TC028: Invalid Currency enum value — returns validation error.
    /// </summary>
    [Fact]
    public async Task TC028_InvalidCurrency_ReturnsValidationError()
    {
        // Arrange
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Deposit",
            TransactionDate: "2026-01-15",
            TotalAmount: 1500.00m,
            Currency: "GBP",
            Platform: "Xtb",
            AssetId: null,
            Quantity: null,
            PricePerUnit: null,
            Fee: null,
            Note: null);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e =>
            e.Type == ErrorType.Validation &&
            e.Code == "Currency");
    }

    /// <summary>
    /// TC029: Invalid Platform enum value — returns validation error.
    /// </summary>
    [Fact]
    public async Task TC029_InvalidPlatform_ReturnsValidationError()
    {
        // Arrange
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Deposit",
            TransactionDate: "2026-01-15",
            TotalAmount: 1500.00m,
            Currency: "CZK",
            Platform: "Robinhood",
            AssetId: null,
            Quantity: null,
            PricePerUnit: null,
            Fee: null,
            Note: null);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e =>
            e.Type == ErrorType.Validation &&
            e.Code == "Platform");
    }

    #endregion

    #region TC031-033: Amount Validation

    /// <summary>
    /// TC031: TotalAmount is zero — returns validation error.
    /// </summary>
    [Fact]
    public async Task TC031_TotalAmountZero_ReturnsValidationError()
    {
        // Arrange
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Deposit",
            TransactionDate: "2026-01-15",
            TotalAmount: 0m,
            Currency: "CZK",
            Platform: "Xtb",
            AssetId: null,
            Quantity: null,
            PricePerUnit: null,
            Fee: null,
            Note: null);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e =>
            e.Type == ErrorType.Validation &&
            e.Code == "TotalAmount");
    }

    /// <summary>
    /// TC032: TotalAmount is negative — returns validation error.
    /// </summary>
    [Fact]
    public async Task TC032_TotalAmountNegative_ReturnsValidationError()
    {
        // Arrange
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Deposit",
            TransactionDate: "2026-01-15",
            TotalAmount: -100m,
            Currency: "CZK",
            Platform: "Xtb",
            AssetId: null,
            Quantity: null,
            PricePerUnit: null,
            Fee: null,
            Note: null);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e =>
            e.Type == ErrorType.Validation &&
            e.Code == "TotalAmount");
    }

    /// <summary>
    /// TC033: Fee is negative — returns validation error.
    /// </summary>
    [Fact]
    public async Task TC033_FeeNegative_ReturnsValidationError()
    {
        // Arrange
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Deposit",
            TransactionDate: "2026-01-15",
            TotalAmount: 1000m,
            Currency: "CZK",
            Platform: "Xtb",
            AssetId: null,
            Quantity: null,
            PricePerUnit: null,
            Fee: -5m,
            Note: null);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e =>
            e.Type == ErrorType.Validation &&
            e.Code == "Fee");
    }

    #endregion

    #region TC040: Unauthenticated

    /// <summary>
    /// TC040: Unauthenticated user — returns Unauthorized error.
    /// </summary>
    [Fact]
    public async Task TC040_Unauthenticated_ReturnsUnauthorizedError()
    {
        // Arrange
        _currentUserMock.Setup(x => x.UserId).Returns((Guid?)null);
        var feature = CreateFeature();
        var request = ValidDepositRequest();

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().ContainSingle()
            .Which.Type.Should().Be(ErrorType.Unauthorized);
    }

    #endregion

    #region TC201-211: FX Rate Provider

    /// <summary>
    /// TC201: CZK transaction — FX provider is NOT called, returns 201.
    /// </summary>
    [Fact]
    public async Task TC201_CzkTransaction_NoFxCall_Returns201()
    {
        // Arrange
        var feature = CreateFeature();
        var request = ValidDepositRequest(); // CZK currency

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBe(Guid.Empty);
        _fxProviderMock.Verify(x => x.GetRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// TC202: EUR transaction — FX rate fetched, returns 201.
    /// </summary>
    [Fact]
    public async Task TC202_EurTransaction_FxFetched_Returns201()
    {
        // Arrange
        _fxProviderMock.Setup(x => x.GetRateAsync("EUR", "CZK", It.IsAny<CancellationToken>()))
            .ReturnsAsync(25.5m);
        var feature = CreateFeature();
        var request = new CreateTransactionRequest(
            TransactionType: "Deposit",
            TransactionDate: "2026-01-10",
            TotalAmount: 1000.00m,
            Currency: "EUR",
            Platform: "Xtb",
            AssetId: null,
            Quantity: null,
            PricePerUnit: null,
            Fee: 0m,
            Note: null);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBe(Guid.Empty);
        _fxProviderMock.Verify(x => x.GetRateAsync("EUR", "CZK", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// TC203: USD transaction — FX rate fetched, returns 201.
    /// </summary>
    [Fact]
    public async Task TC203_UsdTransaction_FxFetched_Returns201()
    {
        // Arrange
        _fxProviderMock.Setup(x => x.GetRateAsync("USD", "CZK", It.IsAny<CancellationToken>()))
            .ReturnsAsync(23.1m);
        var feature = CreateFeature();
        var request = ValidBuyRequest(_assetId); // USD currency

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBe(Guid.Empty);
        _fxProviderMock.Verify(x => x.GetRateAsync("USD", "CZK", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// TC210: FX provider returns null (rate unavailable) — returns 502.
    /// </summary>
    [Fact]
    public async Task TC210_FxUnavailable_Returns502()
    {
        // Arrange
        _fxProviderMock.Setup(x => x.GetRateAsync("USD", "CZK", It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal?)null);
        var feature = CreateFeature();
        var request = ValidBuyRequest(_assetId); // USD currency

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("Transaction.FxRateUnavailable");
    }

    /// <summary>
    /// TC211: FX provider throws exception — returns 502.
    /// </summary>
    [Fact]
    public async Task TC211_FxProviderThrows_Returns502()
    {
        // Arrange
        _fxProviderMock.Setup(x => x.GetRateAsync("USD", "CZK", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("CNB API unavailable"));
        var feature = CreateFeature();
        var request = ValidBuyRequest(_assetId); // USD currency

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("Transaction.FxRateUnavailable");
    }

    #endregion

    #region TC043-044: Asset Resolution

    /// <summary>
    /// TC043: Asset not found — returns NotFound error.
    /// </summary>
    [Fact]
    public async Task TC043_AssetNotFound_ReturnsNotFoundError()
    {
        // Arrange
        _fxProviderMock.Setup(x => x.GetRateAsync("USD", "CZK", It.IsAny<CancellationToken>()))
            .ReturnsAsync(23.1m);
        var feature = CreateFeature();
        var nonExistentAssetId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var request = ValidBuyRequest(nonExistentAssetId);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().ContainSingle()
            .Which.Type.Should().Be(ErrorType.NotFound);
    }

    /// <summary>
    /// TC044: Asset owned by different user — returns NotFound error (same as not found to avoid info leakage).
    /// 🚨 BLOCKER FIX: This is the critical ownership check test.
    /// </summary>
    [Fact]
    public async Task TC044_AssetOwnedByDifferentUser_ReturnsNotFoundError()
    {
        // Arrange
        _fxProviderMock.Setup(x => x.GetRateAsync("USD", "CZK", It.IsAny<CancellationToken>()))
            .ReturnsAsync(23.1m);
        var feature = CreateFeature();
        var otherUsersAssetId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var request = ValidBuyRequest(otherUsersAssetId);

        // Act
        var result = await feature.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().ContainSingle()
            .Which.Type.Should().Be(ErrorType.NotFound);
    }

    #endregion
}
