using LoxoneSolarForecast.Data;
using LoxoneSolarForecast.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace LoxoneSolarForecast.Services;

public interface ILearningService
{
    Task UpdateLearningDataAsync(DateTime date, double actualWh);
    Task<double> GetCorrectionFactorAsync();
    Task<LearningStats> GetStatsAsync();
    Task UpdateShadingProfileAsync();
}

public class LearningStats
{
    public double CorrectionFactor { get; set; } = 1.0;
    public double AverageDailyErrorPercent { get; set; }
    public double WeeklyErrorPercent { get; set; }
    public double MonthlyErrorPercent { get; set; }
    public double AverageEfficiency { get; set; }
    public int SampleCount { get; set; }
    public List<ShadingProfile> ShadingProfiles { get; set; } = new();
}

public class LearningService : ILearningService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<LearningService> _logger;

    public LearningService(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<LearningService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task UpdateLearningDataAsync(DateTime date, double actualWh)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var forecast = await db.ForecastHistory
            .Where(f => f.ForecastedFor.Date == date.Date)
            .OrderByDescending(f => f.GeneratedAt)
            .FirstOrDefaultAsync();

        if (forecast == null) return;

        forecast.ActualWh = actualWh;
        if (forecast.ForecastedWh > 0)
            forecast.ErrorPercent = Math.Abs(forecast.ForecastedWh - actualWh) / forecast.ForecastedWh * 100.0;

        var existing = await db.LearningData.FirstOrDefaultAsync(l => l.Date.Date == date.Date);
        var efficiencyFactor = forecast.ForecastedWh > 0 ? actualWh / forecast.ForecastedWh : 1.0;
        var month = date.Month;
        var season = month switch
        {
            3 or 4 or 5 => "Spring",
            6 or 7 or 8 => "Summer",
            9 or 10 or 11 => "Autumn",
            _ => "Winter"
        };

        if (existing != null)
        {
            existing.ActualWh = actualWh;
            existing.ForecastedWh = forecast.ForecastedWh;
            existing.EfficiencyFactor = efficiencyFactor;
        }
        else
        {
            await db.LearningData.AddAsync(new LearningData
            {
                Date = date.Date,
                ForecastedWh = forecast.ForecastedWh,
                ActualWh = actualWh,
                EfficiencyFactor = efficiencyFactor,
                CorrectionFactor = 1.0,
                Season = season,
            });
        }

        // Recalculate correction factor
        var recentData = await db.LearningData
            .Where(l => l.Date >= DateTime.UtcNow.AddDays(-90) && l.ForecastedWh > 0)
            .OrderByDescending(l => l.Date)
            .Take(30)
            .ToListAsync();

        if (recentData.Count >= 5)
        {
            var avgEfficiency = recentData.Average(l => l.EfficiencyFactor);
            // Apply exponential smoothing
            var correctionFactor = Math.Clamp(avgEfficiency, 0.5, 1.5);

            foreach (var item in recentData)
                item.CorrectionFactor = correctionFactor;
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("Learning data updated for {Date}: actual={ActualWh}Wh, forecast={ForecastWh}Wh",
            date.Date, actualWh, forecast.ForecastedWh);
    }

    public async Task<double> GetCorrectionFactorAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var recentData = await db.LearningData
            .Where(l => l.Date >= DateTime.UtcNow.AddDays(-90) && l.ForecastedWh > 0 && l.ActualWh > 100)
            .OrderByDescending(l => l.Date)
            .Take(30)
            .ToListAsync();

        if (recentData.Count < 5) return 1.0;

        // Weighted average — more recent days have more weight
        var weightedSum = 0.0;
        var weightSum = 0.0;
        for (int i = 0; i < recentData.Count; i++)
        {
            var weight = Math.Exp(-i * 0.05); // exponential decay
            weightedSum += recentData[i].EfficiencyFactor * weight;
            weightSum += weight;
        }

        return Math.Clamp(weightedSum / weightSum, 0.5, 1.5);
    }

    public async Task<LearningStats> GetStatsAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var correctionFactor = await GetCorrectionFactorAsync();

        var recentData = await db.LearningData
            .Where(l => l.ForecastedWh > 0 && l.ActualWh > 0)
            .OrderByDescending(l => l.Date)
            .Take(30)
            .ToListAsync();

        var weekly = recentData.Take(7).ToList();
        var monthly = recentData.Take(30).ToList();

        var shadingProfiles = await db.ShadingProfiles
            .OrderBy(s => s.MonthOfYear)
            .ThenBy(s => s.HourOfDay)
            .ToListAsync();

        return new LearningStats
        {
            CorrectionFactor = correctionFactor,
            AverageDailyErrorPercent = recentData.Count > 0
                ? recentData.Average(l => Math.Abs(l.ForecastedWh - l.ActualWh) / l.ForecastedWh * 100.0) : 0,
            WeeklyErrorPercent = weekly.Count > 0
                ? weekly.Average(l => Math.Abs(l.ForecastedWh - l.ActualWh) / l.ForecastedWh * 100.0) : 0,
            MonthlyErrorPercent = monthly.Count > 0
                ? monthly.Average(l => Math.Abs(l.ForecastedWh - l.ActualWh) / l.ForecastedWh * 100.0) : 0,
            AverageEfficiency = recentData.Count > 0 ? recentData.Average(l => l.EfficiencyFactor) : 1.0,
            SampleCount = recentData.Count,
            ShadingProfiles = shadingProfiles,
        };
    }

    public async Task UpdateShadingProfileAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var cutoff = DateTime.UtcNow.AddDays(-90);

        // Compare actual production to expected (from forecast) by hour and month
        var hourlyActual = await db.ProductionHistory
            .Where(p => p.Timestamp >= cutoff)
            .GroupBy(p => new { p.Timestamp.Month, p.Timestamp.Hour })
            .Select(g => new
            {
                g.Key.Month,
                g.Key.Hour,
                AvgWatts = g.Average(p => p.ValueWatts),
                Count = g.Count()
            })
            .ToListAsync();

        foreach (var group in hourlyActual.Where(g => g.Count >= 10))
        {
            // Compare to hourly forecast average
            var forecastAvg = await db.HourlyForecasts
                .Where(h => h.Hour.Month == group.Month && h.Hour.Hour == group.Hour)
                .AverageAsync(h => (double?)h.ForecastedPeakW) ?? 0;

            if (forecastAvg <= 0) continue;

            var reduction = Math.Max(0, (forecastAvg - group.AvgWatts) / forecastAvg * 100.0);
            if (reduction < 5) continue; // Not significant

            var existing = await db.ShadingProfiles
                .FirstOrDefaultAsync(s => s.HourOfDay == group.Hour && s.MonthOfYear == group.Month);

            if (existing != null)
            {
                // Exponential moving average
                existing.AverageReductionPercent = existing.AverageReductionPercent * 0.8 + reduction * 0.2;
                existing.SampleCount += group.Count;
                existing.LastUpdated = DateTime.UtcNow;
            }
            else
            {
                await db.ShadingProfiles.AddAsync(new ShadingProfile
                {
                    HourOfDay = group.Hour,
                    MonthOfYear = group.Month,
                    AverageReductionPercent = reduction,
                    SampleCount = group.Count,
                    LastUpdated = DateTime.UtcNow,
                });
            }
        }

        await db.SaveChangesAsync();
        _logger.LogDebug("Shading profile updated");
    }
}
