using LoxoneSolarForecast.Data;
using LoxoneSolarForecast.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace LoxoneSolarForecast.Services;

public interface IHistoryService
{
    Task<HistoryViewModel> GetHistoryAsync(string period);
    Task<MonitoringViewModel> GetMonitoringDataAsync();
}

public class HistoryService : IHistoryService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILearningService _learningService;
    private readonly IWeatherService _weatherService;
    private readonly ILoxoneService _loxoneService;
    private readonly ISchedulerState _schedulerState;
    private readonly ILogger<HistoryService> _logger;

    public HistoryService(
        IDbContextFactory<AppDbContext> dbFactory,
        ILearningService learningService,
        IWeatherService weatherService,
        ILoxoneService loxoneService,
        ISchedulerState schedulerState,
        ILogger<HistoryService> logger)
    {
        _dbFactory = dbFactory;
        _learningService = learningService;
        _weatherService = weatherService;
        _loxoneService = loxoneService;
        _schedulerState = schedulerState;
        _logger = logger;
    }

    public async Task<HistoryViewModel> GetHistoryAsync(string period)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;

        DateTime fromDate = period switch
        {
            "today" => now.Date,
            "month" => now.AddMonths(-1).Date,
            "year" => now.AddYears(-1).Date,
            _ => now.AddDays(-7).Date, // week
        };

        var production = await db.ProductionHistory
            .Where(p => p.Timestamp >= fromDate)
            .GroupBy(p => p.Timestamp.Date)
            .Select(g => new { Date = g.Key, TotalWh = g.Average(p => p.ValueWatts) * 24.0 })
            .ToDictionaryAsync(x => x.Date, x => x.TotalWh);

        var consumption = await db.ConsumptionHistory
            .Where(c => c.Timestamp >= fromDate)
            .GroupBy(c => c.Timestamp.Date)
            .Select(g => new { Date = g.Key, TotalWh = g.Average(c => c.ValueWatts) * 24.0 })
            .ToDictionaryAsync(x => x.Date, x => x.TotalWh);

        var grid = await db.GridHistory
            .Where(g => g.Timestamp >= fromDate)
            .GroupBy(g => g.Timestamp.Date)
            .Select(g => new { Date = g.Key, TotalExportWh = g.Average(x => x.ExportWatts) * 24.0 })
            .ToDictionaryAsync(x => x.Date, x => x.TotalExportWh);

        var batteryStats = await db.BatteryHistory
            .Where(b => b.Timestamp >= fromDate)
            .GroupBy(b => b.Timestamp.Date)
            .Select(g => new { Date = g.Key, Min = g.Min(b => b.SocPercent), Max = g.Max(b => b.SocPercent) })
            .ToDictionaryAsync(x => x.Date, x => (x.Min, x.Max));

        var forecasts = await db.ForecastHistory
            .Where(f => f.ForecastedFor >= fromDate)
            .ToDictionaryAsync(f => f.ForecastedFor.Date, f => f.ForecastedWh);

        var allDates = production.Keys
            .Union(consumption.Keys)
            .Union(grid.Keys)
            .OrderBy(d => d)
            .ToList();

        var items = allDates.Select(date => new DailyHistoryItem
        {
            Date = date,
            ProductionKwh = production.GetValueOrDefault(date, 0) / 1000.0,
            ConsumptionKwh = consumption.GetValueOrDefault(date, 0) / 1000.0,
            ExportKwh = grid.GetValueOrDefault(date, 0) / 1000.0,
            ForecastKwh = forecasts.ContainsKey(date) ? forecasts[date] / 1000.0 : null,
            BatteryMinSoc = batteryStats.ContainsKey(date) ? batteryStats[date].Min : null,
            BatteryMaxSoc = batteryStats.ContainsKey(date) ? batteryStats[date].Max : null,
        }).ToList();

        var totalProd = items.Sum(i => i.ProductionKwh);
        var totalCons = items.Sum(i => i.ConsumptionKwh);
        var totalExport = items.Sum(i => i.ExportKwh);
        var bestDay = items.OrderByDescending(i => i.ProductionKwh).FirstOrDefault();

        var learningStats = await _learningService.GetStatsAsync();

        return new HistoryViewModel
        {
            Period = period,
            DailyItems = items,
            TotalProductionKwh = totalProd,
            TotalConsumptionKwh = totalCons,
            TotalExportKwh = totalExport,
            AverageDailyKwh = items.Count > 0 ? totalProd / items.Count : 0,
            BestDayKwh = bestDay?.ProductionKwh ?? 0,
            BestDay = bestDay?.Date,
            ForecastAccuracyPercent = learningStats.SampleCount > 0
                ? 100 - learningStats.AverageDailyErrorPercent : 0,
        };
    }

    public async Task<MonitoringViewModel> GetMonitoringDataAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;

        var schedulerStatus = _schedulerState.GetStatus();
        var loxoneHealth = _loxoneService.GetConnectionHealth();

        var weatherOk = false;
        try { weatherOk = await _weatherService.IsAvailableAsync(); } catch { }

        bool dbOk;
        try { await db.Database.CanConnectAsync(); dbOk = true; }
        catch { dbOk = false; }

        var learningStats = await _learningService.GetStatsAsync();

        var totalDataPoints = await db.ProductionHistory.CountAsync()
                            + await db.ConsumptionHistory.CountAsync();
        var totalForecasts = await db.ForecastHistory.CountAsync();
        var dataSince = await db.ProductionHistory
            .OrderBy(p => p.Timestamp)
            .Select(p => (DateTime?)p.Timestamp)
            .FirstOrDefaultAsync();

        var prodGroups = await db.ProductionHistory
            .Where(p => p.Timestamp >= now.AddDays(-30))
            .GroupBy(p => p.Timestamp.Date)
            .Select(g => g.Average(p => p.ValueWatts))
            .ToListAsync();
        var avgDailyProd = prodGroups.Count > 0
            ? prodGroups.Average() * 24.0 / 1000.0
            : 0;

        var consGroups = await db.ConsumptionHistory
            .Where(c => c.Timestamp >= now.AddDays(-30))
            .GroupBy(c => c.Timestamp.Date)
            .Select(g => g.Average(c => c.ValueWatts))
            .ToListAsync();
        var avgDailyCons = consGroups.Count > 0
            ? consGroups.Average() * 24.0 / 1000.0
            : 0;

        // Recent push logs
        var recentPushLogs = await db.LoxonePushLogs
            .OrderByDescending(l => l.Timestamp)
            .Take(20)
            .ToListAsync();

        var activities = recentPushLogs.Select(l => new RecentActivity
        {
            Timestamp = l.Timestamp,
            Type = "LoxonePush",
            Message = l.Success
                ? $"Pushed {l.TargetName}: {l.Value}"
                : $"Push failed to {l.TargetName}: {l.ErrorMessage}",
            IsError = !l.Success,
        }).ToList();

        return new MonitoringViewModel
        {
            SchedulerStatus = schedulerStatus,
            ConnectionHealth = new ConnectionHealth
            {
                WeatherApi = weatherOk,
                PvgisApi = false,
                Loxone = loxoneHealth.Loxone,
                Database = dbOk,
                WeatherApiLastCheck = now,
                LoxoneLastCheck = loxoneHealth.LoxoneLastCheck,
                LoxoneError = loxoneHealth.LoxoneError,
            },
            Statistics = new SystemStatistics
            {
                ForecastAccuracyPercent = learningStats.SampleCount > 0
                    ? 100 - learningStats.AverageDailyErrorPercent : 0,
                AverageDailyProductionKwh = avgDailyProd,
                AverageDailyConsumptionKwh = avgDailyCons,
                TotalDataPoints = totalDataPoints,
                TotalForecasts = totalForecasts,
                LearningCorrectionFactor = learningStats.CorrectionFactor,
                DataSince = dataSince,
            },
            RecentActivities = activities,
        };
    }
}

