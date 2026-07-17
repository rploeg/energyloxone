using LoxoneSolarForecast.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace LoxoneSolarForecast.Pages;

public class LoginModel : PageModel
{
    private readonly IConfiguration _configuration;
    private readonly IConfigurationService _configService;

    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    public string? Error { get; set; }

    public LoginModel(IConfiguration configuration, IConfigurationService configService)
    {
        _configuration = configuration;
        _configService = configService;
    }

    public void OnGet()
    {
        ViewData["Title"] = "Login";
        ViewData["AppName"] = _configService.GetConfiguration().General.ApplicationName;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ViewData["Title"] = "Login";
        ViewData["AppName"] = _configService.GetConfiguration().General.ApplicationName;

        var adminUser = _configuration["Auth:Username"] ?? "admin";
        var adminPass = _configuration["Auth:Password"] ?? "solar123";

        if (Username != adminUser || Password != adminPass)
        {
            Error = "Invalid username or password.";
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, Username),
            new(ClaimTypes.Role, "Admin"),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });

        return LocalRedirect(returnUrl ?? "/");
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Login");
    }
}
