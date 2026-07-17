using LoxoneSolarForecast.Models.ViewModels;
using LoxoneSolarForecast.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LoxoneSolarForecast.Pages;

public class RecommendationsModel : PageModel
{
    private readonly IRecommendationService _recommendationService;
    private readonly IConfigurationService _configService;

    public RecommendationViewModel Recommendations { get; set; } = new();

    public RecommendationsModel(IRecommendationService recommendationService, IConfigurationService configService)
    {
        _recommendationService = recommendationService;
        _configService = configService;
    }

    public async Task OnGetAsync()
    {
        Recommendations = await _recommendationService.GetRecommendationsAsync();
        var config = _configService.GetConfiguration();
        ViewData["Title"] = "Energy Optimization";
        ViewData["AppName"] = config.General.ApplicationName;
    }
}
