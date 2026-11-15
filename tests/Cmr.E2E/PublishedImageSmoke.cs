using Microsoft.Playwright;
using Xunit;

namespace Cmr.E2E;

/// <summary>
/// Runs only when CI sets CMR_PUBLISHED_URL to a container built from the production Dockerfile.
/// dotnet run is not this test: that path rewrites the shell, and the phone loads the image.
/// </summary>
public class PublishedImageSmoke
{
    [Fact]
    public async Task Mobile_chrome_reaches_the_login_screen()
    {
        var url = Environment.GetEnvironmentVariable("CMR_PUBLISHED_URL");
        if (string.IsNullOrWhiteSpace(url))
            return;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var html = await http.GetStringAsync(url);
        Assert.DoesNotContain("#[.{fingerprint}]", html);
        Assert.Contains("blazor.webassembly.", html, StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await http.GetAsync(url.TrimEnd('/') + "/_framework/blazor.boot.json")).StatusCode);

        Microsoft.Playwright.Program.Main(["install", "chromium"]);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            IsMobile = true,
            HasTouch = true,
            UserAgent = "Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36"
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(60_000);
        var failed = new List<string>();
        page.RequestFailed += (_, request) =>
        {
            if (request.Url.Contains("/_framework/", StringComparison.Ordinal) && !request.Url.Contains("blazor.boot.json", StringComparison.Ordinal))
                failed.Add(request.Url);
        };
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByTestId("home-login").WaitForAsync();
        Assert.Equal(0, await page.GetByText("खुल रहा है").CountAsync());
        Assert.Empty(failed);
    }
}
