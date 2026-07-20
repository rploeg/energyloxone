using LoxoneSolarForecast.Data;
using LoxoneSolarForecast.Models.Configuration;
using LoxoneSolarForecast.Models.Entities;
using LoxoneSolarForecast.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace LoxoneSolarForecast.Services;

public interface IForecastService
{
    Task<ForecastViewModel> GetForecastAsync();
    Task GenerateForecastAsync();
    Task<double> GetRemainingTodayKwhAsync();
    Task<double> GetConfidenceAsync(DateTime date);
}

public class ForecastService : IForecastService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IWeatherService _weatherService;
    private readonly IConfigurationService _configService;
    private readonly ILearningService _learningService;
    private readonly IInfluxDBService _influxDBService;
    private readonly ILogger<ForecastService> _logger;

    public ForecastService(
        IDbContextFactory<AppDbContext> dbFactory,
        IWeatherService weatherService,
        IConfigurationService configService,
        ILearningService learningService,
        IInfluxDBService influxDBService,
        ILogger<ForecastService> logger)
    {
        _dbFactory = dbFactory;
        _weatherService = weatherService;
        _configService = configService;
        _learningService = learningService;
        _influxDBService = influxDBService;
        _logger = logger;
    }

    public async Task GenerateForecastAsync()
    {
        _logger.LogInformation("Starting forecast generation");
        var config = _configService.GetConfiguration();
        var location = config.Location;

        if (location.Latitude == 0 && location.Longitude == 0)
        {
            _logger.LogWarning("Location not configured, skipping forecast");
            return;
        }

        var weatherResult = await _weatherService.GetForecastAsync(
            location.Latitude, location.Longitude, config.General.ForecastHorizonDays);

        if (weatherResult == null)
        {
            _logger.LogError("Failed to get weather data for forecast");
            return;
        }

        var correctionFactor = await _learningService.GetCorrectionFactorAsync();
        var now = DateTime.UtcNow;

        using var db = await _dbFactory.CreateDbContextAsync();

        // Remove stale forecasts
        var cutoff = now.AddDays(-1);
        var old = await db.HourlyForecasts.Where(f => f.GeneratedAt < cutoff).ToListAsync();
        db.HourlyForecasts.RemoveRange(old);

        var hourlyForecasts = new List<HourlyForecast>();
        foreach (var weather in weatherResult.HourlyData)
        {
            var forecastWh = CalculateHourlyProduction(weather, config, correctionFactor);
            var peakW = Math.Max(0, forecastWh * 1.2);
            var confidence = CalculateHourlyConfidence(weather, now);
            hourlyForecasts.Add(new HourlyForecast
            {
                Hour = weather.Time,
                GeneratedAt = now,
                ForecastedWh = Math.Max(0, forecastWh),
                ForecastedPeakW = peakW,
                ConfidencePercent = confidence,
                CloudCoverPercent = weather.CloudCoverPercent,
                DirectRadiationWm2 = weather.DirectRadiationWm2,
                DiffuseRadiationWm2 = weather.DiffuseRadiationWm2,
                TemperatureCelsius = weather.TemperatureCelsius,
                WindSpeedMs = weather.WindSpeedMs,
            });
            
            // Write hourly forecast to InfluxDB
            await _influxDBService.WriteForecastAsync(Math.Max(0, forecastWh), confidence, weather.Time, peakW);
        }

        await db.HourlyForecasts.AddRangeAsync(hourlyForecasts);

        // Aggregate daily forecast history
        var dailyGroups = hourlyForecasts
            .GroupBy(h => h.Hour.Date)
            .ToList();

        foreach (var day in dailyGroups)
        {
            var totalWh = day.Sum(h => h.ForecastedWh);
            var peakW = day.Max(h => h.ForecastedPeakW);
            var avgConf = day.Average(h => h.ConfidencePercent);

            var existing = await db.ForecastHistory
                .FirstOrDefaultAsync(f => f.ForecastedFor.Date == day.Key.Date);

            if (existing != null)
            {
                existing.ForecastedWh = totalWh;
                existing.ConfidencePercent = avgConf;
                existing.GeneratedAt = now;
            }
            else
            {
                await db.ForecastHistory.AddAsync(new ForecastHistory
                {
                    ForecastedFor = day.Key,
                    GeneratedAt = now,
                    ForecastedWh = totalWh,
                    ConfidencePercent = avgConf,
                    Source = "OpenMeteo",
                });
            }
            
            // Write daily forecast to InfluxDB
            await _influxDBService.WriteDailyForecastAsync(totalWh, avgConf, day.Key);
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("Forecast generation complete. {Count} hourly points generated", hourlyForecasts.Count);
    }

    private double CalculateHourlyProduction(
        HourlyWeatherPoint weather,
        AppConfiguration config,
        double correctionFactor)
    {
        double totalWh = 0;

        foreach (var array in config.Solar.Arrays.Where(a => a.IsActive))
        {
            // Solar position-based irradiance calculation
            var radiation = weather.DirectRadiationWm2 + weather.DiffuseRadiationWm2;

            // Apply orientation correction (simplified)
            var azimuthRad = array.AzimuthDegrees * Math.PI / 180.0;
            var tiltRad = array.TiltDegrees * Math.PI / 180.0;
            var orientationFactor = Math.Cos(tiltRad) + Math.Sin(tiltRad) * Math.Cos(azimuthRad) * 0.5;
            orientationFactor = Math.Clamp(orientationFactor, 0.5, 1.5);

            // Temperature derating (panels lose ~0.4%/°C above 25°C)
            var tempDerating = 1 - Math.Max(0, (weather.TemperatureCelsius - 25) * 0.004);

            // System losses
            var efficiencyFactor = (1 - array.SystemLossesPercent / 100.0) * array.ShadingFactor;

            // kWp to Wh/h at standard irradiance (1000 W/m²)
            var arrayProductionWh = (array.InstalledPowerWp / 1000.0)
                                    * radiation
                                    * orientationFactor
                                    * efficiencyFactor
                                    * tempDerating
                                    * correctionFactor;

            totalWh += arrayProductionWh;
        }

        return totalWh;
    }

    private static double CalculateHourlyConfidence(HourlyWeatherPoint weather, DateTime now)
    {
        var hoursAhead = (weather.Time - now).TotalHours;
        var baseConfidence = 95.0;

        // Decrease confidence with time
        baseConfidence -= hoursAhead * 0.5;

        // Decrease with cloud cover
        baseConfidence -= weather.CloudCoverPercent * 0.15;

        // Decrease with precipitation
        baseConfidence -= weather.PrecipitationMm * 2;

        return Math.Clamp(baseConfidence, 30, 99);
    }

    public async Task<ForecastViewModel> GetForecastAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var todayUtc = now.Date;

        var latestGeneration = await db.HourlyForecasts
            .OrderByDescending(h => h.GeneratedAt)
            .Select(h => h.GeneratedAt)
            .FirstOrDefaultAsync();

        var totalForecasts = await db.HourlyForecasts.CountAsync();
        _logger.LogInformation("GetForecastAsync: latestGeneration={LatestGen}, now={Now}, todayUtc={TodayUtc}, totalForecastsInDb={Total}", 
            latestGeneration, now, todayUtc, totalForecasts);

        var hourly = await db.HourlyForecasts
            .Where(h => h.GeneratedAt == latestGeneration && h.Hour >= todayUtc)
            .OrderBy(h => h.Hour)
            .Take(7 * 24)
            .ToListAsync();

        _logger.LogInformation("GetForecastAsync: Found {Count} hourly forecasts for today onwards (filter: latestGen=={LatestGen}, Hour>={TodayUtc})", hourly.Count, latestGeneration, todayUtc);

        var todayHourly = hourly.Where(h => h.Hour.Date == todayUtc).ToList();
        var tomorrowHourly = hourly.Where(h => h.Hour.Date == todayUtc.AddDays(1)).ToList();
        
        var todaySum = todayHourly.Sum(h => h.ForecastedWh);
        var tomorrowSum = tomorrowHourly.Sum(h => h.ForecastedWh);
        _logger.LogInformation("GetForecastAsync: todayHourly={TodayCount} items, sum={TodayWh}Wh; tomorrowHourly={TomorrowCount} items, sum={TomorrowWh}Wh", 
            todayHourly.Count, todaySum, tomorrowHourly.Count, tomorrowSum);

        if (todayHourly.Any())
        {
            var sample = todayHourly.First();
            _logger.LogInformation("GetForecastAsync: Sample todayHourly[0]: Hour={Hour}, ForecastedWh={Wh}, Peak={Peak}W", 
                sample.Hour, sample.ForecastedWh, sample.ForecastedPeakW);
        }

        var dailyGroups = hourly
            .GroupBy(h => h.Hour.Date)
            .OrderBy(g => g.Key)
            .Take(7)
            .Select(g => new DailyForecastItem
            {
                Date = g.Key,
                ForecastedKwh = g.Sum(h => h.ForecastedWh) / 1000.0,
                PeakWatts = g.Max(h => h.ForecastedPeakW),
                PeakTime = g.OrderByDescending(h => h.ForecastedPeakW).FirstOrDefault()?.Hour,
                ConfidencePercent = g.Average(h => h.ConfidencePercent),
                CloudCoverPercent = g.Average(h => h.CloudCoverPercent),
                IsToday = g.Key == todayUtc,
                IsTomorrow = g.Key == todayUtc.AddDays(1),
            })
            .ToList();

        var todayKwh = todayHourly.Sum(h => h.ForecastedWh) / 1000.0;
        var tomorrowKwh = tomorrowHourly.Sum(h => h.ForecastedWh) / 1000.0;
        var todayPeak = todayHourly.OrderByDescending(h => h.ForecastedPeakW).FirstOrDefault();

        return new ForecastViewModel
        {
            DailyForecasts = dailyGroups,
            TodayHourly = todayHourly.Select(h => new HourlyForecastItem
            {
                Hour = h.Hour,
                ForecastedWh = h.ForecastedWh,
                ForecastedPeakW = h.ForecastedPeakW,
                CloudCoverPercent = h.CloudCoverPercent,
                DirectRadiation = h.DirectRadiationWm2,
                ConfidencePercent = h.ConfidencePercent,
            }).ToList(),
            TomorrowHourly = tomorrowHourly.Select(h => new HourlyForecastItem
            {
                Hour = h.Hour,
                ForecastedWh = h.ForecastedWh,
                ForecastedPeakW = h.ForecastedPeakW,
                CloudCoverPercent = h.CloudCoverPercent,
                DirectRadiation = h.DirectRadiationWm2,
                ConfidencePercent = h.ConfidencePercent,
            }).ToList(),
            TodayKwh = todayKwh,
            TomorrowKwh = tomorrowKwh,
            TodayConfidence = dailyGroups.FirstOrDefault(d => d.IsToday)?.ConfidencePercent ?? 0,
            TodayPeakTime = todayPeak?.Hour,
            TodayPeakWatts = todayPeak?.ForecastedPeakW ?? 0,
            LastGenerated = latestGeneration,
        };
    }

    public async Task<double> GetRemainingTodayKwhAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;

        var latestGeneration = await db.HourlyForecasts
            .OrderByDescending(h => h.GeneratedAt)
            .Select(h => h.GeneratedAt)
            .FirstOrDefaultAsync();

        return await db.HourlyForecasts
            .Where(h => h.GeneratedAt == latestGeneration && h.Hour >= now && h.Hour.Date == now.Date)
            .SumAsync(h => (double?)h.ForecastedWh / 1000.0) ?? 0;
    }

    public async Task<double> GetConfidenceAsync(DateTime date)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var latestGeneration = await db.HourlyForecasts
            .OrderByDescending(h => h.GeneratedAt)
            .Select(h => h.GeneratedAt)
            .FirstOrDefaultAsync();

        var avg = await db.HourlyForecasts
            .Where(h => h.GeneratedAt == latestGeneration && h.Hour.Date == date.Date)
            .AverageAsync(h => (double?)h.ConfidencePercent);

        return avg ?? 0;
    }
}
