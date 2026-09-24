using System.Globalization;
using DataAccess.Data;
using DataAccess.Repository;
using DataAccess.Repository.IRepository;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Models;
using MudBlazor.Services;
using Radzen;
using Statistics.Interfaces;
using Statistics.Services;
using TradingTools.Blazor.Components;
using TradingTools.Blazor.Services;
using TradingTools.Blazor.Services.Interfaces;
using Utilities.Trade;

// Library UI text (e.g. the rich text editor toolbar) follows the UI culture, which otherwise comes from
// the OS (German). Only the UI culture is pinned, so number/date formatting is left as it was.
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");

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
builder.Services.AddRadzenComponents();

// Mirrors the Razor Pages app's authentication: cookie-based Identity. The login wall itself is
// enforced in MainLayout (see its OnInitializedAsync) rather than as an endpoint-level fallback
// policy - a global RequireAuthenticatedUser() fallback also catches Blazor's own infrastructure
// endpoints (_framework/blazor.web.js, the SignalR hub), which share one endpoint mapping with every
// page and can't be exempted individually, breaking the app for anonymous visitors.
builder.Services.AddCascadingAuthenticationState();

ConfigureDatabase(builder);
ConfigureIdentity(builder);
AddServices(builder);

var app = builder.Build();

ApplyMigrations(app);
ApplySymbolDataFix(app);

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

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Plain GET+POST endpoints (not a Razor component) so the topbar's account menu can log out with a
// real <form> post, exactly like the Razor Pages app's /Account/Logout page did.
app.MapMethods("/account/logout", ["GET", "POST"], async (SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.LocalRedirect("/account/login");
});

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

// One-time data fixup for the ESymbol rename (SP -> US500, see MyEnumConverter.SymbolFromEnum): any
// trade saved with the old "S&P" free-text/enum value is updated to the new "US500" one. Runs on every
// startup (Development included, unlike ApplyMigrations) but the WHERE clause makes it a no-op once
// the affected rows are fixed, so it's safe to leave in place.
static void ApplySymbolDataFix(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory>();

    try
    {
        using var context = dbContextFactory.CreateDbContext();

        var updated = context.Database.ExecuteSqlRaw(
            """UPDATE "BaseTrades" SET "Symbol" = 'US500' WHERE "Symbol" = 'S&P'""");

        if (updated > 0)
        {
            logger.LogInformation("Updated {Count} trade(s) with Symbol 'S&P' to 'US500'.", updated);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while updating the 'S&P' Symbol value to 'US500'.");
        throw;
    }
}

static void ConfigureIdentity(WebApplicationBuilder builder)
{
    builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // Password settings
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 6;

        // Lockout settings
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(30); // Cookie expires after 30 days when RememberMe is checked
        options.SlidingExpiration = true; // Renew cookie on activity
        options.LoginPath = "/account/login";
        options.LogoutPath = "/account/logout";
        options.AccessDeniedPath = "/account/login";
    });
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

    // Journal/Review text is stored as HTML from the rich text editor - sanitized before saving.
    builder.Services.AddSingleton<Ganss.Xss.IHtmlSanitizer, Ganss.Xss.HtmlSanitizer>();
}

static void ConfigureDatabase(WebApplicationBuilder builder)
{
    var connectionString = builder.Configuration.GetConnectionString("PostgreSqlConnection")
        ?? throw new InvalidOperationException("PostgreSqlConnection string is missing.");

    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(connectionString, x => x.MigrationsAssembly("DataAccess")));
}
