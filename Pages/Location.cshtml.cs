using LoxoneSolarForecast.Models.Configuration;
using LoxoneSolarForecast.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LoxoneSolarForecast.Pages;

public class LocationModel : PageModel
{
    private readonly IConfigurationService _configService;
    private readonly IForecastService _forecastService;

    public LocationModel(IConfigurationService configService, IForecastService forecastService)
    {
        _configService = configService;
        _forecastService = forecastService;
    }

    [BindProperty]
    public LocationSettings Location { get; set; } = new();

    public string? StatusMessage { get; set; }

    public void OnGet()
    {
        var config = _configService.GetConfiguration();
        Location = config.Location;
        ViewData["Title"] = "Location";
        ViewData["AppName"] = config.General.ApplicationName;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var config = _configService.GetConfiguration();
        config.Location = Location;
        _configService.SaveConfiguration(config);
        await _forecastService.GenerateForecastAsync();
        StatusMessage = "Location saved and forecast updated.";
        ViewData["Title"] = "Location";
        ViewData["AppName"] = config.General.ApplicationName;
        return Page();
    }
}
