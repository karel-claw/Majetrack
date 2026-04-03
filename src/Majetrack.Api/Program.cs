using Majetrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddDbContext<MajetrackDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("MajetrackDb"))
           .UseSnakeCaseNamingConvention());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MajetrackDbContext>();
    await db.Database.MigrateAsync();
}

app.UseHttpsRedirection();

app.Run();
