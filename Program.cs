using DataAccess.Data;
using DataAccess.Repository;
using DataAccess.Repository.IRepository;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Statistics.Interfaces;
using Statistics.Services;
using TradingTools.Blazor.Components;
using TradingTools.Blazor.Services;
using TradingTools.Blazor.Services.Interfaces;
using Utilities.Trade;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Blazor Server's SignalR circuit defaults to a 32 KB max message size, which any screenshot
// upload blows past almost immediately - the circuit then dies rather than surfacing a normal
// error. Raise it comfortably above the 10 MB per-file cap used by the upload pages.
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
{
    options.MaximumReceiveMessageSize = 15 * 1024 * 1024;
});

builder.Services.AddMudServices();

ConfigureDatabase(builder);
AddServices(builder);

var app = builder.Build();

ApplyMigrations(app);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

// Uploaded screenshots are written into wwwroot at runtime, after the build-time static asset
// manifest is generated, so they need the classic file-system-backed static file middleware —
// MapStaticAssets() alone only knows about files that existed at build/publish time.
app.UseStaticFiles();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static void ApplyMigrations(WebApplication app)
{
    // Apply database migrations on startup (Production)
    if (!app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<Program>>();
        var dbContextFactory = services.GetRequiredService<IDbContextFactory>();

        try
        {
            using var context = dbContextFactory.CreateDbContext();

            logger.LogInformation("Applying PostgreSQL database migration(s)...");

            var pendingMigrations = context.Database.GetPendingMigrations();

            if (pendingMigrations.Any())
            {
                logger.LogInformation("Found {Count} pending migrations", pendingMigrations.Count());
                foreach (var migration in pendingMigrations)
                {
                    logger.LogInformation("  - {Migration}", migration);
                }

                context.Database.Migrate();
                logger.LogInformation("Database migrations applied successfully.");
            }
            else
            {
                logger.LogInformation("Database is up to date. No migrations to apply.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while migrating the database.");
            throw;
        }
    }
}

static void AddServices(WebApplicationBuilder builder)
{
    // Register DbContext factory for migrations
    builder.Services.AddSingleton<IDbContextFactory, DbContextFactory>();

    builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
    builder.Services.AddScoped<DeleteTradeService>();
    builder.Services.AddScoped<IStatisticsService, StatisticsService>();
    builder.Services.AddScoped<INewTradeService, NewTradeService>();
    builder.Services.AddScoped<ITradesService, TradesService>();
}

static void ConfigureDatabase(WebApplicationBuilder builder)
{
    var connectionString = builder.Configuration.GetConnectionString("PostgreSqlConnection")
        ?? throw new InvalidOperationException("PostgreSqlConnection string is missing.");

    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(connectionString, x => x.MigrationsAssembly("DataAccess")));
}
