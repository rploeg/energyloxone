using LoxoneSolarForecast.Models.Entities;
using LoxoneSolarForecast.Models.ViewModels;
using System.Text.Json;

namespace LoxoneSolarForecast.Services;

public interface IWeatherService
{
    Task<WeatherForecastResult?> GetForecastAsync(double latitude, double longitude, int days);
    Task<bool> IsAvailableAsync();
}

public class WeatherForecastResult
{
    public List<HourlyWeatherPoint> HourlyData { get; set; } = new();
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}

public class HourlyWeatherPoint
{
    public DateTime Time { get; set; }
    public double CloudCoverPercent { get; set; }
    public double DirectRadiationWm2 { get; set; }
    public double DiffuseRadiationWm2 { get; set; }
    public double TemperatureCelsius { get; set; }
    public double WindSpeedMs { get; set; }
    public double HumidityPercent { get; set; }
    public double PrecipitationMm { get; set; }
    public double DirectNormalIrradiance { get; set; }
}

public class OpenMeteoWeatherService : IWeatherService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenMeteoWeatherService> _logger;
    private const string BaseUrl = "https://api.open-meteo.com/v1/forecast";

    public OpenMeteoWeatherService(IHttpClientFactory httpClientFactory, ILogger<OpenMeteoWeatherService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("OpenMeteo");
        _logger = logger;
    }

    public async Task<WeatherForecastResult?> GetForecastAsync(double latitude, double longitude, int days)
    {
        try
        {
            var url = $"{BaseUrl}?latitude={latitude:F4}&longitude={longitude:F4}" +
                      $"&hourly=cloud_cover,direct_radiation,diffuse_radiation,temperature_2m," +
                      $"wind_speed_10m,relative_humidity_2m,precipitation,direct_normal_irradiance" +
                      $"&forecast_days={days}&timezone=UTC&timeformat=unixtime";

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new WeatherForecastResult();
            var hourly = root.GetProperty("hourly");

            var times = hourly.GetProperty("time").EnumerateArray().ToList();
            var cloudCover = hourly.GetProperty("cloud_cover").EnumerateArray().ToList();
            var directRad = hourly.GetProperty("direct_radiation").EnumerateArray().ToList();
            var diffuseRad = hourly.GetProperty("diffuse_radiation").EnumerateArray().ToList();
            var temps = hourly.GetProperty("temperature_2m").EnumerateArray().ToList();
            var winds = hourly.GetProperty("wind_speed_10m").EnumerateArray().ToList();
            var humidity = hourly.GetProperty("relative_humidity_2m").EnumerateArray().ToList();
            var precip = hourly.GetProperty("precipitation").EnumerateArray().ToList();
            var dni = hourly.GetProperty("direct_normal_irradiance").EnumerateArray().ToList();

            for (int i = 0; i < times.Count; i++)
            {
                result.HourlyData.Add(new HourlyWeatherPoint
                {
                    Time = DateTimeOffset.FromUnixTimeSeconds(times[i].GetInt64()).UtcDateTime,
                    CloudCoverPercent = GetDouble(cloudCover, i),
                    DirectRadiationWm2 = GetDouble(directRad, i),
                    DiffuseRadiationWm2 = GetDouble(diffuseRad, i),
                    TemperatureCelsius = GetDouble(temps, i),
                    WindSpeedMs = GetDouble(winds, i) / 3.6,
                    HumidityPercent = GetDouble(humidity, i),
                    PrecipitationMm = GetDouble(precip, i),
                    DirectNormalIrradiance = GetDouble(dni, i),
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch weather forecast from Open-Meteo");
            return null;
        }
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{BaseUrl}?latitude=52.37&longitude=4.90&hourly=temperature_2m&forecast_days=1&timezone=UTC",
                HttpCompletionOption.ResponseHeadersRead);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static double GetDouble(List<JsonElement> list, int index)
    {
        if (index >= list.Count) return 0;
        var el = list[index];
        if (el.ValueKind == JsonValueKind.Null || el.ValueKind == JsonValueKind.Undefined) return 0;
        return el.TryGetDouble(out var v) ? v : 0;
    }
}
