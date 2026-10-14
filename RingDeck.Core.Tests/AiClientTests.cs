using System.Net;
using System.Text;

namespace RingDeck.Core.Tests;

public class AiClientTests
{
    sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Last;
        public string? LastBody;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            ct.ThrowIfCancellationRequested();
            return respond(request);
        }
    }

    static HttpResponseMessage Ok(string body, string type = "text/event-stream") =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, type) };

    static readonly ChatMessage[] Ask = { new("user", "Hi") };
    static string Plain(string s) => s;

    static async Task<string> Collect(IAsyncEnumerable<string> pieces)
    {
        var sb = new StringBuilder();
        await foreach (var p in pieces) sb.Append(p);
        return sb.ToString();
    }

    [Fact]
    public async Task StreamsOpenAiPiecesInOrder()
    {
        var handler = new FakeHandler(_ => Ok("data: {\"choices\":[{\"delta\":{\"content\":\"Hel\"}}]}\n\n: keep-alive\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"lo\"}}]}\n\ndata: [DONE]\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"IGNORED\"}}]}\n\n"));
        var client = new AiClient(handler);
        var c = new AiConnection { Endpoint = "https://api.example/v1/chat/completions", Model = "m", ApiKey = "k", Headers = { new() { Name = "X-A", Value = "1" } } };
        Assert.Equal("Hello", await Collect(client.SendAsync(c, null, Ask, Plain, default)));
        Assert.Equal(HttpMethod.Post, handler.Last!.Method);
        Assert.Equal("Bearer k", handler.Last.Headers.Authorization!.ToString());
        Assert.Equal("1", handler.Last.Headers.GetValues("X-A").Single());
        Assert.Contains("\"model\":\"m\"", handler.LastBody);
        Assert.Equal("application/json", handler.Last.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task AnthropicEventLinesAreSkipped()
    {
        var sse = "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{}}\n\nevent: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"Oi\"}}\n\nevent: message_stop\ndata: {\"type\":\"message_stop\"}\n\n";
        var client = new AiClient(new FakeHandler(_ => Ok(sse)));
        Assert.Equal("Oi", await Collect(client.SendAsync(new AiConnection { Format = AiFormat.Anthropic, Endpoint = "https://a/v1/messages", Model = "m" }, null, Ask, Plain, default)));
    }

    [Fact]
    public async Task CustomFormatReadsTheWholeAnswerFromThePath()
    {
        var client = new AiClient(new FakeHandler(_ => Ok("{\"result\":{\"text\":\"done\"}}", "application/json")));
        var c = new AiConnection { Format = AiFormat.Custom, Endpoint = "https://corp/ai", BodyTemplate = "{\"q\":\"{{prompt}}\"}", ResponsePath = "result.text" };
        Assert.Equal("done", await Collect(client.SendAsync(c, null, Ask, Plain, default)));
    }

    [Fact]
    public async Task CustomPathMissIsAnError()
    {
        var client = new AiClient(new FakeHandler(_ => Ok("{\"other\":1}", "application/json")));
        var c = new AiConnection { Format = AiFormat.Custom, Endpoint = "https://corp/ai", BodyTemplate = "{}", ResponsePath = "result.text" };
        var ex = await Assert.ThrowsAsync<AiException>(() => Collect(client.SendAsync(c, null, Ask, Plain, default)));
        Assert.Contains("result.text", ex.Message);
        Assert.Contains("{\"other\":1}", ex.Message);
    }

    [Fact]
    public async Task ErrorStatusBecomesARedactedException()
    {
        var client = new AiClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("invalid key: sk-1") }));
        var ex = await Assert.ThrowsAsync<AiException>(() => Collect(client.SendAsync(new AiConnection { Endpoint = "https://x", ApiKey = "sk-1" }, null, Ask, Plain, default)));
        Assert.StartsWith("HTTP 401", ex.Message);
        Assert.DoesNotContain("sk-1", ex.Message);
    }

    [Fact]
    public async Task ErrorInsideTheStreamIsAnException()
    {
        var client = new AiClient(new FakeHandler(_ => Ok("data: {\"type\":\"error\",\"error\":{\"message\":\"Overloaded\"}}\n\n")));
        var ex = await Assert.ThrowsAsync<AiException>(() => Collect(client.SendAsync(new AiConnection { Format = AiFormat.Anthropic, Endpoint = "https://a" }, null, Ask, Plain, default)));
        Assert.Equal("Overloaded", ex.Message);
    }

    [Fact]
    public async Task MissingOrBadEndpointIsAClearError()
    {
        var client = new AiClient(new FakeHandler(_ => Ok("")));
        var ex = await Assert.ThrowsAsync<AiException>(() => Collect(client.SendAsync(new AiConnection { Endpoint = "not a url" }, null, Ask, Plain, default)));
        Assert.Contains("address", ex.Message);
    }

    [Fact]
    public async Task CancellationStops()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var client = new AiClient(new FakeHandler(_ => Ok("data: [DONE]\n\n")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Collect(client.SendAsync(new AiConnection { Endpoint = "https://x" }, null, Ask, Plain, cts.Token)));
    }
}
