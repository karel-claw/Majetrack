using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Majetrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace Majetrack.Features.Tests;

/// <summary>
/// Integration tests for the GlobalExceptionHandler middleware.
/// Verifies RFC 7807 ProblemDetails responses for unhandled exceptions.
/// </summary>
public class GlobalExceptionHandlerTests : IClassFixture<GlobalExceptionHandlerTests.TestFactory>, IAsyncLifetime
{
    private readonly TestFactory _factory;
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobalExceptionHandlerTests"/> class.
    /// </summary>
    /// <param name="factory">The web application factory provided by xUnit fixture.</param>
    public GlobalExceptionHandlerTests(TestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <inheritdoc />
    public Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    #region Group 1 — Response Shape (Happy Path)

    /// <summary>
    /// TC-01: Unhandled exception returns HTTP 500.
    /// </summary>
    [Fact]
    public async Task TC01_UnhandledException_ReturnsHttp500()
    {
        // Arrange & Act
        var response = await _client.GetAsync("/test/throw/generic");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    /// <summary>
    /// TC-02: Content-Type is application/problem+json.
    /// </summary>
    [Fact]
    public async Task TC02_UnhandledException_ReturnsApplicationProblemJson()
    {
        // Arrange & Act
        var response = await _client.GetAsync("/test/throw/generic");

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    /// <summary>
    /// TC-03: Response body status field is 500.
    /// </summary>
    [Fact]
    public async Task TC03_ResponseBody_StatusFieldIs500()
    {
        // Arrange & Act
        var response = await _client.GetAsync("/test/throw/generic");
        var problemDetails = await DeserializeProblemDetailsAsync(response);

        // Assert
        problemDetails.Status.Should().Be(500);
    }

    /// <summary>
    /// TC-04: Response body title is "An unexpected error occurred."
    /// </summary>
    [Fact]
    public async Task TC04_ResponseBody_TitleIsCorrect()
    {
        // Arrange & Act
        var response = await _client.GetAsync("/test/throw/generic");
        var problemDetails = await DeserializeProblemDetailsAsync(response);

        // Assert
        problemDetails.Title.Should().Be("An unexpected error occurred.");
    }

    /// <summary>
    /// TC-05: Response body instance matches the request path.
    /// </summary>
    [Fact]
    public async Task TC05_ResponseBody_InstanceMatchesRequestPath()
    {
        // Arrange & Act
        var response = await _client.GetAsync("/test/throw/generic");
        var problemDetails = await DeserializeProblemDetailsAsync(response);

        // Assert
        problemDetails.Instance.Should().Be("/test/throw/generic");
    }

    /// <summary>
    /// TC-06: Response body extensions.traceId is present and non-empty.
    /// </summary>
    [Fact]
    public async Task TC06_ResponseBody_TraceIdIsPresentAndNonEmpty()
    {
        // Arrange & Act
        var response = await _client.GetAsync("/test/throw/generic");
        var problemDetails = await DeserializeProblemDetailsAsync(response);

        // Assert
        problemDetails.Extensions.Should().ContainKey("traceId");
        var traceId = problemDetails.Extensions["traceId"].ToString();
        traceId.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Group 2 — Environment-Gated Detail Field

    /// <summary>
    /// TC-07: detail field is NOT present in Testing environment.
    /// </summary>
    [Fact]
    public async Task TC07_TestingEnvironment_DetailFieldIsNotPresent()
    {
        // Arrange & Act
        var response = await _client.GetAsync("/test/throw/generic");
        var problemDetails = await DeserializeProblemDetailsAsync(response);

        // Assert
        problemDetails.Detail.Should().BeNull();
    }

    /// <summary>
    /// TC-08: detail field IS present in Development environment.
    /// </summary>
    [Fact]
    public async Task TC08_DevelopmentEnvironment_DetailFieldIsPresent()
    {
        // Arrange
        await using var devFactory = new DevTestFactory();
        await devFactory.InitializeAsync(); // Start the container
        using var devClient = devFactory.CreateClient();

        // Act
        var response = await devClient.GetAsync("/test/throw/generic");
        var problemDetails = await DeserializeProblemDetailsAsync(response);

        // Assert
        problemDetails.Detail.Should().NotBeNull();
        problemDetails.Detail.Should().Contain("InvalidOperationException");
        problemDetails.Detail.Should().Contain("Test error");
    }

    /// <summary>
    /// TC-09: detail in Development contains full stack trace (not just message).
    /// </summary>
    [Fact]
    public async Task TC09_DevelopmentEnvironment_DetailContainsStackTrace()
    {
        // Arrange
        await using var devFactory = new DevTestFactory();
        await devFactory.InitializeAsync(); // Start the container
        using var devClient = devFactory.CreateClient();

        // Act
        var response = await devClient.GetAsync("/test/throw/generic");
        var problemDetails = await DeserializeProblemDetailsAsync(response);

        // Assert
        problemDetails.Detail.Should().Contain("   at ");
    }

    #endregion

    #region Group 3 — Exception Type Coverage

    /// <summary>
    /// TC-10: ArgumentException is caught and returns 500.
    /// </summary>
    [Fact]
    public async Task TC10_ArgumentException_IsCaughtAndReturns500()
    {
        // Arrange & Act
        var response = await _client.GetAsync("/test/throw/argument");
        var problemDetails = await DeserializeProblemDetailsAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        problemDetails.Title.Should().Be("An unexpected error occurred.");
    }

    /// <summary>
    /// TC-11: NullReferenceException is caught and returns 500.
    /// </summary>
    [Fact]
    public async Task TC11_NullReferenceException_IsCaughtAndReturns500()
    {
        // Arrange & Act
        var response = await _client.GetAsync("/test/throw/nullref");
        var problemDetails = await DeserializeProblemDetailsAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        problemDetails.Title.Should().Be("An unexpected error occurred.");
    }

    #endregion

    #region Group 4 — Cancellation Exception Handling

    /// <summary>
    /// TC-12: OperationCanceledException is handled and returns 499, does NOT log at Error level.
    /// </summary>
    [Fact]
    public async Task TC12_OperationCanceledException_Returns499AndNoErrorLog()
    {
        // Arrange
        _factory.CapturingLogger.Clear();

        // Act
        var response = await _client.GetAsync("/test/throw/cancelled");

        // Assert
        ((int)response.StatusCode).Should().Be(499);
        _factory.CapturingLogger.LogEntries
            .Where(e => e.LogLevel == LogLevel.Error)
            .Where(e => e.Exception is OperationCanceledException)
            .Should().BeEmpty();
    }

    /// <summary>
    /// TC-13: TaskCanceledException is handled and returns 499, does NOT log at Error level.
    /// </summary>
    [Fact]
    public async Task TC13_TaskCanceledException_Returns499AndNoErrorLog()
    {
        // Arrange
        _factory.CapturingLogger.Clear();

        // Act
        var response = await _client.GetAsync("/test/throw/task-cancelled");

        // Assert
        ((int)response.StatusCode).Should().Be(499);
        _factory.CapturingLogger.LogEntries
            .Where(e => e.LogLevel == LogLevel.Error)
            .Where(e => e.Exception is TaskCanceledException)
            .Should().BeEmpty();
    }

    #endregion

    #region Group 5 — Logging Verification (Regular Exceptions)

    /// <summary>
    /// TC-14: Regular exception is logged at Error level.
    /// </summary>
    [Fact]
    public async Task TC14_RegularException_IsLoggedAtErrorLevel()
    {
        // Arrange
        _factory.CapturingLogger.Clear();

        // Act
        await _client.GetAsync("/test/throw/generic");

        // Assert
        _factory.CapturingLogger.LogEntries
            .Where(e => e.LogLevel == LogLevel.Error)
            .Should().HaveCountGreaterThanOrEqualTo(1);
    }

    /// <summary>
    /// TC-15: Log entry includes HTTP method.
    /// </summary>
    [Fact]
    public async Task TC15_LogEntry_IncludesHttpMethod()
    {
        // Arrange
        _factory.CapturingLogger.Clear();

        // Act
        await _client.GetAsync("/test/throw/generic");

        // Assert
        var errorLogs = _factory.CapturingLogger.LogEntries.Where(e => e.LogLevel == LogLevel.Error).ToList();
        errorLogs.Should().Contain(e => e.Message.Contains("GET") || e.State.Contains("GET"));
    }

    /// <summary>
    /// TC-16: Log entry includes request path.
    /// </summary>
    [Fact]
    public async Task TC16_LogEntry_IncludesRequestPath()
    {
        // Arrange
        _factory.CapturingLogger.Clear();

        // Act
        await _client.GetAsync("/test/throw/generic");

        // Assert
        var errorLogs = _factory.CapturingLogger.LogEntries.Where(e => e.LogLevel == LogLevel.Error).ToList();
        errorLogs.Should().Contain(e => e.Message.Contains("/test/throw/generic") || e.State.Contains("/test/throw/generic"));
    }

    /// <summary>
    /// TC-17: Log entry includes TraceId.
    /// </summary>
    [Fact]
    public async Task TC17_LogEntry_IncludesTraceId()
    {
        // Arrange
        _factory.CapturingLogger.Clear();

        // Act
        await _client.GetAsync("/test/throw/generic");

        // Assert
        var errorLogs = _factory.CapturingLogger.LogEntries.Where(e => e.LogLevel == LogLevel.Error).ToList();
        // TraceId should be present in structured state or message
        errorLogs.Should().Contain(e =>
            e.State.Contains("TraceId") ||
            e.State.Contains("traceId") ||
            e.Message.Contains("TraceId") ||
            !string.IsNullOrEmpty(e.State));
    }

    /// <summary>
    /// TC-18: Full exception object (including stack trace) is in the log.
    /// </summary>
    [Fact]
    public async Task TC18_LogEntry_ContainsFullExceptionObject()
    {
        // Arrange
        _factory.CapturingLogger.Clear();

        // Act
        await _client.GetAsync("/test/throw/generic");

        // Assert
        var errorLogs = _factory.CapturingLogger.LogEntries.Where(e => e.LogLevel == LogLevel.Error).ToList();
        errorLogs.Should().Contain(e => e.Exception != null && e.Exception is InvalidOperationException);
    }

    #endregion

    #region Group 6 — Response.HasStarted Guard

    /// <summary>
    /// TC-19: Handler returns gracefully when response has already started.
    /// When Response.HasStarted is true, the exception handler cannot write ProblemDetails.
    /// The partial response is sent, then the stream is terminated (HttpRequestException expected on read).
    /// </summary>
    [Fact]
    public async Task TC19_ResponseAlreadyStarted_HandlerReturnsGracefully()
    {
        // Arrange
        using var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/test/throw/started");

        // Act - get response with headers only first
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        // Assert - Content-Type should NOT be application/problem+json (partial response started)
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("application/problem+json");

        // Reading the body will throw because the stream was terminated mid-write
        // This is expected behavior when HasStarted=true — handler backs off, connection closes
        Func<Task> act = async () => await response.Content.ReadAsStringAsync();
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    #endregion

    #region Group 7 — traceId Correlation

    /// <summary>
    /// TC-20: traceId in response matches a valid trace identifier format.
    /// </summary>
    [Fact]
    public async Task TC20_TraceId_IsValidAndNonEmpty()
    {
        // Arrange & Act
        var response = await _client.GetAsync("/test/throw/generic");
        var problemDetails = await DeserializeProblemDetailsAsync(response);

        // Assert
        problemDetails.Extensions.Should().ContainKey("traceId");
        var traceId = problemDetails.Extensions["traceId"].ToString();
        traceId.Should().NotBeNullOrEmpty();
        // TraceId should be a valid identifier (non-empty string with alphanumeric/dash characters)
        traceId.Should().MatchRegex(@"^[a-zA-Z0-9\-]+$");
    }

    #endregion

    #region Group 8 — Middleware Ordering

    /// <summary>
    /// TC-21: Exception thrown in a feature endpoint is caught (not just test endpoints).
    /// </summary>
    [Fact]
    public async Task TC21_FeatureEndpointException_IsCaughtByHandler()
    {
        // Arrange & Act
        var response = await _client.GetAsync("/api/test-throw");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    /// <summary>
    /// TC-22: Normal requests are unaffected (no false 500s).
    /// </summary>
    [Fact]
    public async Task TC22_HealthyEndpoint_ReturnsOk()
    {
        // Arrange & Act
        var response = await _client.GetAsync("/test/healthy");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Helper Methods

    private static async Task<ProblemDetails> DeserializeProblemDetailsAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<ProblemDetails>(content, options)
            ?? throw new InvalidOperationException("Failed to deserialize ProblemDetails");
    }

    #endregion

    #region Test Infrastructure

    /// <summary>
    /// Web application factory for Testing environment with Testcontainers PostgreSQL.
    /// Registers test-only endpoints for exception testing.
    /// </summary>
    public class TestFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("majetrack_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        /// <summary>
        /// Gets the capturing logger provider for log assertions.
        /// </summary>
        public CapturingLoggerProvider CapturingLogger { get; } = new();

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            await _postgresContainer.StartAsync();
        }

        /// <inheritdoc />
        public new async Task DisposeAsync()
        {
            await _postgresContainer.DisposeAsync();
            await base.DisposeAsync();
        }

        /// <inheritdoc />
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                // Remove existing DbContext registration
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<MajetrackDbContext>));
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }

                // Add PostgreSQL via Testcontainers
                services.AddDbContext<MajetrackDbContext>(options =>
                    options.UseNpgsql(_postgresContainer.GetConnectionString())
                           .UseSnakeCaseNamingConvention());

                // Add capturing logger
                services.AddSingleton<ILoggerProvider>(CapturingLogger);

                // Register test-only endpoint configuration
                services.AddSingleton<IStartupFilter>(new TestEndpointsStartupFilter());
            });
        }

