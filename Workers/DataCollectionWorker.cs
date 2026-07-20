namespace LoxoneSolarForecast.Workers;

/// <summary>
/// Unified background worker that orchestrates all data collectors
/// Replaces the previous single-source worker with a multi-source architecture
/// Manages collection intervals for each source independently
/// </summary>
public class DataCollectionWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfigurationService _configService;
    private readonly ILogger<DataCollectionWorker> _logger;
    private readonly Dictionary<string, DateTime> _lastCollectionTime = new();
    private readonly Dictionary<string, int> _collectionIntervals = new();

    public DataCollectionWorker(
        IServiceProvider serviceProvider,
        IConfigurationService configService,
        ILogger<DataCollectionWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _configService = configService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Data Collection Worker starting...");
        
        // Initial delay to allow application to fully initialize
        await Task.Delay(5000, stoppingToken);

        InitializeCollectionSchedules();

        // Main collection loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectFromAllSourcesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in data collection loop");
            }

            // Check every 10 seconds if any source needs collection
            await Task.Delay(10000, stoppingToken);
        }

        _logger.LogInformation("Data Collection Worker stopping");
    }

    private void InitializeCollectionSchedules()
    {
        var config = _configService.GetConfiguration();

        // Loxone collection interval
        if (config.Loxone?.EnablePull == true)
        {
            var loxoneInterval = config.LoxoneDataSources
                .Where(x => x.IsActive)
                .Select(x => x.UpdateIntervalMinutes)
                .FirstOrDefault(5);
            _collectionIntervals["Loxone"] = loxoneInterval;
            _lastCollectionTime["Loxone"] = DateTime.UtcNow;
            _logger.LogInformation("Loxone collection scheduled every {Interval} minutes", loxoneInterval);
        }

        // HomeWizard collection interval
        if (config.HomeWizard?.Enabled == true && !string.IsNullOrEmpty(config.HomeWizard.IpAddress))
        {
            _collectionIntervals["HomeWizard"] = config.HomeWizard.UpdateIntervalMinutes;
            _lastCollectionTime["HomeWizard"] = DateTime.UtcNow;
            _logger.LogInformation("HomeWizard collection scheduled every {Interval} minutes", config.HomeWizard.UpdateIntervalMinutes);
        }

        // Forecast collection interval
        var forecastInterval = config.General?.ForecastIntervalMinutes ?? 60;
        _collectionIntervals["Forecast"] = forecastInterval;
        _lastCollectionTime["Forecast"] = DateTime.UtcNow;
        _logger.LogInformation("Forecast collection scheduled every {Interval} minutes", forecastInterval);
    }

    private async Task CollectFromAllSourcesAsync()
    {
        var now = DateTime.UtcNow;

        // Loxone data collection
        if (ShouldCollect("Loxone", now))
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var loxoneService = scope.ServiceProvider.GetRequiredService<ILoxoneService>();
                await loxoneService.CollectDataAsync();
                _lastCollectionTime["Loxone"] = now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Loxone data collection failed");
            }
        }

        // HomeWizard water meter collection
        if (ShouldCollect("HomeWizard", now))
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var homeWizardCollector = scope.ServiceProvider.GetRequiredService<IHomeWizardCollector>();
                await homeWizardCollector.CollectAsync();
                _lastCollectionTime["HomeWizard"] = now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HomeWizard data collection failed");
            }
        }

        // Solar forecast collection
        if (ShouldCollect("Forecast", now))
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var forecastService = scope.ServiceProvider.GetRequiredService<IForecastService>();
                await forecastService.GenerateForecastsAsync();
                _lastCollectionTime["Forecast"] = now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Forecast generation failed");
            }
        }
    }

    private bool ShouldCollect(string sourceName, DateTime now)
    {
        if (!_collectionIntervals.ContainsKey(sourceName))
            return false;

        if (!_lastCollectionTime.ContainsKey(sourceName))
            return true;

        var interval = TimeSpan.FromMinutes(_collectionIntervals[sourceName]);
        var timeSinceLastCollection = now - _lastCollectionTime[sourceName];
        
        return timeSinceLastCollection >= interval;
    }
}
