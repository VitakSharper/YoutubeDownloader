using System.Net;

namespace YoutubeDownloader.Core.Tests;

/// <summary>Returns a canned response computed from the request.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_responder(request));

    public static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(new StubHttpMessageHandler(responder));

    public static HttpResponseMessage Json(HttpStatusCode code, string body)
        => new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}

/// <summary>Always faults the send (simulates a network failure).</summary>
internal sealed class ThrowingHttpMessageHandler : HttpMessageHandler
{
    private readonly Exception _ex;

    public ThrowingHttpMessageHandler(Exception ex) => _ex = ex;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromException<HttpResponseMessage>(_ex);
}
