using ErrorOr;
using FluentAssertions;
using FluentValidation;
using Majetrack.Features.Shared.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Majetrack.Features.Tests.Shared.Extensions;

/// <summary>
/// Unit tests for <see cref="ErrorOrEndpointExtensions"/>.
/// Tests pure mapping logic without HTTP pipeline or database dependencies.
/// </summary>
public class ErrorOrEndpointExtensionsTests
{
    #region Group 1 — List<Error>.ToHttpResult()

    /// <summary>
    /// TC-01: Single Validation error returns ValidationProblem with field key.
    /// </summary>
    [Fact]
    public void TC01_SingleValidationError_ReturnsValidationProblemWithFieldKey()
    {
        // Arrange
        var errors = new List<Error> { Error.Validation("Amount", "'Amount' must be greater than 0.") };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        var validationProblem = result.Should().BeOfType<ValidationProblem>().Subject;
        validationProblem.StatusCode.Should().Be(400);
        validationProblem.ProblemDetails.Errors.Should().ContainKey("Amount");
        validationProblem.ProblemDetails.Errors["Amount"].Should().Contain("'Amount' must be greater than 0.");
    }

    /// <summary>
    /// TC-02: Multiple Validation errors on same field returns array of both messages under one key.
    /// </summary>
    [Fact]
    public void TC02_MultipleValidationErrorsSameField_ReturnsArrayOfBothMessages()
    {
        // Arrange
        var errors = new List<Error>
        {
            Error.Validation("Symbol", "'Symbol' must not be empty."),
            Error.Validation("Symbol", "'Symbol' must be 3 characters.")
        };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        var validationProblem = result.Should().BeOfType<ValidationProblem>().Subject;
        validationProblem.ProblemDetails.Errors["Symbol"].Should().HaveCount(2);
        validationProblem.ProblemDetails.Errors["Symbol"].Should().Contain("'Symbol' must not be empty.");
        validationProblem.ProblemDetails.Errors["Symbol"].Should().Contain("'Symbol' must be 3 characters.");
    }

    /// <summary>
    /// TC-03: Multiple Validation errors on different fields returns all field keys present.
    /// </summary>
    [Fact]
    public void TC03_MultipleValidationErrorsDifferentFields_ReturnsAllFieldKeys()
    {
        // Arrange
        var errors = new List<Error>
        {
            Error.Validation("Amount", "msg1"),
            Error.Validation("Symbol", "msg2")
        };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        var validationProblem = result.Should().BeOfType<ValidationProblem>().Subject;
        validationProblem.ProblemDetails.Errors.Should().ContainKey("Amount");
        validationProblem.ProblemDetails.Errors.Should().ContainKey("Symbol");
        validationProblem.ProblemDetails.Errors["Amount"].Should().Contain("msg1");
        validationProblem.ProblemDetails.Errors["Symbol"].Should().Contain("msg2");
    }

    /// <summary>
    /// TC-04: Mixed errors — first is Validation, second is NotFound — returns Problem(400), NOT ValidationProblem.
    /// </summary>
    [Fact]
    public void TC04_MixedErrorsValidationFirst_ReturnsProblem400NotValidationProblem()
    {
        // Arrange
        var errors = new List<Error>
        {
            Error.Validation("Field", "bad"),
            Error.NotFound("Entity", "not found")
        };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(400);
    }

    /// <summary>
    /// TC-05: NotFound error returns Problem(404).
    /// </summary>
    [Fact]
    public void TC05_NotFoundError_ReturnsProblem404()
    {
        // Arrange
        var errors = new List<Error> { Error.NotFound("Transaction", "Transaction with id 99 was not found.") };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(404);
    }

    /// <summary>
    /// TC-06: Conflict error returns Problem(409).
    /// </summary>
    [Fact]
    public void TC06_ConflictError_ReturnsProblem409()
    {
        // Arrange
        var errors = new List<Error> { Error.Conflict("Transaction", "Duplicate transaction detected.") };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(409);
    }

    /// <summary>
    /// TC-07: Unauthorized error returns Problem(401).
    /// </summary>
    [Fact]
    public void TC07_UnauthorizedError_ReturnsProblem401()
    {
        // Arrange
        var errors = new List<Error> { Error.Unauthorized("Auth", "User is not authenticated.") };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(401);
    }

    /// <summary>
    /// TC-08: Forbidden error returns Problem(403).
    /// </summary>
    [Fact]
    public void TC08_ForbiddenError_ReturnsProblem403()
    {
        // Arrange
        var errors = new List<Error> { Error.Forbidden("Auth", "User does not have permission.") };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(403);
    }

    /// <summary>
    /// TC-09: Unexpected error returns Problem(500).
    /// </summary>
    [Fact]
    public void TC09_UnexpectedError_ReturnsProblem500()
    {
        // Arrange
        var errors = new List<Error> { Error.Unexpected("DB", "Database connection timed out.") };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(500);
    }

