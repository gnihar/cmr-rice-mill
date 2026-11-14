using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Cmr.Web;

public static class Uploader
{
    public static async Task Send(HttpClient http, IBrowserFile file, string ownerType, Guid ownerId)
    {
        using var stream = file.OpenReadStream(10 * 1024 * 1024);
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(stream), "file", file.Name);
        content.Add(new StringContent(ownerType), "ownerType");
        content.Add(new StringContent(ownerId.ToString()), "ownerId");
        var req = new HttpRequestMessage(HttpMethod.Post, "api/documents") { Content = content };
        req.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        var res = await http.SendAsync(req);
        res.EnsureSuccessStatusCode();
    }
}
