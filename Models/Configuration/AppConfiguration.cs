using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace LoxoneSolarForecast.Models.Configuration;

public class AppConfiguration
{
    public GeneralSettings General { get; set; } = new();
    public LoxoneSettings Loxone { get; set; } = new();
    public HomeWizardSettings HomeWizard { get; set; } = new();
    public InfluxDBSettings InfluxDB { get; set; } = new();
    public SolarSettings Solar { get; set; } = new();
    public LocationSettings Location { get; set; } = new();
    public NotificationSettings Notifications { get; set; } = new();
    public List<LoxoneDataSource> LoxoneDataSources { get; set; } = new();
    public List<LoxonePushTarget> LoxonePushTargets { get; set; } = new();
}

public class GeneralSettings
{
    public string ApplicationName { get; set; } = "LoxoneSolarForecast";
    public string Timezone { get; set; } = "Europe/Amsterdam";
    public int UpdateIntervalMinutes { get; set; } = 5; // Deprecated: per-source intervals now configured individually
    public int ForecastIntervalMinutes { get; set; } = 60; // How often to refresh solar forecast (minutes)
    public int WeatherUpdateIntervalMinutes { get; set; } = 60; // How often to update weather data (minutes)
    public int ForecastHorizonDays { get; set; } = 7;
    public bool EnableLearningEngine { get; set; } = true;
    public bool EnableRecommendations { get; set; } = true;
    public bool EnableLogging { get; set; } = true;
}

public class LoxoneSettings
{
    public string? IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 80;
    public string? Username { get; set; } = string.Empty;
    public string? Password { get; set; } = string.Empty;
    public bool EnablePush { get; set; } = false;
    public bool EnablePull { get; set; } = true;
}

public class InfluxDBSettings
{
    public bool EnableInfluxDB { get; set; } = false;
    public string? Url { get; set; } = string.Empty;
    public string? Organization { get; set; } = string.Empty;
    public string? Bucket { get; set; } = "Loxone";
    public string? Token { get; set; } = string.Empty;
    public bool WriteForecasts { get; set; } = false;
}

public class SolarSettings
{
    public List<SolarArray> Arrays { get; set; } = new();
}

public class SolarArray
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Main Array";
    public double InstalledPowerWp { get; set; } = 5000;
    public double AzimuthDegrees { get; set; } = 0; // 0=South, -90=East, 90=West
    public double TiltDegrees { get; set; } = 30;
    public string ModuleType { get; set; } = "Monocrystalline";
    public string InverterType { get; set; } = "String Inverter";
    public double SystemLossesPercent { get; set; } = 14;
    public double ShadingFactor { get; set; } = 1.0;
    public bool IsActive { get; set; } = true;
}

public class LocationSettings
{
    public double Latitude { get; set; } = 52.3676;
    public double Longitude { get; set; } = 4.9041;
    public double? Elevation { get; set; }
    public string Address { get; set; } = string.Empty;
}

public class NotificationSettings
{
    public bool EnableEmail { get; set; } = false;
    public string? SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUsername { get; set; } = string.Empty;
    public string? SmtpPassword { get; set; } = string.Empty;
    public string? FromAddress { get; set; } = string.Empty;
    public string? ToAddress { get; set; } = string.Empty;
    public bool EnableWebhook { get; set; } = false;
    public string? WebhookUrl { get; set; } = string.Empty;
    public bool NotifyForecastFailure { get; set; } = true;
    public bool NotifyLoxoneOffline { get; set; } = true;
    public bool NotifyWeatherApiUnavailable { get; set; } = true;
    public bool NotifyDatabaseIssues { get; set; } = true;
}

public class LoxoneDataSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string DataType { get; set; } = "PVProduction"; // PVProduction, Consumption, BatterySOC, BatteryPower, GridExport
    public string Unit { get; set; } = "W";
    public bool IsActive { get; set; } = true;
    public int UpdateIntervalMinutes { get; set; } = 5; // How often to collect data from this source
}

public class LoxonePushTarget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string DataKey { get; set; } = string.Empty; // e.g. SolarForecastToday
    public bool IsActive { get; set; } = true;
}

public class HomeWizardSettings
{
    public bool Enabled { get; set; } = false;
    public string? IpAddress { get; set; } = string.Empty;
    public int UpdateIntervalMinutes { get; set; } = 5; // How often to collect water data
}
