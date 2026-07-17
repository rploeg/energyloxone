using LoxoneSolarForecast.Models.ViewModels;
using LoxoneSolarForecast.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LoxoneSolarForecast.Pages;

public class ForecastModel : PageModel
{
    private readonly IForecastService _forecastService;
    private readonly IConfigurationService _configService;

    public ForecastViewModel Forecast { get; set; } = new();

    public ForecastModel(IForecastService forecastService, IConfigurationService configService)
    {
        _forecastService = forecastService;
        _configService = configService;
    }

    public async Task OnGetAsync()
    {
        Forecast = await _forecastService.GetForecastAsync();
        var config = _configService.GetConfiguration();
        ViewData["Title"] = "Forecast";
        ViewData["AppName"] = config.General.ApplicationName;
    }
}
