using Majetrack.Api.Infrastructure;
using Majetrack.Features;
using Majetrack.Features.Shared.Services;
using Majetrack.Infrastructure.ExternalServices.CnbExchangeRateProvider;
using Majetrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI / Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Problem Details and Exception Handling
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Database
builder.Services.AddDbContext<MajetrackDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("MajetrackDb"))
           .UseSnakeCaseNamingConvention());

// Exchange Rate Provider
builder.Services.Configure<CnbExchangeRateProviderOptions>(
    builder.Configuration.GetSection(CnbExchangeRateProviderOptions.SectionName));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IExchangeRateProvider, CnbExchangeRateProvider>();

// Features
var featuresAssembly = typeof(IFeatureConfiguration).Assembly;
builder.Services.AddFeatures(builder.Configuration, featuresAssembly);

// Current user identity — resolves UserId / Email from JWT claims
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// Auth placeholders (ordering matters for middleware pipeline)
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

var app = builder.Build();

// Global exception handler - must be first middleware to catch all downstream exceptions
app.UseExceptionHandler();

// Development middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MajetrackDbContext>();
    await db.Database.MigrateAsync();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<EnsureUserMiddleware>();

// Feature endpoints
app.MapFeatures(featuresAssembly);

app.Run();

/// <summary>
/// Partial class declaration required by <c>WebApplicationFactory&lt;T&gt;</c>
/// to enable integration testing of the application.
/// </summary>
public partial class Program { }
