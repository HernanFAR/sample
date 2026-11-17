using System.Security.Claims;
using CookieLab.Components;
using CookieLab.Handlers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication("LabCookie").AddCookie("LabCookie", options =>
{
    options.Cookie.Name = "Lab.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
});
builder.Services.AddAuthorization();
builder.Services.AddTransient<IncludeBrowserCredentialsHandler>();
builder.Services.AddHttpClient("baseline")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
builder.Services.AddHttpClient("browser-credentials")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
    .AddHttpMessageHandler<IncludeBrowserCredentialsHandler>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/login", async (HttpContext context) =>
{
    var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "lab-user"), new Claim(ClaimTypes.Name, "lab-user")],
        "LabCookie"));
    await context.SignInAsync("LabCookie", principal);
    return Results.Redirect("/");
});

// LAB ONLY: GET signout simplifies an isolated browser experiment.
// Never copy this pattern to production; logout should be CSRF-protected.
app.MapGet("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync("LabCookie");
    return Results.Text("Logged out in browser; existing Blazor circuit not forcibly disconnected.");
});

app.MapGet("/api/whoami", (HttpContext context) =>
    Results.Json(new
    {
        authenticated = context.User.Identity?.IsAuthenticated == true,
        name = context.User.Identity?.Name,
        cookieHeaderPresent = context.Request.Headers.ContainsKey("Cookie"),
        credentialOptionObserved = context.Request.Headers["X-Lab-Client-Option"].ToString()
    }));

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
