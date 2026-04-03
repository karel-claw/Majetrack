using Majetrack.Features;
using Majetrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI / Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database
builder.Services.AddDbContext<MajetrackDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("MajetrackDb"))
           .UseSnakeCaseNamingConvention());

// Features
var featuresAssembly = typeof(IFeatureConfiguration).Assembly;
builder.Services.AddFeatures(builder.Configuration, featuresAssembly);

// Auth placeholders (ordering matters for middleware pipeline)
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

var app = builder.Build();

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

// Feature endpoints
app.MapFeatures(featuresAssembly);

app.Run();

/// <summary>
/// Partial class declaration required by <c>WebApplicationFactory&lt;T&gt;</c>
/// to enable integration testing of the application.
/// </summary>
public partial class Program { }
