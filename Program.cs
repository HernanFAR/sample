using System.Security.Claims;
using System.Net;
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
builder.Services.AddScoped<CircuitScopedCookieClient>();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler>(sp => sp.GetRequiredService<CircuitScopedCookieClient>());
// LAB ONLY: deliberately shared server-side CookieContainer to measure lifetime/isolation hazards.
builder.Services.AddSingleton(new CookieContainer());
builder.Services.AddHttpClient("baseline")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
builder.Services.AddHttpClient("browser-credentials")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
    .AddHttpMessageHandler<IncludeBrowserCredentialsHandler>();
builder.Services.AddHttpClient("empty-cookie-container")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        UseCookies = true, AllowAutoRedirect = false,
        CookieContainer = new CookieContainer()
    });
builder.Services.AddHttpClient("shared-cookie-container")
    .ConfigurePrimaryHttpMessageHandler(services => new HttpClientHandler
    {
        UseCookies = true, AllowAutoRedirect = false,
        CookieContainer = services.GetRequiredService<CookieContainer>()
    });

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/login", async (HttpContext context) =>
{
    var user = context.Request.Query["user"].ToString() == "other-user" ? "other-user" : "lab-user";
    var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, user), new Claim(ClaimTypes.Name, user)],
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

// LAB ONLY: browser authenticated request seeds a server-side shared cookie jar.
// This is an explicit cookie copy, not automatic propagation by HttpClientHandler.
app.MapPost("/lab/seed", (HttpContext context, CookieContainer jar) =>
{
    if (context.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    if (!context.Request.Cookies.TryGetValue("Lab.Auth", out var raw)) return Results.BadRequest();
    var origin = new Uri($"{context.Request.Scheme}://{context.Request.Host}/");
    jar.Add(origin, new Cookie("Lab.Auth", raw, "/", context.Request.Host.Host));
    return Results.Ok(new { seeded = true, forUser = context.User.Identity.Name });
}).DisableAntiforgery(); // Lab-only POST; a production equivalent must be CSRF protected.

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
