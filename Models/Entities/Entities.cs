namespace LoxoneSolarForecast.Models.Entities;

public class ProductionHistory
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public double ValueWatts { get; set; }
    public string Source { get; set; } = string.Empty;
    public Guid? ArrayId { get; set; }
}

public class ConsumptionHistory
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public double ValueWatts { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class BatteryHistory
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public double SocPercent { get; set; }
    public double ChargePowerWatts { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class GridHistory
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public double ExportWatts { get; set; }
    public double ImportWatts { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class ForecastHistory
{
    public long Id { get; set; }
    public DateTime ForecastedFor { get; set; }
    public DateTime GeneratedAt { get; set; }
    public double ForecastedWh { get; set; }
    public double? ActualWh { get; set; }
    public double? ErrorPercent { get; set; }
    public double ConfidencePercent { get; set; }
    public string Source { get; set; } = "OpenMeteo";
}

public class HourlyForecast
{
    public long Id { get; set; }
    public DateTime Hour { get; set; }
    public DateTime GeneratedAt { get; set; }
    public double ForecastedWh { get; set; }
    public double ForecastedPeakW { get; set; }
    public double ConfidencePercent { get; set; }
    public double CloudCoverPercent { get; set; }
    public double DirectRadiationWm2 { get; set; }
    public double DiffuseRadiationWm2 { get; set; }
    public double TemperatureCelsius { get; set; }
    public double WindSpeedMs { get; set; }
}

public class LearningData
{
    public long Id { get; set; }
    public DateTime Date { get; set; }
    public double ForecastedWh { get; set; }
    public double ActualWh { get; set; }
    public double EfficiencyFactor { get; set; }
    public double CorrectionFactor { get; set; }
    public string Season { get; set; } = string.Empty;
}

public class ShadingProfile
{
    public long Id { get; set; }
    public int HourOfDay { get; set; }
    public int MonthOfYear { get; set; }
    public double AverageReductionPercent { get; set; }
    public int SampleCount { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class AppLog
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "Info";
    public string Component { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public string? Properties { get; set; }
}

public class LoxonePushLog
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Value { get; set; }
    public bool Success { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
}
