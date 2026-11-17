using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Server.Circuits;
namespace CookieLab.Handlers;

/// <summary>
/// Lab: one CookieContainer and HttpClient per Blazor circuit.
/// Never route these through IHttpClientFactory handler pools.
/// </summary>
public sealed class CircuitScopedCookieClient : CircuitHandler, IDisposable
{
    private readonly IHttpContextAccessor accessor;
    private readonly CookieContainer jar = new();
    private readonly HttpClient client;
    private readonly string scopeId = Guid.NewGuid().ToString("N");
    private bool captured;
    private string? capturedAccount;
    private string? captureStatus;

    public CircuitScopedCookieClient(IHttpContextAccessor accessor)
    {
        this.accessor = accessor;
        client = new HttpClient(new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = jar,
            AllowAutoRedirect = false
        }, disposeHandler: true);
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var context = accessor.HttpContext;
        if (context is null) captureStatus = "missing-http-context";
        else if (context.User.Identity?.IsAuthenticated != true) captureStatus = "unauthenticated-connection";
        else if (!context.Request.Cookies.TryGetValue("Lab.Auth", out var raw)) captureStatus = "missing-cookie";
        else
        {
            var origin = new Uri($"{context.Request.Scheme}://{context.Request.Host}/");
            jar.Add(origin, new Cookie("Lab.Auth", raw, "/", context.Request.Host.Host));
            capturedAccount = context.User.Identity.Name;
            captured = true;
            captureStatus = "captured";
        }
        return Task.CompletedTask;
    }

    public async Task<string> ProbeAsync(Uri target)
    {
        using var response = await client.GetAsync(target);
        var payload = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(payload);
        return JsonSerializer.Serialize(new
        {
            scopeId, captured, captureStatus, capturedAccount,
            owner = doc.RootElement.Clone()
        });
    }

    public void Dispose() => client.Dispose();
}