    /// <summary>
    /// TC-10: Failure error returns Problem(500).
    /// </summary>
    [Fact]
    public void TC10_FailureError_ReturnsProblem500()
    {
        // Arrange
        var errors = new List<Error> { Error.Failure("Service", "Downstream service unavailable.") };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(500);
    }

    /// <summary>
    /// TC-11: Empty error list returns Problem(500) with "An unknown error occurred." detail.
    /// </summary>
    [Fact]
    public void TC11_EmptyErrorList_ReturnsProblem500WithUnknownErrorDetail()
    {
        // Arrange
        var errors = new List<Error>();

        // Act
        var result = errors.ToHttpResult();

        // Assert
        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(500);
        problemResult.ProblemDetails.Detail.Should().Be("An unknown error occurred.");
    }

    /// <summary>
    /// TC-12: Multiple errors, first is NotFound — second being Validation does NOT affect result.
    /// </summary>
    [Fact]
    public void TC12_MultipleErrorsNotFoundFirst_Returns404IgnoringSecondError()
    {
        // Arrange
        var errors = new List<Error>
        {
            Error.NotFound("X", "not found"),
            Error.Validation("Y", "bad")
        };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(404);
    }

    /// <summary>
    /// TC-13: Mixed list with Validation first preserves error description as Problem detail.
    /// </summary>
    [Fact]
    public void TC13_MixedListValidationFirst_PreservesDescriptionAsProblemDetail()
    {
        // Arrange
        var errors = new List<Error>
        {
            Error.Validation("Field", "specific validation message"),
            Error.Conflict("X", "conflict")
        };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(400);
        problemResult.ProblemDetails.Detail.Should().Be("specific validation message");
    }

    #endregion

    #region Group 2 — ErrorOr<T>.ToHttpResult(onValue)

    /// <summary>
    /// TC-14: Success path — onValue factory is invoked with the correct value.
    /// </summary>
    [Fact]
    public void TC14_SuccessPath_OnValueFactoryInvokedWithCorrectValue()
    {
        // Arrange
        var item = new TestItem(42, "AAPL");
        ErrorOr<TestItem> result = item;

        // Act
        var httpResult = result.ToHttpResult(i => TypedResults.Ok(i));

        // Assert
        var okResult = httpResult.Should().BeOfType<Ok<TestItem>>().Subject;
        okResult.Value.Should().Be(item);
    }

    /// <summary>
    /// TC-15: Success path — custom factory returning Results.Created is returned as-is.
    /// </summary>
    [Fact]
    public void TC15_SuccessPath_CustomCreatedFactoryReturnsCreatedResult()
    {
        // Arrange
        var item = new TestItem(1, "TSLA");
        ErrorOr<TestItem> result = item;

        // Act
        var httpResult = result.ToHttpResult(i => TypedResults.Created($"/api/items/{i.Id}", i));

        // Assert
        var createdResult = httpResult.Should().BeOfType<Created<TestItem>>().Subject;
        createdResult.Location.Should().Be("/api/items/1");
        createdResult.Value.Should().Be(item);
    }

    /// <summary>
    /// TC-16: Error path — delegates to List&lt;Error&gt;.ToHttpResult(), returns Problem(404).
    /// </summary>
    [Fact]
    public void TC16_ErrorPath_DelegatesToListErrorToHttpResult()
    {
        // Arrange
        ErrorOr<TestItem> result = Error.NotFound("Item", "Not found.");
        var wasCalled = false;

        // Act
        var httpResult = result.ToHttpResult(_ =>
        {
            wasCalled = true;
            return TypedResults.Ok<TestItem>(null!);
        });

        // Assert
        httpResult.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(404);
        wasCalled.Should().BeFalse("onValue should not be invoked on error path");
    }

    /// <summary>
    /// TC-17: Error path with Validation errors returns ValidationProblem.
    /// </summary>
    [Fact]
    public void TC17_ErrorPathWithValidationErrors_ReturnsValidationProblem()
    {
        // Arrange
        ErrorOr<TestItem> result = Error.Validation("Name", "Name is required.");

        // Act
        var httpResult = result.ToHttpResult(i => TypedResults.Ok(i));

        // Assert
        var validationProblem = httpResult.Should().BeOfType<ValidationProblem>().Subject;
        validationProblem.ProblemDetails.Errors.Should().ContainKey("Name");
    }

    #endregion

    #region Group 3 — ValidateRequest<T>

