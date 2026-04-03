using ErrorOr;
using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace Majetrack.Features.Shared.Extensions;

/// <summary>
/// Extension methods for mapping ErrorOr results to ASP.NET Core IResult HTTP responses,
/// and for validating request objects using FluentValidation within the ErrorOr pipeline.
/// </summary>
public static class ErrorOrEndpointExtensions
{
    /// <summary>
    /// Maps a non-empty list of <see cref="Error"/> values produced by an ErrorOr operation
    /// to the appropriate <see cref="IResult"/> HTTP response.
    /// The first error's <see cref="ErrorType"/> determines the HTTP status code.
    /// When all errors are of type <see cref="ErrorType.Validation"/>, all error descriptions
    /// are collected and returned as a RFC 7807 validation problem response.
    /// </summary>
    /// <param name="errors">
    /// The errors from a failed <c>ErrorOr</c> result. Must not be empty.
    /// </param>
    /// <returns>An <see cref="IResult"/> that produces the appropriate HTTP response.</returns>
    public static IResult ToHttpResult(this List<Error> errors)
    {
        if (errors.Count == 0)
        {
            return Results.Problem(statusCode: 500, detail: "An unknown error occurred.");
        }

        // All-Validation shortcut: structured ValidationProblem with field dictionary
        if (errors.All(e => e.Type == ErrorType.Validation))
        {
            var dict = errors
                .GroupBy(e => e.Code)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.Description).ToArray());
            return Results.ValidationProblem(dict);
        }

        // First-error dispatch for all other types
        return errors[0].Type switch
        {
            ErrorType.Validation => Results.Problem(statusCode: 400, detail: errors[0].Description),
            ErrorType.NotFound => Results.Problem(statusCode: 404, detail: errors[0].Description),
            ErrorType.Conflict => Results.Problem(statusCode: 409, detail: errors[0].Description),
            ErrorType.Unauthorized => Results.Problem(statusCode: 401, detail: errors[0].Description),
            ErrorType.Forbidden => Results.Problem(statusCode: 403, detail: errors[0].Description),
            _ => Results.Problem(statusCode: 500, detail: errors[0].Description),
        };
    }

    /// <summary>
    /// Maps an <see cref="ErrorOr{TValue}"/> result to an <see cref="IResult"/>.
    /// On success, invokes <paramref name="onValue"/> with the result value.
    /// On failure, delegates to <see cref="ToHttpResult(List{Error})"/>.
    /// </summary>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="result">The ErrorOr result to map.</param>
    /// <param name="onValue">Factory that produces the success IResult from the value.</param>
    /// <returns>An <see cref="IResult"/> for the HTTP response.</returns>
    public static IResult ToHttpResult<T>(this ErrorOr<T> result, Func<T, IResult> onValue) =>
        result.Match(
            value => onValue(value),
            errors => errors.ToHttpResult());

    /// <summary>
    /// Validates <paramref name="request"/> using the provided FluentValidation validator
    /// and returns the result as an <see cref="ErrorOr{T}"/>.
    /// On validation failure, each <see cref="FluentValidation.Results.ValidationFailure"/>
    /// is converted to an <see cref="ErrorType.Validation"/> error whose
    /// <see cref="Error.Code"/> is the property name and <see cref="Error.Description"/>
    /// is the failure message.
    /// On success, the original request is returned as the value.
    /// </summary>
    /// <typeparam name="T">The request type being validated.</typeparam>
    /// <param name="request">The request object to validate.</param>
    /// <param name="validator">The FluentValidation validator to run.</param>
    /// <param name="ct">Optional cancellation token forwarded to the validator.</param>
    /// <returns>
    /// <c>ErrorOr.From(request)</c> on success,
    /// or a list of <see cref="ErrorType.Validation"/> errors on failure.
    /// </returns>
    public static async Task<ErrorOr<T>> ValidateRequest<T>(
        this T request,
        IValidator<T> validator,
        CancellationToken ct = default)
    {
        var validation = await validator.ValidateAsync(request, ct);

        if (validation.IsValid)
        {
            return request;
        }

        return validation.Errors
            .ConvertAll(f => Error.Validation(f.PropertyName, f.ErrorMessage));
    }
}
