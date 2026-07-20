using LoxoneSolarForecast.Models.Configuration;
using LoxoneSolarForecast.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LoxoneSolarForecast.Pages;

public class ConfigurationModel : PageModel
{
    private readonly IConfigurationService _configService;
    private readonly ILogger<ConfigurationModel> _logger;
    
    public ConfigurationModel(IConfigurationService configService, ILogger<ConfigurationModel> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    [BindProperty]
    public AppConfiguration Config { get; set; } = new();

    public string? StatusMessage { get; set; }
    public bool IsError { get; set; }

    public void OnGet()
    {
        Config = _configService.GetConfiguration();
        ViewData["Title"] = "Configuration";
        ViewData["AppName"] = Config.General.ApplicationName;
    }

    public IActionResult OnPost()
    {
        // Remove validation errors for complex nested list objects
        var keysToRemove = ModelState.Keys
            .Where(k => k.Contains("Config.LoxoneDataSources[") || k.Contains("Config.LoxonePushTargets["))
            .ToList();
        
        foreach (var key in keysToRemove)
        {
            ModelState.Remove(key);
        }

        if (!ModelState.IsValid)
        {
            IsError = true;
            StatusMessage = "Please fix validation errors.";
            return Page();
        }

        // Preserve passwords if the form fields are empty (browsers don't auto-populate password fields)
        var currentConfig = _configService.GetConfiguration();
        if (string.IsNullOrWhiteSpace(Config.Loxone.Password))
        {
            Config.Loxone.Password = currentConfig.Loxone.Password;
        }
        if (string.IsNullOrWhiteSpace(Config.InfluxDB.Token))
        {
            Config.InfluxDB.Token = currentConfig.InfluxDB.Token;
        }

        _configService.SaveConfiguration(Config);
        StatusMessage = "Configuration saved successfully.";
        ViewData["Title"] = "Configuration";
        ViewData["AppName"] = Config.General.ApplicationName;
        return Page();
    }

    public async Task<IActionResult> OnPostTestConnectionAsync()
    {
        Config = _configService.GetConfiguration();
        var success = await _configService.TestLoxoneConnectionAsync(
            Config.Loxone.IpAddress, Config.Loxone.Port,
            Config.Loxone.Username, Config.Loxone.Password);

        return new JsonResult(new { success, message = success ? "Connected!" : "Connection failed" });
    }

    public async Task<IActionResult> OnPostTestInfluxAsync([FromServices] IInfluxDBService influxDBService)
    {
        try
        {
            var result = await influxDBService.TestWriteAsync();
            return new JsonResult(new { 
                success = result.Success,
                message = result.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InfluxDB connection test failed");
            return new JsonResult(new { 
                success = false, 
                message = $"Error: {ex.Message}" 
            });
        }
    }

    public async Task<IActionResult> OnPostTestHomeWizardAsync([FromServices] IHomeWizardCollector homeWizardCollector)
    {
        try
        {
            var success = await homeWizardCollector.TestConnectionAsync();
            return new JsonResult(new { 
                success,
                message = success ? "HomeWizard connected! Data collected successfully." : "HomeWizard connection failed. Check IP address and network connectivity."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HomeWizard connection test failed");
            return new JsonResult(new { 
                success = false, 
                message = $"Error: {ex.Message}" 
            });
        }
    }
}
