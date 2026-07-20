using LoxoneSolarForecast.Models.Configuration;

namespace LoxoneSolarForecast.Services;

/// <summary>
/// HomeWizard Watermeter data collector
/// Collects real-time water usage from HomeWizard API endpoint
/// API: http://IP/api/v1/data
/// Response: {"water": {"current_liter_per_minute": 0.0, "total_liter_m3": 123.456}}
/// </summary>
public interface IHomeWizardCollector : IDataCollector
{
}

public class HomeWizardCollector : IHomeWizardCollector
{
    public string SourceName => "HomeWizard";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IInfluxDBService _influxDBService;
    private readonly IConfigurationService _configService;
    private readonly ILogger<HomeWizardCollector> _logger;

    private bool _lastConnectionSuccess;
    private DateTime? _lastConnectionCheck;
    private DateTime? _lastSuccessfulCollection;
    private string? _lastConnectionError;

    public HomeWizardCollector(
        IHttpClientFactory httpClientFactory,
        IInfluxDBService influxDBService,
        IConfigurationService configService,
        ILogger<HomeWizardCollector> logger)
    {
        _httpClientFactory = httpClientFactory;
        _influxDBService = influxDBService;
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// Collect water usage data from HomeWizard
    /// </summary>
    public async Task CollectAsync()
    {
        try
        {
            var config = _configService.GetConfiguration();
            if (config?.HomeWizard == null || !config.HomeWizard.Enabled)
            {
                _logger.LogDebug("HomeWizard collector is disabled");
                return;
            }

            var data = await FetchWaterDataAsync(config.HomeWizard);
            if (data != null)
            {
                // Write current flow (liters/min) - from active_liter_lpm
                await _influxDBService.WriteWaterFlowAsync(
                    data["active_liter_lpm"],
                    "HomeWizard");

                // Write total consumption (m³)
                await _influxDBService.WriteWaterConsumptionAsync(
                    data["total_liter_m3"],
                    "HomeWizard");

                _lastConnectionSuccess = true;
                _lastSuccessfulCollection = DateTime.UtcNow;
                _lastConnectionError = null;

                _logger.LogDebug(
                    "HomeWizard water data collected: {FlowRate} L/min, {Total} m³",
                    data["active_liter_lpm"],
                    data["total_liter_m3"]);
            }
        }
        catch (Exception ex)
        {
            _lastConnectionSuccess = false;
            _lastConnectionError = ex.Message;
            _logger.LogError(ex, "HomeWizard collection failed: {Error}", ex.Message);
        }
        finally
        {
            _lastConnectionCheck = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Test connection to HomeWizard API
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var config = _configService.GetConfiguration();
            if (config?.HomeWizard == null)
                return false;

            var result = await FetchWaterDataAsync(config.HomeWizard);
            _lastConnectionSuccess = result != null;
            _lastConnectionCheck = DateTime.UtcNow;
            return _lastConnectionSuccess;
        }
        catch (Exception ex)
        {
            _lastConnectionError = ex.Message;
            _lastConnectionSuccess = false;
            _lastConnectionCheck = DateTime.UtcNow;
            _logger.LogError(ex, "HomeWizard test connection failed");
            return false;
        }
    }

    /// <summary>
    /// Get connection health status
    /// </summary>
    public DataCollectorHealth GetHealth()
    {
        return new DataCollectorHealth
        {
            SourceName = SourceName,
            IsConnected = _lastConnectionSuccess,
            LastCheck = _lastConnectionCheck,
            LastSuccessfulCollection = _lastSuccessfulCollection,
            ErrorMessage = _lastConnectionError,
            Status = _lastConnectionSuccess ? "OK" : "ERROR"
        };
    }

    /// <summary>
    /// Fetch water data from HomeWizard API
    /// </summary>
    private async Task<Dictionary<string, double>?> FetchWaterDataAsync(HomeWizardSettings settings)
    {
        if (string.IsNullOrEmpty(settings.IpAddress))
        {
            _logger.LogWarning("HomeWizard IP address not configured");
            return null;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var url = $"http://{settings.IpAddress}/api/v1/data";
            var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("HomeWizard API returned {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // HomeWizard API returns fields at root level: active_liter_lpm and total_liter_m3
            if (!root.TryGetProperty("active_liter_lpm", out var flowElement) ||
                !root.TryGetProperty("total_liter_m3", out var totalElement))
            {
                _logger.LogWarning("HomeWizard response missing expected properties");
                return null;
            }

            var flowPerMinute = flowElement.GetDouble();
            var totalM3 = totalElement.GetDouble();

            return new Dictionary<string, double>
            {
                { "active_liter_lpm", flowPerMinute },
                { "total_liter_m3", totalM3 }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch HomeWizard data from {Url}", settings.IpAddress);
            throw;
        }
    }
}
