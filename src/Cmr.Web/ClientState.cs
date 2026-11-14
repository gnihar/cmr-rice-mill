using System.Net.Http.Json;
using Cmr.Contracts;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.JSInterop;

namespace Cmr.Web;

public sealed class LangState
{
    public string Code { get; private set; } = "hi";
    public event Action? Changed;
    public string H(string hi, string en) => Code == "en" ? en : hi;

    public async Task Init(IJSRuntime js)
    {
        var saved = await js.InvokeAsync<string?>("cmr.get", "cmr.lang");
        if (saved is "en" or "hi") Code = saved;
    }

    public async Task Set(IJSRuntime js, string code)
    {
        Code = code;
        await js.InvokeVoidAsync("cmr.set", "cmr.lang", code);
        Changed?.Invoke();
    }
}

public sealed class SessionState
{
    public MeDto? Me { get; set; }
    public int Queued { get; set; }
    public bool IsOwner => Me?.Role == "owner";
    public event Action? Changed;
    public void Touch() => Changed?.Invoke();
}

public sealed class ApiClient(HttpClient http, IJSRuntime js, SessionState session)
{
    public async Task<T?> Get<T>(string url) 
    {
        var response = await Send(HttpMethod.Get, url, null, queue: false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) return default;
        return await Read<T>(response);
    }

    public async Task<T> SendJson<T>(HttpMethod method, string url, object body, bool queue = true)
    {
        var response = await Send(method, url, body, queue);
        return await Read<T>(response);
    }

    public async Task SendEmpty(HttpMethod method, string url, object? body = null)
    {
        var response = await Send(method, url, body, queue: false);
        if (!response.IsSuccessStatusCode) await Read<object>(response);
    }

    public async Task<(bool Ok, string? Role, string? Error)> Login(string identifier, string pin)
    {
        var response = await Send(HttpMethod.Post, "api/auth/login", new LoginRequest(identifier, pin), queue: false);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadFromJsonAsync<Err>();
            return (false, null, err?.Message ?? "Login failed");
        }
        var ok = await response.Content.ReadFromJsonAsync<LoginOk>();
        return (true, ok?.Role, null);
    }

    public async Task Logout() => await Send(HttpMethod.Post, "api/auth/logout", null, queue: false);

    public async Task<MeDto?> Me()
    {
        var response = await Send(HttpMethod.Get, "api/auth/me", null, queue: false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<MeDto>();
    }

    public async Task Flush()
    {
        var raw = await js.InvokeAsync<string?>("cmr.get", "cmr.outbox");
        if (string.IsNullOrWhiteSpace(raw)) return;
        var items = System.Text.Json.JsonSerializer.Deserialize<List<Queued>>(raw) ?? [];
        var left = new List<Queued>();
        foreach (var item in items)
        {
            try
            {
                using var content = new StringContent(item.Body, System.Text.Encoding.UTF8, "application/json");
                var req = new HttpRequestMessage(new HttpMethod(item.Method), item.Url) { Content = content };
                req.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
                var response = await http.SendAsync(req);
                if (!response.IsSuccessStatusCode) left.Add(item);
            }
            catch (HttpRequestException)
            {
                left.Add(item);
            }
        }
        await js.InvokeVoidAsync("cmr.set", "cmr.outbox", System.Text.Json.JsonSerializer.Serialize(left));
        session.Queued = left.Count;
        session.Touch();
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, object? body, bool queue)
    {
        try
        {
            var req = new HttpRequestMessage(method, url);
            req.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
            if (body is not null) req.Content = JsonContent.Create(body);
            var response = await http.SendAsync(req);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && url != "api/auth/login" && url != "api/auth/refresh")
            {
                var refresh = new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh");
                refresh.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
                var refreshed = await http.SendAsync(refresh);
                if (refreshed.IsSuccessStatusCode)
                {
                    var retry = new HttpRequestMessage(method, url);
                    retry.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
                    if (body is not null) retry.Content = JsonContent.Create(body);
                    response = await http.SendAsync(retry);
                }
            }
            return response;
        }
        catch (HttpRequestException) when (queue && body is not null)
        {
            var raw = await js.InvokeAsync<string?>("cmr.get", "cmr.outbox");
            var items = string.IsNullOrWhiteSpace(raw) ? [] : System.Text.Json.JsonSerializer.Deserialize<List<Queued>>(raw) ?? [];
            items.Add(new Queued(method.Method, url, System.Text.Json.JsonSerializer.Serialize(body)));
            await js.InvokeVoidAsync("cmr.set", "cmr.outbox", System.Text.Json.JsonSerializer.Serialize(items));
            session.Queued = items.Count;
            session.Touch();
            throw new QueueSavedException();
        }
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<T>())!;
        var err = await response.Content.ReadFromJsonAsync<Err>();
        throw new ApiException(err?.Message ?? "Request failed", (int)response.StatusCode);
    }

    private sealed record Err(string? Error, string? Message);
    private sealed record LoginOk(string Role, string Name);
    private sealed record Queued(string Method, string Url, string Body);
}

public sealed class ApiException(string message, int status) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class QueueSavedException : Exception
{
    public QueueSavedException() : base("queued") { }
}
