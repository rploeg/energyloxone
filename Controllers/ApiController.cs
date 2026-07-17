using LoxoneSolarForecast.Data;
using LoxoneSolarForecast.Models.ViewModels;
using LoxoneSolarForecast.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LoxoneSolarForecast.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
public class ApiController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly IForecastService _forecastService;
    private readonly IHistoryService _historyService;
    private readonly IRecommendationService _recommendationService;
    private readonly ILoxoneService _loxoneService;
    private readonly IConfigurationService _configService;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ApiController(
        IDashboardService dashboardService,
        IForecastService forecastService,
        IHistoryService historyService,
        IRecommendationService recommendationService,
        ILoxoneService loxoneService,
        IConfigurationService configService,
        IDbContextFactory<AppDbContext> dbFactory)
    {
        _dashboardService = dashboardService;
        _forecastService = forecastService;
        _historyService = historyService;
        _recommendationService = recommendationService;
        _loxoneService = loxoneService;
        _configService = configService;
        _dbFactory = dbFactory;
    }

    /// <summary>GET /api/status — Overall system status</summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var dashboard = await _dashboardService.GetDashboardDataAsync();
        return Ok(new
        {
            timestamp = DateTime.UtcNow,
            production = new
            {
                currentWatts = dashboard.CurrentProductionWatts,
                todayForecastKwh = dashboard.TodayForecastKwh,
                remainingTodayKwh = dashboard.RemainingTodayKwh,
            },
            consumption = new
            {
                currentWatts = dashboard.CurrentConsumptionWatts,
            },
            battery = new
            {
                socPercent = dashboard.BatterySocPercent,
            },
            grid = new
            {
                exportWatts = dashboard.GridExportWatts,
            },
            connections = new
            {
                loxone = dashboard.LoxoneConnected,
                weatherApi = dashboard.WeatherApiConnected,
                database = dashboard.DatabaseConnected,
            },
        });
    }

    /// <summary>GET /api/forecast — Full 7-day forecast</summary>
    [HttpGet("forecast")]
    public async Task<IActionResult> GetForecast()
    {
        var forecast = await _forecastService.GetForecastAsync();
        return Ok(new
        {
            generatedAt = forecast.LastGenerated,
            today = new
            {
                kWh = forecast.TodayKwh,
                confidence = forecast.TodayConfidence,
                peakTime = forecast.TodayPeakTime,
                peakWatts = forecast.TodayPeakWatts,
            },
            tomorrow = new
            {
                kWh = forecast.TomorrowKwh,
            },
            daily = forecast.DailyForecasts.Select(d => new
            {
                date = d.Date.ToString("yyyy-MM-dd"),
                kWh = d.ForecastedKwh,
                peakWatts = d.PeakWatts,
                peakTime = d.PeakTime,
                confidence = d.ConfidencePercent,
                cloudCover = d.CloudCoverPercent,
            }),
            hourlyToday = forecast.TodayHourly.Select(h => new
            {
                hour = h.Hour,
                wh = h.ForecastedWh,
                peakW = h.ForecastedPeakW,
                confidence = h.ConfidencePercent,
            }),
        });
    }

    /// <summary>GET /api/forecast/today — Today's forecast</summary>
    [HttpGet("forecast/today")]
    public async Task<IActionResult> GetForecastToday()
    {
        var forecast = await _forecastService.GetForecastAsync();
        return Ok(new
        {
            date = DateTime.UtcNow.Date.ToString("yyyy-MM-dd"),
            kWh = forecast.TodayKwh,
            remainingKwh = await _forecastService.GetRemainingTodayKwhAsync(),
            confidence = forecast.TodayConfidence,
            peakTime = forecast.TodayPeakTime,
            peakWatts = forecast.TodayPeakWatts,
            hourly = forecast.TodayHourly.Select(h => new
            {
                hour = h.Hour.ToString("HH:mm"),
                wh = h.ForecastedWh,
            }),
        });
    }

    /// <summary>GET /api/forecast/tomorrow — Tomorrow's forecast</summary>
    [HttpGet("forecast/tomorrow")]
    public async Task<IActionResult> GetForecastTomorrow()
    {
        var forecast = await _forecastService.GetForecastAsync();
        return Ok(new
        {
            date = DateTime.UtcNow.AddDays(1).Date.ToString("yyyy-MM-dd"),
            kWh = forecast.TomorrowKwh,
            hourly = forecast.TomorrowHourly.Select(h => new
            {
                hour = h.Hour.ToString("HH:mm"),
                wh = h.ForecastedWh,
            }),
        });
    }

    /// <summary>GET /api/history — Historical production data</summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] string period = "week")
    {
        var history = await _historyService.GetHistoryAsync(period);
        return Ok(new
        {
            period = history.Period,
            summary = new
            {
                totalProductionKwh = history.TotalProductionKwh,
                totalConsumptionKwh = history.TotalConsumptionKwh,
                totalExportKwh = history.TotalExportKwh,
                averageDailyKwh = history.AverageDailyKwh,
                forecastAccuracyPercent = history.ForecastAccuracyPercent,
            },
            daily = history.DailyItems.Select(d => new
            {
                date = d.Date.ToString("yyyy-MM-dd"),
                productionKwh = d.ProductionKwh,
                consumptionKwh = d.ConsumptionKwh,
                exportKwh = d.ExportKwh,
                forecastKwh = d.ForecastKwh,
            }),
        });
    }

    /// <summary>GET /api/recommendations — Energy optimization recommendations</summary>
    [HttpGet("recommendations")]
    public async Task<IActionResult> GetRecommendations()
    {
        var recs = await _recommendationService.GetRecommendationsAsync();
        return Ok(new
        {
            generatedAt = DateTime.UtcNow,
            expectedSurplusKwh = recs.ExpectedSurplusKwh,
            surplusWindow = new
            {
                start = recs.SurplusStartTime?.ToString(@"hh\:mm"),
                end = recs.SurplusEndTime?.ToString(@"hh\:mm"),
            },
            batteryFullTime = recs.BatteryFullTime,
            recommendations = recs.Recommendations.Select(r => new
            {
                category = r.Category,
                title = r.Title,
                description = r.Description,
                start = r.StartTime,
                end = r.EndTime,
                solarEnergyKwh = r.SolarEnergyKwh,
                priority = r.Priority,
            }),
        });
    }

    /// <summary>POST /api/forecast/refresh — Trigger immediate forecast regeneration</summary>
    [HttpPost("forecast/refresh")]
    public async Task<IActionResult> RefreshForecast()
    {
        await _forecastService.GenerateForecastAsync();
        var forecast = await _forecastService.GetForecastAsync();
        return Ok(new
        {
            message = "Forecast refreshed",
            generatedAt = forecast.LastGenerated,
            todayKwh = forecast.TodayKwh,
            tomorrowKwh = forecast.TomorrowKwh,
            confidence = forecast.TodayConfidence,
        });
    }

    /// <summary>GET /api/health — Application health check</summary>
    [HttpGet("health")]
    public async Task<IActionResult> GetHealth()
    {
        bool dbOk;
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            await db.Database.CanConnectAsync();
            dbOk = true;
        }
        catch { dbOk = false; }

        var status = dbOk ? "healthy" : "degraded";
        return dbOk ? Ok(new { status, timestamp = DateTime.UtcNow })
                    : StatusCode(503, new { status, timestamp = DateTime.UtcNow });
    }

    /// <summary>GET /api/loxone/test — Test Loxone connectivity and probe available endpoints</summary>
    [HttpGet("loxone/test")]
    public async Task<IActionResult> TestLoxoneEndpoints([FromQuery] string? testUrl = null)
    {
        var config = _configService.GetConfiguration();

        if (string.IsNullOrWhiteSpace(config.Loxone.IpAddress))
            return BadRequest(new { error = "Loxone IP not configured" });

        var results = new List<object>();
        var baseUrl = $"http://{config.Loxone.IpAddress}:{config.Loxone.Port}";

        // Helper function to create HttpClient with authentication
        System.Net.Http.HttpClient CreateAuthenticatedClient()
        {
            var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            // Add HTTP Basic Authentication if credentials are provided
            if (!string.IsNullOrWhiteSpace(config.Loxone.Username) && !string.IsNullOrWhiteSpace(config.Loxone.Password))
            {
                var credentials = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(
                    $"{config.Loxone.Username}:{config.Loxone.Password}"));
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            }

            return client;
        }

        // Test configured data sources
        foreach (var source in config.LoxoneDataSources)
        {
            var fullUrl = source.Url.Replace("{loxone}", baseUrl);
            try
            {
                using var client = CreateAuthenticatedClient();
                var response = await client.GetAsync(fullUrl);
                results.Add(new
                {
                    url = fullUrl,
                    name = source.Name,
                    status = (int)response.StatusCode,
                    statusText = response.StatusCode.ToString(),
                    success = response.IsSuccessStatusCode,
                });
            }
            catch (Exception ex)
            {
                results.Add(new
                {
                    url = fullUrl,
                    name = source.Name,
                    error = ex.Message,
                    success = false,
                });
            }
        }

        // If a custom test URL provided, test that too
        if (!string.IsNullOrWhiteSpace(testUrl))
        {
            var fullTestUrl = testUrl.Replace("{loxone}", baseUrl);
            try
            {
                using var client = CreateAuthenticatedClient();
                var response = await client.GetAsync(fullTestUrl);
                results.Add(new
                {
                    url = fullTestUrl,
                    name = "Custom Test",
                    status = (int)response.StatusCode,
                    statusText = response.StatusCode.ToString(),
                    success = response.IsSuccessStatusCode,
                });
            }
            catch (Exception ex)
            {
                results.Add(new
                {
                    url = fullTestUrl,
                    name = "Custom Test",
                    error = ex.Message,
                    success = false,
                });
            }
        }

        return Ok(new
        {
            loxoneIp = config.Loxone.IpAddress,
            loxonePort = config.Loxone.Port,
            baseUrl = baseUrl,
            testTime = DateTime.UtcNow,
            results = results,
            hint = "Common Loxone paths: /dev/sps/io/{datapoint}, /statistics, /jdev/cfg/app",
        });
    }
}

