using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Akira.Testing;

/// <summary>
/// Answers HTTP requests from a delegate and records every request it receives.
/// </summary>
public sealed class StubHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    // Read when the request is sent, since the client disposes its content afterwards
    public List<string?> Bodies { get; } = [];

    public static StubHttpHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new((_, _) => Task.FromResult(Response(status, json, "application/json")));

    public static StubHttpHandler Status(HttpStatusCode status) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)));

    public static StubHttpHandler Throws(Exception exception) =>
        new((_, _) => Task.FromException<HttpResponseMessage>(exception));

    public static HttpResponseMessage Response(HttpStatusCode status, string body, string contentType) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, contentType) };

    public static HttpResponseMessage Image(byte[] content, string contentType) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content) { Headers = { ContentType = new MediaTypeHeaderValue(contentType) } }
        };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        // Some callers send side by side, such as outgoing webhooks
        lock (Requests)
        {
            Requests.Add(request);
            Bodies.Add(body);
        }

        return await respond(request, cancellationToken);
    }
}
