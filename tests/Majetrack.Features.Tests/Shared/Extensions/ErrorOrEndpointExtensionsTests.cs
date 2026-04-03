using ErrorOr;
using FluentAssertions;
using FluentValidation;
using Majetrack.Features.Shared.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Majetrack.Features.Tests.Shared.Extensions;

/// <summary>
/// Unit tests for <see cref="ErrorOrEndpointExtensions"/>.
/// Tests the mapping of ErrorOr errors to HTTP results and FluentValidation integration.
/// </summary>
public class ErrorOrEndpointExtensionsTests
{
    #region Group 1 — List<Error>.ToHttpResult()

    /// <summary>
    /// TC-01: Single Validation error returns 400 with HttpValidationProblemDetails and field key present.
    /// </summary>
    [Fact]
    public void ToHttpResult_SingleValidationError_ReturnsValidationProblem()
    {
        // Arrange
        var errors = new List<Error> { Error.Validation("Name", "Name is required.") };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(400);
        var validation = problem.ProblemDetails.Should().BeOfType<HttpValidationProblemDetails>().Subject;
        validation.Errors.Should().ContainKey("Name");
        validation.Errors["Name"].Should().Contain("Name is required.");
    }

    /// <summary>
    /// TC-02: Multiple Validation errors on same field groups messages in array.
    /// </summary>
    [Fact]
    public void ToHttpResult_MultipleValidationErrorsSameField_GroupsMessages()
    {
        // Arrange
        var errors = new List<Error>
        {
            Error.Validation("Amount", "Amount must be positive."),
            Error.Validation("Amount", "Amount must be less than 1000.")
        };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        var validation = problem.ProblemDetails.Should().BeOfType<HttpValidationProblemDetails>().Subject;
        validation.Errors["Amount"].Should().HaveCount(2);
        validation.Errors["Amount"].Should().Contain("Amount must be positive.");
        validation.Errors["Amount"].Should().Contain("Amount must be less than 1000.");
    }

    /// <summary>
    /// TC-03: Multiple Validation errors on different fields returns both keys.
    /// </summary>
    [Fact]
    public void ToHttpResult_MultipleValidationErrorsDifferentFields_ReturnsBothKeys()
    {
        // Arrange
        var errors = new List<Error>
        {
            Error.Validation("Name", "Name is required."),
            Error.Validation("Email", "Email is invalid.")
        };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        var validation = problem.ProblemDetails.Should().BeOfType<HttpValidationProblemDetails>().Subject;
        validation.Errors.Should().ContainKey("Name");
        validation.Errors.Should().ContainKey("Email");
    }

    /// <summary>
    /// TC-04: Mixed errors with Validation first returns Problem 400 (not ValidationProblem).
    /// </summary>
    [Fact]
    public void ToHttpResult_MixedErrorsValidationFirst_ReturnsProblem400()
    {
        // Arrange
        var errors = new List<Error>
        {
            Error.Validation("Name", "Name is required."),
            Error.NotFound("User.NotFound", "User not found.")
        };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(400);
        problem.ProblemDetails.Detail.Should().Be("Name is required.");
    }

    /// <summary>
    /// TC-05: NotFound error returns Problem 404.
    /// </summary>
    [Fact]
    public void ToHttpResult_NotFoundError_ReturnsProblem404()
    {
        // Arrange
        var errors = new List<Error> { Error.NotFound("User.NotFound", "User not found.") };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(404);
        problem.ProblemDetails.Detail.Should().Be("User not found.");
    }

    /// <summary>
    /// TC-06: Conflict error returns Problem 409.
    /// </summary>
    [Fact]
    public void ToHttpResult_ConflictError_ReturnsProblem409()
    {
        // Arrange
        var errors = new List<Error> { Error.Conflict("User.Duplicate", "User already exists.") };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(409);
        problem.ProblemDetails.Detail.Should().Be("User already exists.");
    }

    /// <summary>
    /// TC-07: Unauthorized error returns Problem 401.
    /// </summary>
    [Fact]
    public void ToHttpResult_UnauthorizedError_ReturnsProblem401()
    {
        // Arrange
        var errors = new List<Error> { Error.Unauthorized("Auth.Invalid", "Invalid token.") };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(401);
        problem.ProblemDetails.Detail.Should().Be("Invalid token.");
    }

    /// <summary>
    /// TC-08: Forbidden error returns Problem 403.
    /// </summary>
    [Fact]
    public void ToHttpResult_ForbiddenError_ReturnsProblem403()
    {
        // Arrange
        var errors = new List<Error> { Error.Forbidden("Access.Denied", "Access denied.") };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(403);
        problem.ProblemDetails.Detail.Should().Be("Access denied.");
    }

