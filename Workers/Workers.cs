using LoxoneSolarForecast.Data;
using LoxoneSolarForecast.Models.Entities;
using LoxoneSolarForecast.Services;
using Microsoft.EntityFrameworkCore;

namespace LoxoneSolarForecast.Workers;

/// <summary>
/// Collects data from Loxone at per-source configured intervals and stores it in SQLite.
/// Polls every minute but only collects from sources whose interval has passed.
/// </summary>
public class DataCollectionWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DataCollectionWorker> _logger;
    private readonly Dictionary<Guid, DateTime> _lastCollectionTimes = new();
    private readonly Dictionary<string, DateTime> _lastHomeWizardCollection = new();
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMinutes(1); // Check every minute

    public DataCollectionWorker(IServiceProvider services, ILogger<DataCollectionWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DataCollectionWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _services.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var loxoneService = scope.ServiceProvider.GetRequiredService<ILoxoneService>();
            var learningService = scope.ServiceProvider.GetRequiredService<ILearningService>();
            var schedulerState = scope.ServiceProvider.GetRequiredService<ISchedulerState>();

            var config = configService.GetConfiguration();
            var now = DateTime.UtcNow;

            try
            {
                // Loxone data collection
                var sourcesToCollect = config.LoxoneDataSources
                    .Where(ds => ds.IsActive && ShouldCollect(ds.Id, ds.UpdateIntervalMinutes, now))
                    .ToList();

                if (sourcesToCollect.Any())
                {
                    schedulerState.UpdateDataCollection(true, null, null);
                    _logger.LogDebug($"Collecting data from {sourcesToCollect.Count} Loxone sources");

                    // Collect all active Loxone sources
                    await loxoneService.CollectDataAsync();

                    // Update collection times for sources that were collected
                    foreach (var source in sourcesToCollect)
                    {
                        _lastCollectionTimes[source.Id] = now;
                    }

                    var nextMinInterval = config.LoxoneDataSources
                        .Where(ds => ds.IsActive)
                        .Min(ds => ds.UpdateIntervalMinutes);
                    schedulerState.UpdateDataCollection(false, now, now.AddMinutes(nextMinInterval));

                    // Update learning data once per day (around midnight)
                    if (now.Hour == 0 && now.Minute < 2)
                    {
                        await UpdateYesterdayLearningAsync(scope.ServiceProvider, learningService);
                    }
                }

                // HomeWizard water meter collection (independent schedule)
                if (config.HomeWizard?.Enabled == true && ShouldCollectHomeWizard(now, config.HomeWizard.UpdateIntervalMinutes))
                {
                    try
                    {
                        var homeWizardCollector = scope.ServiceProvider.GetRequiredService<IHomeWizardCollector>();
                        _logger.LogDebug("Collecting HomeWizard water data");
                        await homeWizardCollector.CollectAsync();
                        _lastHomeWizardCollection["HomeWizard"] = now;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "HomeWizard collection failed");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in DataCollectionWorker");
                schedulerState.UpdateDataCollection(false, null, null);
            }

            try
            {
                await Task.Delay(PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("DataCollectionWorker stopped");
    }

    private bool ShouldCollect(Guid sourceId, int intervalMinutes, DateTime now)
    {
        if (!_lastCollectionTimes.TryGetValue(sourceId, out var lastTime))
        {
            // First time collecting this source
            return true;
        }

        return (now - lastTime).TotalMinutes >= intervalMinutes;
    }

    private bool ShouldCollectHomeWizard(DateTime now, int intervalMinutes)
    {
        if (!_lastHomeWizardCollection.TryGetValue("HomeWizard", out var lastTime))
        {
            // First time collecting
            return true;
        }

        return (now - lastTime).TotalMinutes >= intervalMinutes;
    }

    private static async Task UpdateYesterdayLearningAsync(
        IServiceProvider services,
        ILearningService learningService)
    {
        var db = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var ctx = await db.CreateDbContextAsync();
        var yesterday = DateTime.UtcNow.AddDays(-1).Date;

        var actualWh = await ctx.ProductionHistory
            .Where(p => p.Timestamp.Date == yesterday)
            .SumAsync(p => (double?)p.ValueWatts / 12.0) // 5-min samples: avg W * (1h/12) = Wh
            ?? 0;

        if (actualWh > 0)
            await learningService.UpdateLearningDataAsync(yesterday, actualWh);
    }
}

/// <summary>
/// Generates solar forecasts at configurable intervals (default: every hour).
/// </summary>
public class ForecastWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ForecastWorker> _logger;

    public ForecastWorker(IServiceProvider services, ILogger<ForecastWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ForecastWorker started");

        // Generate initial forecast on startup
        await RunForecastAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _services.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var config = configService.GetConfiguration();
            var interval = TimeSpan.FromMinutes(config.General.ForecastIntervalMinutes);

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) { break; }

            await RunForecastAsync(stoppingToken);
        }

        _logger.LogInformation("ForecastWorker stopped");
    }

    private async Task RunForecastAsync(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested) return;

        using var scope = _services.CreateScope();
        var forecastService = scope.ServiceProvider.GetRequiredService<IForecastService>();
        var schedulerState = scope.ServiceProvider.GetRequiredService<ISchedulerState>();
        var learningService = scope.ServiceProvider.GetRequiredService<ILearningService>();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
        var config = configService.GetConfiguration();
        var interval = TimeSpan.FromMinutes(config.General.ForecastIntervalMinutes);

        try
        {
            schedulerState.UpdateForecast(true, null, null);
            await forecastService.GenerateForecastAsync();
            await learningService.UpdateShadingProfileAsync();
            var now = DateTime.UtcNow;
            schedulerState.UpdateForecast(false, now, now.Add(interval));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error in ForecastWorker");
            schedulerState.UpdateForecast(false, null, null);
        }
    }
}

