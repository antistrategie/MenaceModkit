using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Menace.Modkit.ModManagement.Tests.Helpers;

/// <summary>Canned HTTP responses keyed by exact request URL — lets network code be tested offline.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (string? Text, byte[]? Bytes)> _routes = new();

    public StubHttpHandler Json(string url, string json)
    {
        _routes[url] = (json, null);
        return this;
    }

    public StubHttpHandler Bytes(string url, byte[] bytes)
    {
        _routes[url] = (null, bytes);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        if (!_routes.TryGetValue(url, out var route))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });

        var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
        response.Content = route.Bytes != null
            ? new ByteArrayContent(route.Bytes)
            : new StringContent(route.Text ?? string.Empty);
        return Task.FromResult(response);
    }
}