public interface ISchedulerState
{
    SchedulerStatus GetStatus();
    void UpdateDataCollection(bool running, DateTime? lastRun, DateTime? nextRun);
    void UpdateForecast(bool running, DateTime? lastRun, DateTime? nextRun);
    void UpdateLoxonePush(bool running, DateTime? lastRun);
    void SetInterval(int minutes);
}

public class SchedulerState : ISchedulerState
{
    private readonly object _lock = new();
    private SchedulerStatus _status = new();

    public SchedulerStatus GetStatus()
    {
        lock (_lock)
            return new SchedulerStatus
            {
                LastDataCollection = _status.LastDataCollection,
                LastForecastGeneration = _status.LastForecastGeneration,
                LastLoxonePush = _status.LastLoxonePush,
                NextDataCollection = _status.NextDataCollection,
                NextForecastGeneration = _status.NextForecastGeneration,
                DataCollectionRunning = _status.DataCollectionRunning,
                ForecastRunning = _status.ForecastRunning,
                LoxonePushRunning = _status.LoxonePushRunning,
                DataCollectionIntervalMinutes = _status.DataCollectionIntervalMinutes,
            };
    }

    public void UpdateDataCollection(bool running, DateTime? lastRun, DateTime? nextRun)
    {
        lock (_lock)
        {
            _status.DataCollectionRunning = running;
            if (lastRun.HasValue) _status.LastDataCollection = lastRun;
            if (nextRun.HasValue) _status.NextDataCollection = nextRun;
        }
    }

    public void UpdateForecast(bool running, DateTime? lastRun, DateTime? nextRun)
    {
        lock (_lock)
        {
            _status.ForecastRunning = running;
            if (lastRun.HasValue) _status.LastForecastGeneration = lastRun;
            if (nextRun.HasValue) _status.NextForecastGeneration = nextRun;
        }
    }

    public void UpdateLoxonePush(bool running, DateTime? lastRun)
    {
        lock (_lock)
        {
            _status.LoxonePushRunning = running;
            if (lastRun.HasValue) _status.LastLoxonePush = lastRun;
        }
    }

    public void SetInterval(int minutes)
    {
        lock (_lock)
            _status.DataCollectionIntervalMinutes = minutes;
    }
}
