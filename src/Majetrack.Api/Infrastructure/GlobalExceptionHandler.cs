using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Majetrack.Api.Infrastructure;

/// <summary>
/// Global exception handler that catches all unhandled exceptions and returns
/// an RFC 7807 ProblemDetails response. Stack traces are exposed only in the
/// Development environment; all exceptions are logged before responding.
/// </summary>
/// <param name="logger">The logger instance for recording exception details.</param>
/// <param name="problemDetailsService">The service for writing ProblemDetails responses.</param>
/// <param name="environment">The host environment for determining detail exposure.</param>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment) : IExceptionHandler
{
    /// <summary>
    /// HTTP status code for client-closed requests (nginx convention).
    /// Used for cancelled requests to distinguish from server errors.
    /// </summary>
    private const int ClientClosedRequestStatusCode = 499;

    /// <summary>
    /// Attempts to handle an unhandled exception by logging it and writing
    /// an appropriate ProblemDetails response.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="exception">The unhandled exception.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> if the exception was handled; <c>false</c> if the response
    /// has already started and cannot be modified.
    /// </returns>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Guard: cannot write response if it has already started
        if (httpContext.Response.HasStarted)
        {
            logger.LogError(
                exception,
                "Unhandled exception after response started. Method={Method} Path={Path} TraceId={TraceId}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                httpContext.TraceIdentifier);
            return false;
        }

        // Handle cancellation exceptions specially - not server errors
        if (exception is OperationCanceledException)
        {
            logger.LogInformation(
                exception,
                "Request cancelled. Method={Method} Path={Path} TraceId={TraceId}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                httpContext.TraceIdentifier);

            httpContext.Response.StatusCode = ClientClosedRequestStatusCode;
            return true;
        }

        // Log all other exceptions at Error level with full context
        logger.LogError(
            exception,
            "Unhandled exception. Method={Method} Path={Path} TraceId={TraceId}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            httpContext.TraceIdentifier);

        // Build ProblemDetails response
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Instance = httpContext.Request.Path,
            Detail = environment.IsDevelopment() ? exception.ToString() : null
        };

        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails
        });

        return true;
    }
}
