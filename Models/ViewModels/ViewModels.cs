namespace LoxoneSolarForecast.Models.ViewModels;

public class DashboardViewModel
{
    public double CurrentProductionWatts { get; set; }
    public double TodayForecastKwh { get; set; }
    public double TomorrowForecastKwh { get; set; }
    public double RemainingTodayKwh { get; set; }
    public double ConfidencePercent { get; set; }
    public double ExpectedSurplusKwh { get; set; }
    public double CurrentConsumptionWatts { get; set; }
    public double BatterySocPercent { get; set; }
    public double GridExportWatts { get; set; }
    public DateTime? PeakProductionTime { get; set; }
    public double PeakProductionWatts { get; set; }
    public bool LoxoneConnected { get; set; }
    public bool WeatherApiConnected { get; set; }
    public bool PvgisApiConnected { get; set; }
    public bool DatabaseConnected { get; set; }
    public List<HourlyDataPoint> HourlyForecast { get; set; } = new();
    public List<HourlyDataPoint> HourlyActual { get; set; } = new();
    public List<DailyDataPoint> DailyForecast { get; set; } = new();
    public DateTime LastUpdated { get; set; }
    public string ApplicationName { get; set; } = "LoxoneSolarForecast";
}

public class HourlyDataPoint
{
    public DateTime Time { get; set; }
    public double Value { get; set; }
}

public class DailyDataPoint
{
    public DateTime Date { get; set; }
    public double ForecastedKwh { get; set; }
    public double? ActualKwh { get; set; }
    public double Confidence { get; set; }
}

public class ForecastViewModel
{
    public List<DailyForecastItem> DailyForecasts { get; set; } = new();
    public List<HourlyForecastItem> TodayHourly { get; set; } = new();
    public List<HourlyForecastItem> TomorrowHourly { get; set; } = new();
    public double TodayKwh { get; set; }
    public double TomorrowKwh { get; set; }
    public double TodayConfidence { get; set; }
    public DateTime? TodayPeakTime { get; set; }
    public double TodayPeakWatts { get; set; }
    public DateTime LastGenerated { get; set; }
}

public class DailyForecastItem
{
    public DateTime Date { get; set; }
    public double ForecastedKwh { get; set; }
    public double PeakWatts { get; set; }
    public DateTime? PeakTime { get; set; }
    public double ConfidencePercent { get; set; }
    public double CloudCoverPercent { get; set; }
    public bool IsToday { get; set; }
    public bool IsTomorrow { get; set; }
}

public class HourlyForecastItem
{
    public DateTime Hour { get; set; }
    public double ForecastedWh { get; set; }
    public double ForecastedPeakW { get; set; }
    public double CloudCoverPercent { get; set; }
    public double DirectRadiation { get; set; }
    public double ConfidencePercent { get; set; }
}

public class HistoryViewModel
{
    public string Period { get; set; } = "week";
    public List<DailyHistoryItem> DailyItems { get; set; } = new();
    public double TotalProductionKwh { get; set; }
    public double TotalConsumptionKwh { get; set; }
    public double TotalExportKwh { get; set; }
    public double AverageDailyKwh { get; set; }
    public double BestDayKwh { get; set; }
    public DateTime? BestDay { get; set; }
    public double ForecastAccuracyPercent { get; set; }
}

public class DailyHistoryItem
{
    public DateTime Date { get; set; }
    public double ProductionKwh { get; set; }
    public double ConsumptionKwh { get; set; }
    public double ExportKwh { get; set; }
    public double? ForecastKwh { get; set; }
    public double? BatteryMinSoc { get; set; }
    public double? BatteryMaxSoc { get; set; }
}

public class MonitoringViewModel
{
    public SchedulerStatus SchedulerStatus { get; set; } = new();
    public ConnectionHealth ConnectionHealth { get; set; } = new();
    public SystemStatistics Statistics { get; set; } = new();
    public List<RecentActivity> RecentActivities { get; set; } = new();
}

public class SchedulerStatus
{
    public DateTime? LastDataCollection { get; set; }
    public DateTime? LastForecastGeneration { get; set; }
    public DateTime? LastLoxonePush { get; set; }
    public DateTime? NextDataCollection { get; set; }
    public DateTime? NextForecastGeneration { get; set; }
    public bool DataCollectionRunning { get; set; }
    public bool ForecastRunning { get; set; }
    public bool LoxonePushRunning { get; set; }
    public int DataCollectionIntervalMinutes { get; set; }
}

public class ConnectionHealth
{
    public bool WeatherApi { get; set; }
    public bool PvgisApi { get; set; }
    public bool Loxone { get; set; }
    public bool Database { get; set; }
    public DateTime? WeatherApiLastCheck { get; set; }
    public DateTime? LoxoneLastCheck { get; set; }
    public string? WeatherApiError { get; set; }
    public string? LoxoneError { get; set; }
}

public class SystemStatistics
{
    public double ForecastAccuracyPercent { get; set; }
    public double AverageDailyProductionKwh { get; set; }
    public double AverageDailyConsumptionKwh { get; set; }
    public int TotalDataPoints { get; set; }
    public int TotalForecasts { get; set; }
    public double LearningCorrectionFactor { get; set; }
    public DateTime? DataSince { get; set; }
}

public class RecentActivity
{
    public DateTime Timestamp { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsError { get; set; }
}

public class RecommendationViewModel
{
    public List<Recommendation> Recommendations { get; set; } = new();
    public double ExpectedSurplusKwh { get; set; }
    public TimeSpan? SurplusStartTime { get; set; }
    public TimeSpan? SurplusEndTime { get; set; }
    public DateTime? BatteryFullTime { get; set; }
    public DateTime? BestEvChargeStart { get; set; }
    public DateTime? BestEvChargeEnd { get; set; }
    public double AvailableSolarForEvKwh { get; set; }
}

public class Recommendation
{
    public string Category { get; set; } = string.Empty; // EV, Battery, HeatPump, Appliance
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public double? SolarEnergyKwh { get; set; }
    public string Priority { get; set; } = "Normal"; // High, Normal, Low
    public string Icon { get; set; } = "bi-lightning";
}

public class LogViewModel
{
    public List<Entities.AppLog> Logs { get; set; } = new();
    public string? LevelFilter { get; set; }
    public string? ComponentFilter { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int TotalCount { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; } = 100;
    public List<string> AvailableComponents { get; set; } = new();
}
