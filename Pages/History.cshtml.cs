using LoxoneSolarForecast.Models.ViewModels;
using LoxoneSolarForecast.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LoxoneSolarForecast.Pages;

public class HistoryModel : PageModel
{
    private readonly IHistoryService _historyService;
    private readonly IConfigurationService _configService;

    public HistoryViewModel History { get; set; } = new();

    public HistoryModel(IHistoryService historyService, IConfigurationService configService)
    {
        _historyService = historyService;
        _configService = configService;
    }

    public async Task OnGetAsync(string period = "week")
    {
        History = await _historyService.GetHistoryAsync(period);
        var config = _configService.GetConfiguration();
        ViewData["Title"] = "History";
        ViewData["AppName"] = config.General.ApplicationName;
    }
}
