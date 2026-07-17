using LoxoneSolarForecast.Models.ViewModels;
using LoxoneSolarForecast.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LoxoneSolarForecast.Pages;

public class IndexModel : PageModel
{
    private readonly IDashboardService _dashboardService;
    private readonly IConfigurationService _configService;

    public DashboardViewModel Dashboard { get; set; } = new();

    public IndexModel(IDashboardService dashboardService, IConfigurationService configService)
    {
        _dashboardService = dashboardService;
        _configService = configService;
    }

    public async Task OnGetAsync()
    {
        Dashboard = await _dashboardService.GetDashboardDataAsync();
        ViewData["Title"] = "Dashboard";
        ViewData["AppName"] = Dashboard.ApplicationName;
    }
}
