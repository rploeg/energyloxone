using LoxoneSolarForecast.Models.Configuration;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace LoxoneSolarForecast.Services;

public interface IConfigurationService
{
    AppConfiguration GetConfiguration();
    void SaveConfiguration(AppConfiguration config);
    Task<bool> TestLoxoneConnectionAsync(string ip, int port, string username, string password);
}

public class ConfigurationService : IConfigurationService
{
    private readonly string _configPath;
    private readonly IDataProtector _protector;
    private readonly ILogger<ConfigurationService> _logger;
    private AppConfiguration? _cached;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ConfigurationService(
        IConfiguration configuration,
        IDataProtectionProvider dataProtection,
        ILogger<ConfigurationService> logger)
    {
        _configPath = Path.Combine(
            configuration["Storage:ConfigPath"] ?? "/config",
            "settings.json");
        _protector = dataProtection.CreateProtector("LoxoneSolarForecast.Secrets");
        _logger = logger;

        EnsureConfigDirectory();
    }

    private void EnsureConfigDirectory()
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    public AppConfiguration GetConfiguration()
    {
        lock (_lock)
        {
            if (_cached != null) return _cached;

            if (!File.Exists(_configPath))
            {
                _cached = CreateDefaultConfiguration();
                SaveConfigurationInternal(_cached);
                return _cached;
            }

            try
            {
                var json = File.ReadAllText(_configPath);
                _cached = JsonSerializer.Deserialize<AppConfiguration>(json, _jsonOptions)
                          ?? CreateDefaultConfiguration();
                return _cached;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration, using defaults");
                _cached = CreateDefaultConfiguration();
                return _cached;
            }
        }
    }

    public void SaveConfiguration(AppConfiguration config)
    {
        lock (_lock)
        {
            SaveConfigurationInternal(config);
            _cached = config;
        }
    }

    private void SaveConfigurationInternal(AppConfiguration config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration");
            throw;
        }
    }

    public async Task<bool> TestLoxoneConnectionAsync(string ip, int port, string username, string password)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var credentials = Convert.ToBase64String(
                System.Text.Encoding.ASCII.GetBytes($"{username}:{password}"));
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

            var url = $"http://{ip}:{port}/jdev/sps/LoxAPPversion3";
            var response = await client.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static AppConfiguration CreateDefaultConfiguration()
    {
        var config = new AppConfiguration();
        config.LoxoneDataSources = new List<LoxoneDataSource>
        {
            new() { Name = "PV Production", Url = "http://{loxone}/dev/sps/io/PVPower", DataType = "PVProduction", Unit = "W", IsActive = true },
            new() { Name = "House Consumption", Url = "http://{loxone}/dev/sps/io/HouseConsumption", DataType = "Consumption", Unit = "W", IsActive = true },
            new() { Name = "Battery SOC", Url = "http://{loxone}/dev/sps/io/BatterySOC", DataType = "BatterySOC", Unit = "%", IsActive = false },
            new() { Name = "Battery Power", Url = "http://{loxone}/dev/sps/io/BatteryPower", DataType = "BatteryPower", Unit = "W", IsActive = false },
            new() { Name = "Grid Export", Url = "http://{loxone}/dev/sps/io/GridExport", DataType = "GridExport", Unit = "W", IsActive = false },
        };
        config.LoxonePushTargets = new List<LoxonePushTarget>
        {
            new() { Name = "Solar Forecast Today", Url = "http://{loxone}/dev/sps/io/SolarForecastToday/{value}", DataKey = "SolarForecastToday", IsActive = false },
            new() { Name = "Solar Forecast Tomorrow", Url = "http://{loxone}/dev/sps/io/SolarForecastTomorrow/{value}", DataKey = "SolarForecastTomorrow", IsActive = false },
            new() { Name = "Solar Remaining Today", Url = "http://{loxone}/dev/sps/io/SolarRemainingToday/{value}", DataKey = "SolarRemainingToday", IsActive = false },
            new() { Name = "Solar Confidence", Url = "http://{loxone}/dev/sps/io/SolarConfidence/{value}", DataKey = "SolarConfidence", IsActive = false },
            new() { Name = "Solar Peak Time", Url = "http://{loxone}/dev/sps/io/SolarPeakTime/{value}", DataKey = "SolarPeakTime", IsActive = false },
            new() { Name = "Solar Peak Power", Url = "http://{loxone}/dev/sps/io/SolarPeakPower/{value}", DataKey = "SolarPeakPower", IsActive = false },
        };
        return config;
    }
}

// Extension so LoxoneSettings can access its data sources from the parent
public static class AppConfigurationExtensions
{
    public static List<LoxoneDataSource> GetDataSources(this AppConfiguration config)
        => config.LoxoneDataSources;

    public static List<LoxonePushTarget> GetPushTargets(this AppConfiguration config)
        => config.LoxonePushTargets;
}
