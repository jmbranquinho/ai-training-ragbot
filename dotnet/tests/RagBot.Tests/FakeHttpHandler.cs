// Plays the website in tests (the .NET counterpart of httpx.MockTransport in Python).
using System.Net;
using System.Text;

namespace RagBot.Tests;

internal sealed class FakeHttpHandler(Dictionary<string, HttpResponseMessage> routes)
    : HttpMessageHandler
{
    public static HttpResponseMessage Html(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/html"),
        };
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = routes.TryGetValue(request.RequestUri!.ToString(), out var route)
            ? route
            : new HttpResponseMessage(HttpStatusCode.NotFound);
        return Task.FromResult(response);
    }
}
