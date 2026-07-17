using LoxoneSolarForecast.Models.Configuration;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LoxoneSolarForecast.Services;

public interface IInfluxDBService
{
    Task WriteProductionAsync(double valueWatts, string sourceName);
    Task WriteConsumptionAsync(double valueWatts, string sourceName);
    Task WriteBatterySOCAsync(double valuePercent, string sourceName);
    Task WriteBatteryPowerAsync(double valueWatts, string sourceName);
    Task WriteGridExportAsync(double exportWatts, double importWatts, string sourceName);
    Task WriteForecastAsync(double forecastedWh, double confidencePercent, DateTime forecastHour, double peakW);
    Task WriteDailyForecastAsync(double forecastedWh, double confidencePercent, DateTime forecastDay);
    Task<bool> TestConnectionAsync();
}

public class InfluxDBService : IInfluxDBService
{
    private readonly IConfigurationService _configService;
    private readonly ILogger<InfluxDBService> _logger;
    private readonly HttpClient _httpClient;

    public InfluxDBService(
        IConfigurationService configService,
        ILogger<InfluxDBService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _configService = configService;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
    }

    private string GetWriteUrl(InfluxDBSettings settings)
    {
        var baseUrl = (settings.Url ?? "").TrimEnd('/');
        return $"{baseUrl}/api/v2/write?org={Uri.EscapeDataString(settings.Organization ?? "")}&bucket={Uri.EscapeDataString(settings.Bucket ?? "Loxone")}";
    }

    private async Task<bool> AuthenticateAsync(InfluxDBSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Token))
            return await Task.FromResult(false);

        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", settings.Token);
        
        return await Task.FromResult(true);
    }

    private async Task WriteLineProtocolAsync(string lineProtocol, InfluxDBSettings settings)
    {
        try
        {
            if (!settings.EnableInfluxDB)
                return;

            if (string.IsNullOrWhiteSpace(settings.Url) || string.IsNullOrWhiteSpace(settings.Bucket))
                return;

            await AuthenticateAsync(settings);

            var writeUrl = GetWriteUrl(settings);
            var content = new StringContent(lineProtocol, System.Text.Encoding.UTF8, "text/plain");
            var response = await _httpClient.PostAsync(writeUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning($"InfluxDB write failed: {response.StatusCode} - {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing to InfluxDB");
        }
    }

    public async Task WriteProductionAsync(double valueWatts, string sourceName)
    {
        var config = _configService.GetConfiguration();
        var timestamp = GetUnixNanoseconds();
        var lineProtocol = $"production,source={EscapeTag(sourceName)} value={valueWatts} {timestamp}";
        await WriteLineProtocolAsync(lineProtocol, config.InfluxDB);
    }

    public async Task WriteConsumptionAsync(double valueWatts, string sourceName)
    {
        var config = _configService.GetConfiguration();
        var timestamp = GetUnixNanoseconds();
        var lineProtocol = $"consumption,source={EscapeTag(sourceName)} value={valueWatts} {timestamp}";
        await WriteLineProtocolAsync(lineProtocol, config.InfluxDB);
    }

    public async Task WriteBatterySOCAsync(double valuePercent, string sourceName)
    {
        var config = _configService.GetConfiguration();
        var timestamp = GetUnixNanoseconds();
        var lineProtocol = $"battery_soc,source={EscapeTag(sourceName)} value={valuePercent} {timestamp}";
        await WriteLineProtocolAsync(lineProtocol, config.InfluxDB);
    }

    public async Task WriteBatteryPowerAsync(double valueWatts, string sourceName)
    {
        var config = _configService.GetConfiguration();
        var timestamp = GetUnixNanoseconds();
        var lineProtocol = $"battery_power,source={EscapeTag(sourceName)} value={valueWatts} {timestamp}";
        await WriteLineProtocolAsync(lineProtocol, config.InfluxDB);
    }

    public async Task WriteGridExportAsync(double exportWatts, double importWatts, string sourceName)
    {
        var config = _configService.GetConfiguration();
        var timestamp = GetUnixNanoseconds();
        var lineProtocol = $"grid,source={EscapeTag(sourceName)} export={exportWatts},import={importWatts} {timestamp}";
        await WriteLineProtocolAsync(lineProtocol, config.InfluxDB);
    }

    public async Task WriteForecastAsync(double forecastedWh, double confidencePercent, DateTime forecastHour, double peakW)
    {
        var config = _configService.GetConfiguration();
        if (!config.InfluxDB.EnableInfluxDB || !config.InfluxDB.WriteForecasts)
            return;

        var timestamp = ConvertToUnixNanoseconds(forecastHour);
        var lineProtocol = $"forecast_hourly forecastedWh={forecastedWh},confidence={confidencePercent},peakW={peakW} {timestamp}";
        await WriteLineProtocolAsync(lineProtocol, config.InfluxDB);
    }

    public async Task WriteDailyForecastAsync(double forecastedWh, double confidencePercent, DateTime forecastDay)
    {
        var config = _configService.GetConfiguration();
        if (!config.InfluxDB.EnableInfluxDB || !config.InfluxDB.WriteForecasts)
            return;

        var timestamp = ConvertToUnixNanoseconds(forecastDay);
        var lineProtocol = $"forecast_daily forecastedWh={forecastedWh},confidence={confidencePercent} {timestamp}";
        await WriteLineProtocolAsync(lineProtocol, config.InfluxDB);
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var config = _configService.GetConfiguration();
            if (!config.InfluxDB.EnableInfluxDB || string.IsNullOrWhiteSpace(config.InfluxDB.Url))
                return false;

            await AuthenticateAsync(config.InfluxDB);

            var healthUrl = $"{config.InfluxDB.Url?.TrimEnd('/')}/health";
            var response = await _httpClient.GetAsync(healthUrl);
            
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "InfluxDB connection test failed");
            return false;
        }
    }

    private static string EscapeTag(string tag)
    {
        return tag.Replace(" ", "\\ ").Replace(",", "\\,").Replace("=", "\\=");
    }

    private static long GetUnixNanoseconds()
    {
        // Convert current UTC time to Unix nanoseconds
        var utcNow = DateTime.UtcNow;
        var ticks = utcNow.Ticks - 621355968000000000; // Number of 100-ns intervals since Unix epoch
        return ticks * 100;
    }

    private static long ConvertToUnixNanoseconds(DateTime dateTime)
    {
        // Convert specific DateTime to Unix nanoseconds
        var utcDateTime = dateTime.Kind == DateTimeKind.Utc ? dateTime : dateTime.ToUniversalTime();
        var ticks = utcDateTime.Ticks - 621355968000000000; // Number of 100-ns intervals since Unix epoch
        return ticks * 100;
    }
}
