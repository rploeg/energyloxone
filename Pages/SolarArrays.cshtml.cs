using LoxoneSolarForecast.Models.Configuration;
using LoxoneSolarForecast.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LoxoneSolarForecast.Pages;

public class SolarArraysModel : PageModel
{
    private readonly IConfigurationService _configService;
    private readonly IForecastService _forecastService;

    public SolarArraysModel(IConfigurationService configService, IForecastService forecastService)
    {
        _configService = configService;
        _forecastService = forecastService;
    }

    [BindProperty]
    public List<SolarArray> Arrays { get; set; } = new();

    public string? StatusMessage { get; set; }

    public void OnGet()
    {
        var config = _configService.GetConfiguration();
        Arrays = config.Solar.Arrays;
        ViewData["Title"] = "Solar Arrays";
        ViewData["AppName"] = config.General.ApplicationName;
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var config = _configService.GetConfiguration();
        config.Solar.Arrays = Arrays ?? new();
        _configService.SaveConfiguration(config);
        await _forecastService.GenerateForecastAsync();
        StatusMessage = "Solar arrays saved and forecast updated.";
        Arrays = config.Solar.Arrays;
        ViewData["Title"] = "Solar Arrays";
        ViewData["AppName"] = config.General.ApplicationName;
        return Page();
    }

    public IActionResult OnPostAddArray()
    {
        var config = _configService.GetConfiguration();
        config.Solar.Arrays.Add(new SolarArray { Name = $"Array {config.Solar.Arrays.Count + 1}" });
        _configService.SaveConfiguration(config);
        Arrays = config.Solar.Arrays;
        ViewData["Title"] = "Solar Arrays";
        ViewData["AppName"] = config.General.ApplicationName;
        return Page();
    }

    public IActionResult OnPostDeleteArray(Guid id)
    {
        var config = _configService.GetConfiguration();
        config.Solar.Arrays.RemoveAll(a => a.Id == id);
        _configService.SaveConfiguration(config);
        Arrays = config.Solar.Arrays;
        ViewData["Title"] = "Solar Arrays";
        ViewData["AppName"] = config.General.ApplicationName;
        return Page();
    }
}