/// <summary>
/// Pushes forecast values to Loxone Virtual HTTP Inputs.
/// </summary>
public class LoxonePushWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<LoxonePushWorker> _logger;

    public LoxonePushWorker(IServiceProvider services, ILogger<LoxonePushWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LoxonePushWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _services.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var config = configService.GetConfiguration();

            if (!config.Loxone.EnablePush)
            {
                try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            var interval = TimeSpan.FromMinutes(config.General.UpdateIntervalMinutes);
            var forecastService = scope.ServiceProvider.GetRequiredService<IForecastService>();
            var loxoneService = scope.ServiceProvider.GetRequiredService<ILoxoneService>();
            var schedulerState = scope.ServiceProvider.GetRequiredService<ISchedulerState>();

            try
            {
                schedulerState.UpdateLoxonePush(true, null);

                var forecast = await forecastService.GetForecastAsync();
                var remaining = await forecastService.GetRemainingTodayKwhAsync();
                var confidence = await forecastService.GetConfidenceAsync(DateTime.UtcNow);

                var values = new Dictionary<string, string>
                {
                    ["SolarForecastToday"] = (forecast.TodayKwh * 1000).ToString("F0"),
                    ["SolarForecastTomorrow"] = (forecast.TomorrowKwh * 1000).ToString("F0"),
                    ["SolarRemainingToday"] = (remaining * 1000).ToString("F0"),
                    ["SolarConfidence"] = confidence.ToString("F0"),
                    ["SolarPeakTime"] = forecast.TodayPeakTime?.ToString("HH:mm") ?? "00:00",
                    ["SolarPeakPower"] = forecast.TodayPeakWatts.ToString("F0"),
                    ["SolarExpectedSurplus"] = (Math.Max(0, forecast.TodayKwh - 10) * 1000).ToString("F0"),
                };

                await loxoneService.PushToLoxoneAsync(values);
                schedulerState.UpdateLoxonePush(false, DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in LoxonePushWorker");
                schedulerState.UpdateLoxonePush(false, null);
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("LoxonePushWorker stopped");
    }
}
