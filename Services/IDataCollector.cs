namespace LoxoneSolarForecast.Services;

/// <summary>
/// Generic interface for all data collection sources (Loxone, HomeWizard, Grid APIs, etc)
/// Enables scalable multi-source data aggregation platform
/// </summary>
public interface IDataCollector
{
    /// <summary>
    /// Unique name for this data collector (e.g., "Loxone", "HomeWizard", "GridAPI")
    /// </summary>
    string SourceName { get; }

    /// <summary>
    /// Collect data from the source and write to InfluxDB
    /// </summary>
    Task CollectAsync();

    /// <summary>
    /// Test connectivity to the data source
    /// </summary>
    Task<bool> TestConnectionAsync();

    /// <summary>
    /// Get current connection health status
    /// </summary>
    DataCollectorHealth GetHealth();
}

public class DataCollectorHealth
{
    public string SourceName { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public DateTime? LastCheck { get; set; }
    public DateTime? LastSuccessfulCollection { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Status { get; set; } // "OK", "WARNING", "ERROR"
}
