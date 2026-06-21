using System.Net;
using System.Text;

namespace ReadLog.Tests.Infrastructure;

/// <summary>
/// A test <see cref="HttpMessageHandler"/> that returns a canned response and records
/// the requests it received (so tests can assert URLs and whether a call was made).
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public List<HttpRequestMessage> Requests { get; } = [];

    public int CallCount => Requests.Count;

    public static StubHttpMessageHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    public static StubHttpMessageHandler Status(HttpStatusCode status) =>
        new(_ => new HttpResponseMessage(status));

    public HttpClient CreateClient(string baseAddress) =>
        new(this) { BaseAddress = new Uri(baseAddress) };

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_responder(request));
    }
}
