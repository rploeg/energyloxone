using LoxoneSolarForecast.Data;
using LoxoneSolarForecast.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace LoxoneSolarForecast.Services;

public interface IRecommendationService
{
    Task<RecommendationViewModel> GetRecommendationsAsync();
}

public class RecommendationService : IRecommendationService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IForecastService _forecastService;
    private readonly ILogger<RecommendationService> _logger;

    public RecommendationService(
        IDbContextFactory<AppDbContext> dbFactory,
        IForecastService forecastService,
        ILogger<RecommendationService> logger)
    {
        _dbFactory = dbFactory;
        _forecastService = forecastService;
        _logger = logger;
    }

    public async Task<RecommendationViewModel> GetRecommendationsAsync()
    {
        var forecast = await _forecastService.GetForecastAsync();
        using var db = await _dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;

        // Get average daily consumption from history (client-side math — SQLite cannot translate the full chain)
        var consGroups = await db.ConsumptionHistory
            .Where(c => c.Timestamp >= now.AddDays(-30))
            .GroupBy(c => c.Timestamp.Date)
            .Select(g => g.Average(c => c.ValueWatts))
            .ToListAsync();
        var avgConsumption = consGroups.Count > 0
            ? consGroups.Average() * 24.0 / 1000.0
            : 10.0;

        var todayForecastKwh = forecast.TodayKwh;
        var expectedSurplus = Math.Max(0, todayForecastKwh - avgConsumption);

        // Identify peak production window
        var peakHours = forecast.TodayHourly
            .Where(h => h.ForecastedWh > todayForecastKwh / 24.0 * 1000.0 * 1.2)
            .OrderBy(h => h.Hour)
            .ToList();

        DateTime? surplusStart = peakHours.FirstOrDefault()?.Hour;
        DateTime? surplusEnd = peakHours.LastOrDefault()?.Hour;
        double availableSolarForEv = peakHours.Sum(h => h.ForecastedWh) / 1000.0;

        // Battery full time estimation
        var latestBattery = await db.BatteryHistory
            .OrderByDescending(b => b.Timestamp)
            .FirstOrDefaultAsync();

        DateTime? batteryFullTime = null;
        if (latestBattery != null && latestBattery.ChargePowerWatts > 100)
        {
            var remainingPercent = 100 - latestBattery.SocPercent;
            var hoursToFull = remainingPercent / (latestBattery.ChargePowerWatts / 1000.0) * 0.5; // rough estimate
            batteryFullTime = now.AddHours(hoursToFull);
        }

        var recommendations = new List<Recommendation>();

        // EV Charging recommendation
        if (availableSolarForEv > 5 && surplusStart.HasValue)
        {
            recommendations.Add(new Recommendation
            {
                Category = "EV",
                Title = "Solar EV Charging Window",
                Description = $"Expected solar surplus of {availableSolarForEv:F1} kWh. " +
                              $"Best charging window: {surplusStart:HH:mm} – {surplusEnd:HH:mm}.",
                StartTime = surplusStart,
                EndTime = surplusEnd,
                SolarEnergyKwh = availableSolarForEv,
                Priority = availableSolarForEv > 10 ? "High" : "Normal",
                Icon = "bi-ev-station",
            });
        }

        // Battery recommendation
        if (batteryFullTime.HasValue)
        {
            recommendations.Add(new Recommendation
            {
                Category = "Battery",
                Title = "Battery Charge Estimate",
                Description = $"Battery expected to reach 100% around {batteryFullTime:HH:mm} " +
                              $"based on current charge rate of {latestBattery?.ChargePowerWatts:F0} W.",
                StartTime = batteryFullTime,
                SolarEnergyKwh = null,
                Priority = "Normal",
                Icon = "bi-battery-charging",
            });
        }

        // Heat pump / water heater
        if (todayForecastKwh > 5 && surplusStart.HasValue)
        {
            recommendations.Add(new Recommendation
            {
                Category = "HeatPump",
                Title = "Buffer Heating Opportunity",
                Description = $"Consider pre-heating buffer tank between {surplusStart:HH:mm} and {surplusEnd:HH:mm} " +
                              $"using excess solar energy.",
                StartTime = surplusStart,
                EndTime = surplusEnd,
                Priority = "Normal",
                Icon = "bi-thermometer-sun",
            });
        }

        // Appliances
        if (surplusStart.HasValue && expectedSurplus > 2)
        {
            recommendations.Add(new Recommendation
            {
                Category = "Appliance",
                Title = "Optimal Appliance Start",
                Description = $"Schedule washing machine, dryer or dishwasher to start around {surplusStart:HH:mm} " +
                              $"for maximum solar self-consumption.",
                StartTime = surplusStart,
                Priority = "Low",
                Icon = "bi-house-door",
            });
        }

        if (!recommendations.Any())
        {
            recommendations.Add(new Recommendation
            {
                Category = "Info",
                Title = "Insufficient Solar Today",
                Description = $"Today's forecast is {todayForecastKwh:F1} kWh. " +
                              "No surplus energy available for optimization.",
                Priority = "Low",
                Icon = "bi-cloud",
            });
        }

        return new RecommendationViewModel
        {
            Recommendations = recommendations,
            ExpectedSurplusKwh = expectedSurplus,
            SurplusStartTime = surplusStart.HasValue ? surplusStart.Value.TimeOfDay : null,
            SurplusEndTime = surplusEnd.HasValue ? surplusEnd.Value.TimeOfDay : null,
            BatteryFullTime = batteryFullTime,
            BestEvChargeStart = surplusStart,
            BestEvChargeEnd = surplusEnd,
            AvailableSolarForEvKwh = availableSolarForEv,
        };
    }
}