    /// <summary>
    /// TC-18: Valid request returns ErrorOr with original value (IsError = false).
    /// </summary>
    [Fact]
    public async Task TC18_ValidRequest_ReturnsErrorOrWithOriginalValue()
    {
        // Arrange
        var validator = new InlineValidator<CreateRequest>();
        validator.RuleFor(x => x.Symbol).NotEmpty();
        validator.RuleFor(x => x.Amount).GreaterThan(0);
        var request = new CreateRequest("AAPL", 100m);

        // Act
        var result = await request.ValidateRequest(validator);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().Be(request);
    }

    /// <summary>
    /// TC-19: Single invalid field returns errors with Validation type.
    /// </summary>
    [Fact]
    public async Task TC19_SingleInvalidField_ReturnsErrorsWithValidationType()
    {
        // Arrange
        var validator = new InlineValidator<CreateRequest>();
        validator.RuleFor(x => x.Symbol).NotEmpty();
        var request = new CreateRequest("", 100m);

        // Act
        var result = await request.ValidateRequest(validator);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().AllSatisfy(e => e.Type.Should().Be(ErrorType.Validation));
    }

    /// <summary>
    /// TC-20: Multiple invalid fields returns all failures as separate Validation errors.
    /// </summary>
    [Fact]
    public async Task TC20_MultipleInvalidFields_ReturnsAllFailuresAsSeparateErrors()
    {
        // Arrange
        var validator = new InlineValidator<CreateRequest>();
        validator.RuleFor(x => x.Symbol).NotEmpty();
        validator.RuleFor(x => x.Amount).GreaterThan(0);
        var request = new CreateRequest("", -1m);

        // Act
        var result = await request.ValidateRequest(validator);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().HaveCountGreaterThanOrEqualTo(2);
        result.Errors.Should().Contain(e => e.Code == "Symbol");
        result.Errors.Should().Contain(e => e.Code == "Amount");
    }

    /// <summary>
    /// TC-21: Error.Code matches FluentValidation PropertyName exactly.
    /// </summary>
    [Fact]
    public async Task TC21_ErrorCode_MatchesPropertyNameExactly()
    {
        // Arrange
        var validator = new InlineValidator<CreateRequest>();
        validator.RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Must be positive.");
        var request = new CreateRequest("AAPL", 0m);

        // Act
        var result = await request.ValidateRequest(validator);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Single().Code.Should().Be("Amount");
    }

    /// <summary>
    /// TC-22: Error.Description matches FluentValidation ErrorMessage exactly.
    /// </summary>
    [Fact]
    public async Task TC22_ErrorDescription_MatchesErrorMessageExactly()
    {
        // Arrange
        var validator = new InlineValidator<CreateRequest>();
        validator.RuleFor(x => x.Symbol).NotEmpty().WithMessage("Symbol is required.");
        var request = new CreateRequest("", 50m);

        // Act
        var result = await request.ValidateRequest(validator);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Single().Description.Should().Be("Symbol is required.");
    }

    /// <summary>
    /// TC-23: Multiple failures on the same field returns multiple errors with same Code.
    /// </summary>
    [Fact]
    public async Task TC23_MultipleFailuresSameField_ReturnsMultipleErrorsWithSameCode()
    {
        // Arrange
        var validator = new InlineValidator<CreateRequest>();
        validator.RuleFor(x => x.Symbol).NotEmpty().WithMessage("Symbol is required.");
        validator.RuleFor(x => x.Symbol).MinimumLength(3).WithMessage("Symbol must be at least 3 characters.");
        var request = new CreateRequest("", 50m);

        // Act
        var result = await request.ValidateRequest(validator);

        // Assert
        result.IsError.Should().BeTrue();
        var symbolErrors = result.Errors.Where(e => e.Code == "Symbol").ToList();
        symbolErrors.Should().HaveCount(2);
        symbolErrors.Should().Contain(e => e.Description == "Symbol is required.");
        symbolErrors.Should().Contain(e => e.Description == "Symbol must be at least 3 characters.");
    }

    /// <summary>
    /// TC-24: Cancellation token is forwarded — cancelled token throws OperationCanceledException.
    /// </summary>
    [Fact]
    public async Task TC24_CancellationToken_IsForwardedAndThrowsWhenCancelled()
    {
        // Arrange
        var validator = new InlineValidator<CreateRequest>();
        validator.RuleFor(x => x.Symbol).NotEmpty();
        var request = new CreateRequest("AAPL", 100m);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await request.ValidateRequest(validator, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Test Types

    /// <summary>
    /// Test record for ErrorOr&lt;T&gt;.ToHttpResult tests.
    /// </summary>
    /// <param name="Id">The item identifier.</param>
    /// <param name="Name">The item name.</param>
    private sealed record TestItem(int Id, string Name);

    /// <summary>
    /// Test request record for ValidateRequest tests.
    /// </summary>
    /// <param name="Symbol">The symbol field.</param>
    /// <param name="Amount">The amount field.</param>
    private sealed record CreateRequest(string Symbol, decimal Amount);

    #endregion
}
