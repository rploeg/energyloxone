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
    Task WriteWaterFlowAsync(double literPerMinute, string sourceName);
    Task WriteWaterConsumptionAsync(double totalLiterM3, string sourceName);
    Task WriteForecastAsync(double forecastedWh, double confidencePercent, DateTime forecastHour, double peakW);
    Task WriteDailyForecastAsync(double forecastedWh, double confidencePercent, DateTime forecastDay);
    Task<bool> TestConnectionAsync();
    Task<(bool Success, string Message)> TestWriteAsync();
    Task<(double CurrentFlow, double TotalConsumption, double TodayConsumption, bool IsConnected)> GetWaterDashboardDataAsync();
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
            {
                _logger.LogDebug("InfluxDB write skipped: InfluxDB is disabled");
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.Url) || string.IsNullOrWhiteSpace(settings.Bucket))
            {
                _logger.LogWarning("InfluxDB write skipped: URL or Bucket is empty. Url='{Url}', Bucket='{Bucket}'", settings.Url, settings.Bucket);
                return;
            }

            await AuthenticateAsync(settings);

            var writeUrl = GetWriteUrl(settings);
            var content = new StringContent(lineProtocol, System.Text.Encoding.UTF8, "text/plain");
            var response = await _httpClient.PostAsync(writeUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("InfluxDB write failed: {StatusCode} to {Url} - {Error}", response.StatusCode, writeUrl, errorContent);
            }
            else
            {
                _logger.LogDebug("InfluxDB write success ({StatusCode}): {LineProtocol}", response.StatusCode, lineProtocol);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing to InfluxDB. Org={Org}, Bucket={Bucket}", settings.Organization, settings.Bucket);
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

    public async Task WriteWaterFlowAsync(double literPerMinute, string sourceName)
    {
        var config = _configService.GetConfiguration();
        var timestamp = GetUnixNanoseconds();
        var lineProtocol = $"water,source={EscapeTag(sourceName)},type=flow value={literPerMinute} {timestamp}";
        await WriteLineProtocolAsync(lineProtocol, config.InfluxDB);
    }

    public async Task WriteWaterConsumptionAsync(double totalLiterM3, string sourceName)
    {
        var config = _configService.GetConfiguration();
        var timestamp = GetUnixNanoseconds();
        var lineProtocol = $"water,source={EscapeTag(sourceName)},type=consumption value={totalLiterM3} {timestamp}";
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

    public async Task<(bool Success, string Message)> TestWriteAsync()
    {
        try
        {
            var config = _configService.GetConfiguration();
            var settings = config.InfluxDB;

            if (!settings.EnableInfluxDB)
                return (false, "InfluxDB is disabled in configuration.");

            if (string.IsNullOrWhiteSpace(settings.Url))
                return (false, "InfluxDB URL is empty.");

            if (string.IsNullOrWhiteSpace(settings.Organization))
                return (false, "InfluxDB Organization is empty.");

            if (string.IsNullOrWhiteSpace(settings.Bucket))
                return (false, "InfluxDB Bucket is empty.");

            if (string.IsNullOrWhiteSpace(settings.Token))
                return (false, "InfluxDB API Token is empty.");

            await AuthenticateAsync(settings);

            var writeUrl = GetWriteUrl(settings);
            var timestamp = GetUnixNanoseconds();
            var lineProtocol = $"connectivity_test,source=LoxoneSolarForecast value=1i {timestamp}";
            var content = new StringContent(lineProtocol, System.Text.Encoding.UTF8, "text/plain");

            var response = await _httpClient.PostAsync(writeUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return (false, $"Write test failed: {(int)response.StatusCode} {response.StatusCode} - {errorContent}");
            }

            return (true, $"Write test succeeded to org '{settings.Organization}', bucket '{settings.Bucket}'.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "InfluxDB write test failed");
            return (false, $"Write test exception: {ex.Message}");
        }
    }

    public async Task<(double CurrentFlow, double TotalConsumption, double TodayConsumption, bool IsConnected)> GetWaterDashboardDataAsync()
    {
        try
        {
            var settings = _configService.GetConfiguration().InfluxDB;
            if (!settings.EnableInfluxDB || string.IsNullOrWhiteSpace(settings.Token))
                return (0, 0, 0, false);

            var now = DateTime.UtcNow;
            var todayStart = now.Date;

            // Query: Current water flow (last value from last hour)
            var flowQuery = @"from(bucket:""loxone"")
  |> range(start: -1h)
  |> filter(fn: (r) => r._measurement == ""water"" and r.type == ""flow"")
  |> last()";

            var flowResult = await ExecuteFluxQueryAsync(settings, flowQuery);
            var currentFlow = ExtractFluxValue(flowResult);

            // Query: Total water consumption (last value)
            var totalQuery = @"from(bucket:""loxone"")
  |> range(start: -30d)
  |> filter(fn: (r) => r._measurement == ""water"" and r.type == ""consumption"")
  |> last()";

            var totalResult = await ExecuteFluxQueryAsync(settings, totalQuery);
            var totalConsumption = ExtractFluxValue(totalResult);

            // Query: Today water consumption (sum of flow over today)
            var todayFluxStart = ConvertToUnixNanoseconds(todayStart);
            var todayFluxNow = ConvertToUnixNanoseconds(now);
            
            var todayQuery = @$"from(bucket:""loxone"")
  |> range(start: {todayFluxStart}, stop: {todayFluxNow})
  |> filter(fn: (r) => r._measurement == ""water"" and r.type == ""flow"")
  |> integral(unit: 1m)
  |> sum()";

            var todayResult = await ExecuteFluxQueryAsync(settings, todayQuery);
            var todayConsumption = ExtractFluxValue(todayResult) / 1000; // Convert to m³

            return (currentFlow, totalConsumption, todayConsumption, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get water dashboard data from InfluxDB");
            return (0, 0, 0, false);
        }
    }

    private async Task<string> ExecuteFluxQueryAsync(InfluxDBSettings settings, string query)
    {
        var baseUrl = (settings.Url ?? "").TrimEnd('/');
        var url = $"{baseUrl}/api/v2/query?org={Uri.EscapeDataString(settings.Organization ?? "")}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        request.Headers.Add("Accept", "application/csv");
        request.Content = new StringContent(query, System.Text.Encoding.UTF8, "application/vnd.flux");

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"InfluxDB query failed: {response.StatusCode}");

        return await response.Content.ReadAsStringAsync();
    }

    private static double ExtractFluxValue(string csvContent)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
            return 0;

        // InfluxDB annotated CSV format:
        //   #datatype,...
        //   #group,...
        //   #default,_result,...
        //   ,result,table,_start,_stop,_time,_value,_field,_measurement,...  ← header
        //   ,_result,0,...,9.135,value,water,...                              ← data
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        int valueColIndex = -1;
        bool headerFound = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("#")) continue;

            if (!headerFound)
            {
                // Locate the _value column index from the header row
                var headers = line.Split(',');
                for (int i = 0; i < headers.Length; i++)
                {
                    if (headers[i].Trim() == "_value") { valueColIndex = i; break; }
                }
                headerFound = true;
                continue;
            }

            if (valueColIndex < 0) continue;

            // First data row — parse _value
            var parts = line.Split(',');
            if (parts.Length > valueColIndex)
            {
                var raw = parts[valueColIndex].Trim();
                if (double.TryParse(raw, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var result))
                    return result;
            }
        }

        return 0;
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
