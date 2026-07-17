using LoxoneSolarForecast.Data;
using LoxoneSolarForecast.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace LoxoneSolarForecast.Services;

public interface IDashboardService
{
    Task<DashboardViewModel> GetDashboardDataAsync();
}

public class DashboardService : IDashboardService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IForecastService _forecastService;
    private readonly IConfigurationService _configService;
    private readonly IWeatherService _weatherService;
    private readonly ILoxoneService _loxoneService;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(
        IDbContextFactory<AppDbContext> dbFactory,
        IForecastService forecastService,
        IConfigurationService configService,
        IWeatherService weatherService,
        ILoxoneService loxoneService,
        ILogger<DashboardService> logger)
    {
        _dbFactory = dbFactory;
        _forecastService = forecastService;
        _configService = configService;
        _weatherService = weatherService;
        _loxoneService = loxoneService;
        _logger = logger;
    }

    public async Task<DashboardViewModel> GetDashboardDataAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var config = _configService.GetConfiguration();
        var now = DateTime.UtcNow;
        var todayUtc = now.Date;

        // Latest production
        var latestProduction = await db.ProductionHistory
            .Where(p => p.Timestamp > now.AddMinutes(-15))
            .OrderByDescending(p => p.Timestamp)
            .FirstOrDefaultAsync();

        // Latest consumption
        var latestConsumption = await db.ConsumptionHistory
            .Where(c => c.Timestamp > now.AddMinutes(-15))
            .OrderByDescending(c => c.Timestamp)
            .FirstOrDefaultAsync();

        // Latest battery
        var latestBattery = await db.BatteryHistory
            .Where(b => b.Timestamp > now.AddMinutes(-15))
            .OrderByDescending(b => b.Timestamp)
            .FirstOrDefaultAsync();

        // Latest grid
        var latestGrid = await db.GridHistory
            .Where(g => g.Timestamp > now.AddMinutes(-15))
            .OrderByDescending(g => g.Timestamp)
            .FirstOrDefaultAsync();

        // Forecast
        ForecastViewModel? forecast = null;
        try { forecast = await _forecastService.GetForecastAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not load forecast for dashboard"); }

        // Today actual production
        var todayActualHourly = await db.ProductionHistory
            .Where(p => p.Timestamp.Date == todayUtc)
            .GroupBy(p => p.Timestamp.Hour)
            .Select(g => new { Hour = g.Key, AvgW = g.Average(p => p.ValueWatts) })
            .ToListAsync();

        var remainingToday = await _forecastService.GetRemainingTodayKwhAsync();
        var todayConfidence = await _forecastService.GetConfidenceAsync(todayUtc);

        // Horizon 7 days
        var dailyForecast = forecast?.DailyForecasts
            .Select(d => new DailyDataPoint
            {
                Date = d.Date,
                ForecastedKwh = d.ForecastedKwh,
                Confidence = d.ConfidencePercent,
            })
            .ToList() ?? new();

        // Connection checks
        var loxoneHealth = _loxoneService.GetConnectionHealth();
        var loxoneConfigured = !string.IsNullOrWhiteSpace(config.Loxone.IpAddress);
        var weatherOk = false;
        try { weatherOk = await _weatherService.IsAvailableAsync(); } catch { }

        bool dbOk;
        try
        {
            await db.Database.CanConnectAsync();
            dbOk = true;
        }
        catch { dbOk = false; }

        return new DashboardViewModel
        {
            ApplicationName = config.General.ApplicationName,
            CurrentProductionWatts = latestProduction?.ValueWatts ?? 0,
            CurrentConsumptionWatts = latestConsumption?.ValueWatts ?? 0,
            BatterySocPercent = latestBattery?.SocPercent ?? 0,
            GridExportWatts = latestGrid?.ExportWatts ?? 0,
            TodayForecastKwh = forecast?.TodayKwh ?? 0,
            TomorrowForecastKwh = forecast?.TomorrowKwh ?? 0,
            RemainingTodayKwh = remainingToday,
            ConfidencePercent = todayConfidence,
            ExpectedSurplusKwh = Math.Max(0, (forecast?.TodayKwh ?? 0) - 10),
            PeakProductionTime = forecast?.TodayPeakTime,
            PeakProductionWatts = forecast?.TodayPeakWatts ?? 0,
            LoxoneConnected = loxoneHealth.Loxone || loxoneConfigured,
            WeatherApiConnected = weatherOk,
            PvgisApiConnected = false, // tested separately
            DatabaseConnected = dbOk,
            HourlyForecast = forecast?.TodayHourly
                .Select(h => new HourlyDataPoint { Time = h.Hour, Value = h.ForecastedWh })
                .ToList() ?? new(),
            HourlyActual = todayActualHourly
                .Select(h => new HourlyDataPoint
                {
                    Time = todayUtc.AddHours(h.Hour),
                    Value = h.AvgW,
                })
                .OrderBy(h => h.Time)
                .ToList(),
            DailyForecast = dailyForecast,
            LastUpdated = now,
        };
    }
}
