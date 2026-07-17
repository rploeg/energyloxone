using LoxoneSolarForecast.Data;
using LoxoneSolarForecast.Services;
using LoxoneSolarForecast.Workers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;

// ─────────────────────────────────────────────────
// Bootstrap Serilog early so startup errors are captured
// ─────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} — {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ─────────────────────────────────────────────
    // Serilog full config
    // ─────────────────────────────────────────────
    var configPath = builder.Configuration["Storage:ConfigPath"] ?? "/config";
    Directory.CreateDirectory(configPath);
    var logPath = Path.Combine(configPath, "logs", "app-.log");

    builder.Host.UseSerilog((ctx, services, loggerConfig) =>
    {
        loggerConfig
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentName()
            .Enrich.WithThreadId()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} — {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} — {Message:lj}{NewLine}{Exception}");
    });

    // ─────────────────────────────────────────────
    // Data Protection (persisted across container restarts)
    // ─────────────────────────────────────────────
    var dpPath = Path.Combine(configPath, "data-protection-keys");
    Directory.CreateDirectory(dpPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dpPath))
        .SetApplicationName("LoxoneSolarForecast");

    // ─────────────────────────────────────────────
    // SQLite / EF Core
    // ─────────────────────────────────────────────
    var dbPath = Path.Combine(configPath, "solar.db");
    builder.Services.AddDbContextFactory<AppDbContext>(options =>
        options.UseSqlite($"Data Source={dbPath}"));

    // ─────────────────────────────────────────────
    // Authentication (simple cookie-based)
    // ─────────────────────────────────────────────
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/Login";
            options.AccessDeniedPath = "/Login";
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;
        });

    builder.Services.AddAuthorization();

    // ─────────────────────────────────────────────
    // HTTP Clients
    // ─────────────────────────────────────────────
    builder.Services.AddHttpClient("OpenMeteo", client =>
    {
        client.BaseAddress = new Uri("https://api.open-meteo.com");
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Add("User-Agent", "LoxoneSolarForecast/1.0");
    });

    builder.Services.AddHttpClient("PVGIS", client =>
    {
        client.BaseAddress = new Uri("https://re.jrc.ec.europa.eu");
        client.Timeout = TimeSpan.FromSeconds(30);
    });

    // ─────────────────────────────────────────────
    // Application Services
    // ─────────────────────────────────────────────
    builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();
    builder.Services.AddSingleton<ISchedulerState, SchedulerState>();
    builder.Services.AddScoped<IWeatherService, OpenMeteoWeatherService>();
    builder.Services.AddScoped<ILearningService, LearningService>();
    builder.Services.AddScoped<IForecastService, ForecastService>();
    builder.Services.AddScoped<ILoxoneService, LoxoneService>();
    builder.Services.AddScoped<IRecommendationService, RecommendationService>();
    builder.Services.AddScoped<IDashboardService, DashboardService>();
    builder.Services.AddScoped<IHistoryService, HistoryService>();
    builder.Services.AddScoped<IInfluxDBService, InfluxDBService>();

    // ─────────────────────────────────────────────
    // Background Workers
    // ─────────────────────────────────────────────
    builder.Services.AddHostedService<DataCollectionWorker>();
    builder.Services.AddHostedService<ForecastWorker>();
    builder.Services.AddHostedService<LoxonePushWorker>();

    // ─────────────────────────────────────────────
    // Razor Pages & API Controllers
    // ─────────────────────────────────────────────
    builder.Services.AddRazorPages()
        .AddRazorPagesOptions(options =>
        {
            // Require auth on all pages except Login
            options.Conventions.AuthorizeFolder("/");
            options.Conventions.AllowAnonymousToPage("/Login");
        });

    builder.Services.AddControllers();

    // ─────────────────────────────────────────────
    // Health Checks
    // ─────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<AppDbContext>("database");

    // ─────────────────────────────────────────────
    // Anti-forgery
    // ─────────────────────────────────────────────
    builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

    // ─────────────────────────────────────────────
    // Build
    // ─────────────────────────────────────────────
    var app = builder.Build();

    // ─────────────────────────────────────────────
    // DB Migration on startup
    // ─────────────────────────────────────────────
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var ctx = await db.CreateDbContextAsync();
        await ctx.Database.EnsureCreatedAsync();
        Log.Information("Database ready: {Path}", dbPath);
    }

    // ─────────────────────────────────────────────
    // Middleware Pipeline
    // ─────────────────────────────────────────────
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} → {StatusCode} ({Elapsed:0.0}ms)";
        options.GetLevel = (ctx, elapsed, ex) =>
            ex != null || ctx.Response.StatusCode >= 500
                ? LogEventLevel.Error
                : ctx.Response.StatusCode >= 400
                    ? LogEventLevel.Warning
                    : LogEventLevel.Debug;
    });

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
    }

    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapRazorPages();
    app.MapControllers();
    app.MapHealthChecks("/health");

    Log.Information("LoxoneSolarForecast starting on port {Port}",
        builder.Configuration["ASPNETCORE_URLS"] ?? "5000");

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application startup failed");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