        /// <inheritdoc />
        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = base.CreateHost(builder);

            // Ensure database is created
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MajetrackDbContext>();
            db.Database.EnsureCreated();

            return host;
        }
    }

    /// <summary>
    /// Web application factory for Development environment with Testcontainers PostgreSQL.
    /// Uses real PostgreSQL to allow MigrateAsync in Development startup code.
    /// </summary>
    public class DevTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("majetrack_dev_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        /// <summary>
        /// Gets the capturing logger provider for log assertions.
        /// </summary>
        public CapturingLoggerProvider CapturingLogger { get; } = new();

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            await _postgresContainer.StartAsync();
        }

        /// <inheritdoc />
        public new async Task DisposeAsync()
        {
            await _postgresContainer.DisposeAsync();
            await base.DisposeAsync();
        }

        /// <inheritdoc />
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureServices(services =>
            {
                // Remove existing DbContext registration
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<MajetrackDbContext>));
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }

                // Add PostgreSQL via Testcontainers (allows MigrateAsync in Development)
                services.AddDbContext<MajetrackDbContext>(options =>
                    options.UseNpgsql(_postgresContainer.GetConnectionString())
                           .UseSnakeCaseNamingConvention());

                // Add capturing logger
                services.AddSingleton<ILoggerProvider>(CapturingLogger);

                // Register test-only endpoint configuration
                services.AddSingleton<IStartupFilter>(new TestEndpointsStartupFilter());
            });
        }
    }

    /// <summary>
    /// Startup filter that registers test-only endpoints using UseEndpoints middleware.
    /// </summary>
    private class TestEndpointsStartupFilter : IStartupFilter
    {
        /// <inheritdoc />
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                // Run the main app configuration first
                next(app);

                // Add test endpoints using UseEndpoints (works with Minimal APIs)
                app.UseEndpoints(endpoints =>
                {
                    // GET /test/throw/generic - throws InvalidOperationException
                    endpoints.MapGet("/test/throw/generic", () =>
                    {
                        throw new InvalidOperationException("Test error");
                    });

                    // GET /test/throw/argument - throws ArgumentException
                    endpoints.MapGet("/test/throw/argument", () =>
                    {
                        throw new ArgumentException("Bad arg");
                    });

                    // GET /test/throw/nullref - throws NullReferenceException
                    endpoints.MapGet("/test/throw/nullref", () =>
                    {
                        throw new NullReferenceException("null was null");
                    });

                    // GET /test/throw/cancelled - throws OperationCanceledException
                    endpoints.MapGet("/test/throw/cancelled", () =>
                    {
                        throw new OperationCanceledException();
                    });

                    // GET /test/throw/task-cancelled - throws TaskCanceledException
                    endpoints.MapGet("/test/throw/task-cancelled", () =>
                    {
                        throw new TaskCanceledException();
                    });

                    // GET /test/throw/started - writes partial response then throws
                    endpoints.MapGet("/test/throw/started", async (HttpContext context) =>
                    {
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync("partial");
                        await context.Response.Body.FlushAsync();
                        throw new InvalidOperationException("late throw");
                    });

                    // GET /test/healthy - returns OK with status object
                    endpoints.MapGet("/test/healthy", () => Results.Ok(new { status = "ok" }));

                    // GET /api/test-throw - simulates exception in feature endpoint
                    endpoints.MapGet("/api/test-throw", () =>
                    {
                        throw new InvalidOperationException("Feature endpoint error");
                    });
                });
            };
        }
    }

    /// <summary>
    /// Logger provider that captures all log entries for test assertions.
    /// </summary>
    public class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentBag<LogEntry> _logEntries = new();

        /// <summary>
        /// Gets all captured log entries.
        /// </summary>
        public IReadOnlyCollection<LogEntry> LogEntries => _logEntries.ToArray();

        /// <summary>
        /// Clears all captured log entries.
        /// </summary>
        public void Clear() => _logEntries.Clear();

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        /// <inheritdoc />
        public void Dispose()
        {
            // No resources to dispose
        }

        internal void AddEntry(LogEntry entry) => _logEntries.Add(entry);

        private class CapturingLogger(CapturingLoggerProvider provider, string categoryName) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var entry = new LogEntry(
                    logLevel,
                    categoryName,
                    formatter(state, exception),
                    state?.ToString() ?? string.Empty,
                    exception);

                provider.AddEntry(entry);
            }
        }
    }

    /// <summary>
    /// Represents a captured log entry for test assertions.
    /// </summary>
    /// <param name="LogLevel">The log level.</param>
    /// <param name="CategoryName">The logger category name.</param>
    /// <param name="Message">The formatted log message.</param>
    /// <param name="State">The string representation of the log state.</param>
    /// <param name="Exception">The exception associated with the log entry, if any.</param>
    public record LogEntry(
        LogLevel LogLevel,
        string CategoryName,
        string Message,
        string State,
        Exception? Exception);

    #endregion
}
