using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace CookieLab.Handlers;

public sealed class IncludeBrowserCredentialsHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        request.Headers.TryAddWithoutValidation("X-Lab-Client-Option", "Include");
        return base.SendAsync(request, cancellationToken);
    }
}
