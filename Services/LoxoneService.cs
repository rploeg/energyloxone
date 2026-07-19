using LoxoneSolarForecast.Data;
using LoxoneSolarForecast.Models.Configuration;
using LoxoneSolarForecast.Models.Entities;
using LoxoneSolarForecast.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LoxoneSolarForecast.Services;

public interface ILoxoneService
{
    Task CollectDataAsync();
    Task PushToLoxoneAsync(Dictionary<string, string> values);
    Task<bool> TestConnectionAsync();
    Task<double?> ReadValueAsync(string url, string username, string password);
    ConnectionHealth GetConnectionHealth();
}

public class LoxoneService : ILoxoneService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IConfigurationService _configService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IInfluxDBService _influxDBService;
    private readonly ILogger<LoxoneService> _logger;

    private bool _lastConnectionSuccess;
    private DateTime? _lastConnectionCheck;
    private string? _lastConnectionError;

    public LoxoneService(
        IDbContextFactory<AppDbContext> dbFactory,
        IConfigurationService configService,
        IHttpClientFactory httpClientFactory,
        IInfluxDBService influxDBService,
        ILogger<LoxoneService> logger)
    {
        _dbFactory = dbFactory;
        _configService = configService;
        _httpClientFactory = httpClientFactory;
        _influxDBService = influxDBService;
        _logger = logger;
    }

    public ConnectionHealth GetConnectionHealth() => new()
    {
        Loxone = _lastConnectionSuccess,
        LoxoneLastCheck = _lastConnectionCheck,
        LoxoneError = _lastConnectionError,
    };

    public async Task CollectDataAsync()
    {
        var config = _configService.GetConfiguration();
        if (string.IsNullOrWhiteSpace(config.Loxone.IpAddress))
        {
            _logger.LogDebug("Loxone IP not configured, skipping data collection");
            return;
        }

        var dataSources = config.LoxoneDataSources.Where(ds => ds.IsActive).ToList();
        if (!dataSources.Any())
        {
            _logger.LogWarning("No active Loxone data sources configured. Total sources: {Total}", config.LoxoneDataSources.Count);
            return;
        }

        _logger.LogDebug("Active Loxone data sources: {Sources}", string.Join(", ", dataSources.Select(s => $"{s.Name}({s.DataType})")));

        using var db = await _dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        bool anySuccess = false;

        foreach (var source in dataSources)
        {
            try
            {
                // Handle URLs that are already fully formed vs. template URLs
                var url = source.Url.StartsWith("http://") || source.Url.StartsWith("https://")
                    ? source.Url
                    : source.Url.Replace("{loxone}", $"http://{config.Loxone.IpAddress}:{config.Loxone.Port}");

                // Clean up any double http:// (from corrupted URLs)
                while (url.Contains("http://http://"))
                {
                    url = url.Replace("http://http://", "http://");
                }

                var value = await ReadValueAsync(url, config.Loxone.Username, config.Loxone.Password);
                if (value == null)
                {
                    _logger.LogDebug("No value returned from {SourceName} ({DataType}) at {Url}", source.Name, source.DataType, url);
                    continue;
                }

                anySuccess = true;

                switch (source.DataType)
                {
                    case "PVProduction":
                        _logger.LogDebug("PVProduction: {SourceName} = {Value} W", source.Name, value.Value);
                        await db.ProductionHistory.AddAsync(new ProductionHistory
                        {
                            Timestamp = now,
                            ValueWatts = value.Value,
                            Source = source.Name,
                        });
                        await _influxDBService.WriteProductionAsync(value.Value, source.Name);
                        _logger.LogDebug("Wrote PVProduction to InfluxDB: {SourceName}", source.Name);
                        break;

                    case "Consumption":
                        // P1 meter: positive = consumption, negative = export
                        // Only count positive values as consumption
                        _logger.LogDebug("Consumption reading: {SourceName} = {Value} W", source.Name, value.Value);
                        if (value.Value > 0)
                        {
                            await db.ConsumptionHistory.AddAsync(new ConsumptionHistory
                            {
                                Timestamp = now,
                                ValueWatts = value.Value,
                                Source = source.Name,
                            });
                            await _influxDBService.WriteConsumptionAsync(value.Value, source.Name);
                            _logger.LogDebug("Wrote Consumption to InfluxDB: {SourceName}", source.Name);
                        }
                        // When negative, it's export - store in GridHistory
                        if (value.Value < 0)
                        {
                            await db.GridHistory.AddAsync(new GridHistory
                            {
                                Timestamp = now,
                                ExportWatts = Math.Abs(value.Value),
                                ImportWatts = 0,
                                Source = source.Name,
                            });
                            await _influxDBService.WriteGridExportAsync(Math.Abs(value.Value), 0, source.Name);
                        }
                        break;

                    case "BatterySOC":
                        var existing = await db.BatteryHistory
                            .Where(b => b.Timestamp > now.AddMinutes(-10))
                            .OrderByDescending(b => b.Timestamp)
                            .FirstOrDefaultAsync();
                        if (existing != null)
                            existing.SocPercent = value.Value;
                        else
                            await db.BatteryHistory.AddAsync(new BatteryHistory
                            {
                                Timestamp = now,
                                SocPercent = value.Value,
                                Source = source.Name,
                            });
                        break;

                    case "BatteryPower":
                        var existingBatt = await db.BatteryHistory
                            .Where(b => b.Timestamp > now.AddMinutes(-10))
                            .OrderByDescending(b => b.Timestamp)
                            .FirstOrDefaultAsync();
                        if (existingBatt != null)
                            existingBatt.ChargePowerWatts = value.Value;
                        break;

                    case "GridExport":
                        await db.GridHistory.AddAsync(new GridHistory
                        {
                            Timestamp = now,
                            ExportWatts = Math.Max(0, value.Value),
                            ImportWatts = Math.Max(0, -value.Value),
                            Source = source.Name,
                        });
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to collect data from source {Name}", source.Name);
            }
        }

        _lastConnectionSuccess = anySuccess;
        _lastConnectionCheck = now;
        if (!anySuccess)
            _lastConnectionError = "No data sources returned values";
        else
            _lastConnectionError = null;

        await db.SaveChangesAsync();
        _logger.LogDebug("Loxone data collection complete. Success: {Success}", anySuccess);
    }

    public async Task PushToLoxoneAsync(Dictionary<string, string> values)
    {
        var config = _configService.GetConfiguration();
        var pushTargets = config.LoxonePushTargets.Where(t => t.IsActive).ToList();
        if (!pushTargets.Any()) return;

        using var db = await _dbFactory.CreateDbContextAsync();

        foreach (var target in pushTargets)
        {
            if (!values.TryGetValue(target.DataKey, out var value)) continue;

            var url = target.Url
                .Replace("{loxone}", $"{config.Loxone.IpAddress}:{config.Loxone.Port}")
                .Replace("{value}", Uri.EscapeDataString(value));

            int retryCount = 0;
            bool success = false;
            string? error = null;
            int? statusCode = null;

            while (retryCount < 3 && !success)
            {
                try
                {
                    using var client = CreateLoxoneClient(config.Loxone);
                    var response = await client.GetAsync(url);
                    statusCode = (int)response.StatusCode;
                    success = response.IsSuccessStatusCode;
                    if (!success)
                        error = $"HTTP {statusCode}";
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    retryCount++;
                    if (retryCount < 3)
                        await Task.Delay(TimeSpan.FromSeconds(2 * retryCount));
                }
            }

            await db.LoxonePushLogs.AddAsync(new LoxonePushLog
            {
                Timestamp = DateTime.UtcNow,
                TargetName = target.Name,
                Url = url,
                Value = value,
                Success = success,
                HttpStatusCode = statusCode,
                ErrorMessage = error,
                RetryCount = retryCount,
            });

            if (!success)
                _logger.LogWarning("Push to Loxone target {Name} failed after {Retries} retries: {Error}",
                    target.Name, retryCount, error);
        }

        await db.SaveChangesAsync();
    }

    public async Task<bool> TestConnectionAsync()
    {
        var config = _configService.GetConfiguration();
        return await _configService.TestLoxoneConnectionAsync(
            config.Loxone.IpAddress, config.Loxone.Port,
            config.Loxone.Username, config.Loxone.Password);
    }

    public async Task<double?> ReadValueAsync(string url, string username, string password)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            if (!string.IsNullOrWhiteSpace(username))
            {
                var credentials = Convert.ToBase64String(
                    System.Text.Encoding.ASCII.GetBytes($"{username}:{password}"));
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            }

            var response = await client.GetStringAsync(url);

            // Try XML first (Loxone default format): <LL control="..." value="123.45" Code="200"/>
            if (response.TrimStart().StartsWith("<"))
            {
                try
                {
                    var xmlDoc = new System.Xml.XmlDocument();
                    xmlDoc.LoadXml(response);
                    var root = xmlDoc.DocumentElement;
                    
                    if (root?.Name == "LL" && root.GetAttribute("value") is string xmlValue && !string.IsNullOrEmpty(xmlValue))
                    {
                        if (double.TryParse(xmlValue, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                        {
                            return parsed;
                        }
                    }
                }
                catch
                {
                    // Fall through to JSON parsing
                }
            }

            // Try JSON format: {"LL":{"value":"123.45",...}}
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("LL", out var ll) &&
                ll.TryGetProperty("value", out var val))
            {
                var strVal = val.ValueKind == JsonValueKind.String
                    ? val.GetString()
                    : val.GetRawText();

                if (double.TryParse(strVal, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
            }

            // Try plain number
            if (double.TryParse(response.Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var plain))
                return plain;

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read value from {Url}", url);
            return null;
        }
    }

    private static HttpClient CreateLoxoneClient(LoxoneSettings settings)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            var credentials = Convert.ToBase64String(
                System.Text.Encoding.ASCII.GetBytes($"{settings.Username}:{settings.Password}"));
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        }
        return client;
    }
}
