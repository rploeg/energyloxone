using LoxoneSolarForecast.Models.ViewModels;
using LoxoneSolarForecast.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LoxoneSolarForecast.Pages;

public class MonitoringModel : PageModel
{
    private readonly IHistoryService _historyService;
    private readonly IConfigurationService _configService;

    public MonitoringViewModel Monitoring { get; set; } = new();

    public MonitoringModel(IHistoryService historyService, IConfigurationService configService)
    {
        _historyService = historyService;
        _configService = configService;
    }

    public async Task OnGetAsync()
    {
        Monitoring = await _historyService.GetMonitoringDataAsync();
        var config = _configService.GetConfiguration();
        ViewData["Title"] = "Monitoring";
        ViewData["AppName"] = config.General.ApplicationName;
    }
}
