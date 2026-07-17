using LoxoneSolarForecast.Data;
using LoxoneSolarForecast.Models.ViewModels;
using LoxoneSolarForecast.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LoxoneSolarForecast.Pages;

public class LogsModel : PageModel
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IConfigurationService _configService;

    public LogViewModel LogData { get; set; } = new();

    public LogsModel(IDbContextFactory<AppDbContext> dbFactory, IConfigurationService configService)
    {
        _dbFactory = dbFactory;
        _configService = configService;
    }

    public async Task OnGetAsync(
        string? level = null,
        string? component = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        int page = 0)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var config = _configService.GetConfiguration();
        ViewData["Title"] = "Logs";
        ViewData["AppName"] = config.General.ApplicationName;

        var query = db.AppLogs.AsQueryable();

        if (!string.IsNullOrEmpty(level))
            query = query.Where(l => l.Level == level);

        if (!string.IsNullOrEmpty(component))
            query = query.Where(l => l.Component == component);

        if (dateFrom.HasValue)
            query = query.Where(l => l.Timestamp >= dateFrom.Value);

        if (dateTo.HasValue)
            query = query.Where(l => l.Timestamp < dateTo.Value.AddDays(1));

        var total = await query.CountAsync();

        var pageSize = 100;
        var logs = await query
            .OrderByDescending(l => l.Timestamp)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var components = await db.AppLogs
            .Select(l => l.Component)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        LogData = new LogViewModel
        {
            Logs = logs,
            LevelFilter = level,
            ComponentFilter = component,
            DateFrom = dateFrom,
            DateTo = dateTo,
            TotalCount = total,
            PageIndex = page,
            PageSize = pageSize,
            AvailableComponents = components,
        };
    }
}