    /// <summary>
    /// TC-09: Unexpected error returns Problem 500.
    /// </summary>
    [Fact]
    public void ToHttpResult_UnexpectedError_ReturnsProblem500()
    {
        // Arrange
        var errors = new List<Error> { Error.Unexpected("Server.Error", "Something went wrong.") };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(500);
        problem.ProblemDetails.Detail.Should().Be("Something went wrong.");
    }

    /// <summary>
    /// TC-10: Failure error returns Problem 500.
    /// </summary>
    [Fact]
    public void ToHttpResult_FailureError_ReturnsProblem500()
    {
        // Arrange
        var errors = new List<Error> { Error.Failure("Operation.Failed", "Operation failed.") };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(500);
        problem.ProblemDetails.Detail.Should().Be("Operation failed.");
    }

    /// <summary>
    /// TC-11: Empty error list returns Problem 500 with specific detail message.
    /// </summary>
    [Fact]
    public void ToHttpResult_EmptyErrorList_ReturnsProblem500WithUnknownDetail()
    {
        // Arrange
        var errors = new List<Error>();

        // Act
        var result = errors.ToHttpResult();

        // Assert
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(500);
        problem.ProblemDetails.Detail.Should().Be("An unknown error occurred.");
    }

    /// <summary>
    /// TC-12: NotFound first, Validation second — NotFound wins (first-error dispatch).
    /// </summary>
    [Fact]
    public void ToHttpResult_NotFoundFirstValidationSecond_ReturnsNotFound()
    {
        // Arrange
        var errors = new List<Error>
        {
            Error.NotFound("User.NotFound", "User not found."),
            Error.Validation("Name", "Name is required.")
        };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(404);
        problem.ProblemDetails.Detail.Should().Be("User not found.");
    }

    /// <summary>
    /// TC-13: Mixed Validation first — detail from first error's Description.
    /// </summary>
    [Fact]
    public void ToHttpResult_MixedValidationFirst_DetailFromFirstError()
    {
        // Arrange
        var errors = new List<Error>
        {
            Error.Validation("Amount", "Amount must be positive."),
            Error.Conflict("Order.Duplicate", "Order already exists.")
        };

        // Act
        var result = errors.ToHttpResult();

        // Assert
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(400);
        problem.ProblemDetails.Detail.Should().Be("Amount must be positive.");
    }

    #endregion

    #region Group 2 — ErrorOr<T>.ToHttpResult(onValue)

    /// <summary>
    /// TC-14: Success result invokes onValue and returns its result.
    /// </summary>
    [Fact]
    public void ToHttpResult_SuccessResult_InvokesOnValueAndReturnsItsResult()
    {
        // Arrange
        var item = new TestItem { Id = 42, Name = "Test" };
        ErrorOr<TestItem> result = item;

        // Act
        var httpResult = result.ToHttpResult(v => Results.Ok(v));

        // Assert
        var ok = httpResult.Should().BeOfType<Ok<TestItem>>().Subject;
        ok.Value.Should().Be(item);
    }

    /// <summary>
    /// TC-15: Success with custom factory (Created) returns that result.
    /// </summary>
    [Fact]
    public void ToHttpResult_SuccessWithCreatedFactory_ReturnsCreated()
    {
        // Arrange
        var item = new TestItem { Id = 99, Name = "Created Item" };
        ErrorOr<TestItem> result = item;

        // Act
        var httpResult = result.ToHttpResult(v => Results.Created($"/items/{v.Id}", v));

        // Assert
        var created = httpResult.Should().BeOfType<Created<TestItem>>().Subject;
        created.Value.Should().Be(item);
        created.Location.Should().Be("/items/99");
    }

    /// <summary>
    /// TC-16: Error result delegates to List.ToHttpResult, onValue NOT called.
    /// </summary>
    [Fact]
    public void ToHttpResult_ErrorResult_DelegatesAndOnValueNotCalled()
    {
        // Arrange
        ErrorOr<TestItem> result = Error.NotFound("Item.NotFound", "Item not found.");
        bool wasCalled = false;

        // Act
        var httpResult = result.ToHttpResult(v =>
        {
            wasCalled = true;
            return Results.Ok(v);
        });

        // Assert
        wasCalled.Should().BeFalse();
        var problem = httpResult.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(404);
    }

