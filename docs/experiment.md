# Experiment: server HttpClient vs. browser cookies

## Question

Does `request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include)` inside a server-side `DelegatingHandler` cause `HttpClient` to use the *current* browser cookie jar in a .NET 8 Blazor Web App Interactive Server circuit?

## Design

Real Chromium, `InteractiveServer`, normal cookie authentication, two server `HttpClient` variants, one browser `fetch` positive control, and a logout in a **second tab** while the original Blazor circuit remains open.

- Server baseline: `HttpClientHandler.UseCookies=false`, no forwarded cookie.
- Server Include: same .NET handler plus `SetBrowserRequestCredentials(Include)`; a harmless diagnostic header proves the delegating handler ran.
- Browser control: `fetch('/api/whoami', {credentials:'include'})` executes in Chromium and transmits the actual browser cookie.
- Existing circuit: reads `AuthenticationStateProvider` before/after logout.
- Fresh browser navigation: checks authentication after logout.

The server endpoint only returns boolean cookie presence, authenticated status, username, and diagnostic marker. It never displays cookie values.

Expected: Browser control is authenticated before logout, loses authentication afterward; the existing circuit can remain authenticated until revalidated; server-side Include does not automatically use browser cookies.

## Run locally

Requires .NET 8, Node.js and Playwright Chromium.

```bash
dotnet run --project CookieLab.csproj --urls http://127.0.0.1:5088
cd tests
npm install
npx playwright install chromium
node browser.mjs
```

GitHub Actions executes automatically on the experiment branch and records an explicit PASS/FAIL.

## Important limits

This isolates **transport** and circuit auth-state behavior. It does not implement Endorsment's captured-cookie delegation, account-side revocation, or a production-grade logout. The lab uses an intentionally GET-based logout endpoint for test simplicity; never copy that into production. It does not prove that a different custom handler/provider cannot forward browser credentials. It establishes what this particular browser-credentials option does under the server transport.

A result must be read from an actual executed workflow, not inferred from code.

## Extension: CookieContainer

Two more .NET 8 server transports are exercised by real Chromium:

1. `empty-cookie-container`: `HttpClientHandler.UseCookies=true` with a fresh server-side jar. Browser login does **not** populate it automatically.
2. `shared-cookie-container`: a dedicated server-side jar is explicitly populated from an authenticated browser HTTP request via the **lab-only** `POST /lab/seed` endpoint. This shows forwarding works when a cookie is explicitly copied into the jar, while browser logout does not clear that independent jar.

A second isolated browser context logs in as `other-user` and invokes the same shared jar. The test checks whether it reuses `lab-user`'s cookie, demonstrating why a singleton/shared `CookieContainer` is unsuitable for multi-user request delegation. No cookie contents are logged. The experiment deliberately shares the jar to reveal the risk and must not be copied into production.

These tests **do not** mean `CookieContainer` cannot be used safely when properly scoped and managed. They show that it is not a live connection to browser cookies, and that revocation/isolation must be handled separately.