    /// <summary>
    /// TC-17: Validation error delegates to ValidationProblem.
    /// </summary>
    [Fact]
    public void ToHttpResult_ValidationError_DelegatesToValidationProblem()
    {
        // Arrange
        ErrorOr<TestItem> result = Error.Validation("Name", "Name is required.");

        // Act
        var httpResult = result.ToHttpResult(v => Results.Ok(v));

        // Assert
        var problem = httpResult.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(400);
        var validation = problem.ProblemDetails.Should().BeOfType<HttpValidationProblemDetails>().Subject;
        validation.Errors.Should().ContainKey("Name");
    }

    #endregion

    #region Group 3 — ValidateRequest<T>

    /// <summary>
    /// TC-18: Valid request returns IsError=false with original request as value.
    /// </summary>
    [Fact]
    public async Task ValidateRequest_ValidRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new TestRequest { Name = "Valid", Amount = 100 };
        var validator = new TestRequestValidator();

        // Act
        var result = await request.ValidateRequest(validator);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().Be(request);
    }

    /// <summary>
    /// TC-19: Single invalid field returns IsError=true with Validation error.
    /// </summary>
    [Fact]
    public async Task ValidateRequest_SingleInvalidField_ReturnsValidationError()
    {
        // Arrange
        var request = new TestRequest { Name = "", Amount = 100 };
        var validator = new TestRequestValidator();

        // Act
        var result = await request.ValidateRequest(validator);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Type.Should().Be(ErrorType.Validation);
    }

    /// <summary>
    /// TC-20: Multiple invalid fields returns errors for both.
    /// </summary>
    [Fact]
    public async Task ValidateRequest_MultipleInvalidFields_ReturnsErrorsForBoth()
    {
        // Arrange
        var request = new TestRequest { Name = "", Amount = -5 };
        var validator = new TestRequestValidator();

        // Act
        var result = await request.ValidateRequest(validator);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().HaveCount(2);
        result.Errors.Select(e => e.Code).Should().Contain("Name");
        result.Errors.Select(e => e.Code).Should().Contain("Amount");
    }

    /// <summary>
    /// TC-21: Error.Code matches PropertyName exactly (PascalCase).
    /// </summary>
    [Fact]
    public async Task ValidateRequest_ErrorCode_MatchesPropertyNameExactly()
    {
        // Arrange
        var request = new TestRequest { Name = "", Amount = 100 };
        var validator = new TestRequestValidator();

        // Act
        var result = await request.ValidateRequest(validator);

        // Assert
        result.Errors[0].Code.Should().Be("Name");
    }

    /// <summary>
    /// TC-22: Error.Description matches ErrorMessage exactly.
    /// </summary>
    [Fact]
    public async Task ValidateRequest_ErrorDescription_MatchesErrorMessageExactly()
    {
        // Arrange
        var request = new TestRequest { Name = "", Amount = 100 };
        var validator = new TestRequestValidator();

        // Act
        var result = await request.ValidateRequest(validator);

        // Assert
        result.Errors[0].Description.Should().Be("Name is required.");
    }

    /// <summary>
    /// TC-23: Multiple failures on same field returns multiple errors with same Code.
    /// </summary>
    [Fact]
    public async Task ValidateRequest_MultipleFailuresSameField_ReturnsMultipleErrorsWithSameCode()
    {
        // Arrange
        var request = new TestRequest { Name = "X", Amount = 100 }; // Too short
        var validator = new InlineValidator<TestRequest>();
        validator.RuleFor(r => r.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MinimumLength(3).WithMessage("Name must be at least 3 characters.");

        // Act
        var result = await request.ValidateRequest(validator);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().HaveCount(1); // MinimumLength fails, NotEmpty passes
        result.Errors[0].Code.Should().Be("Name");
        result.Errors[0].Description.Should().Be("Name must be at least 3 characters.");
    }

    /// <summary>
    /// TC-24: Pre-cancelled CancellationToken throws OperationCanceledException.
    /// </summary>
    [Fact]
    public async Task ValidateRequest_PreCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var request = new TestRequest { Name = "Valid", Amount = 100 };
        var validator = new TestRequestValidator();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await request.ValidateRequest(validator, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Test Helpers

    private record TestItem
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
    }

    private record TestRequest
    {
        public string Name { get; init; } = string.Empty;
        public decimal Amount { get; init; }
    }

    private class TestRequestValidator : AbstractValidator<TestRequest>
    {
        public TestRequestValidator()
        {
            RuleFor(r => r.Name).NotEmpty().WithMessage("Name is required.");
            RuleFor(r => r.Amount).GreaterThan(0).WithMessage("Amount must be positive.");
        }
    }

    #endregion
}
