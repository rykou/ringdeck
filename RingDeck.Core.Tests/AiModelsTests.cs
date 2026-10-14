using System.Net;
using System.Text;

namespace RingDeck.Core.Tests;

public class AiModelsTests
{
    static string Reveal(string s) => s.StartsWith("dpapi:") ? s[6..] : s;
    static string? Header(HttpCall call, string name) => call.Headers.LastOrDefault(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    [Theory]
    [InlineData(AiFormat.OpenAi, "https://api.openai.com/v1/chat/completions", "https://api.openai.com/v1/models")]
    [InlineData(AiFormat.OpenAi, "http://localhost:11434/v1/chat/completions", "http://localhost:11434/v1/models")]
    [InlineData(AiFormat.OpenAi, "https://openrouter.ai/api/v1/chat/completions?x=1", "https://openrouter.ai/api/v1/models")]
    [InlineData(AiFormat.OpenAi, "https://host/v1/", "https://host/v1/models")]
    [InlineData(AiFormat.Anthropic, "https://api.anthropic.com/v1/messages", "https://api.anthropic.com/v1/models?limit=1000")]
    [InlineData(AiFormat.Anthropic, "https://proxy.corp/anthropic/v1/messages", "https://proxy.corp/anthropic/v1/models?limit=1000")]
    [InlineData(AiFormat.Gemini, "https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse", "https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000")]
    public void ListUrlComesFromTheChatUrl(AiFormat format, string endpoint, string expected) =>
        Assert.Equal(expected, AiModels.ListCall(new AiConnection { Format = format, Endpoint = endpoint }, Reveal)!.Url);

    [Fact]
    public void ListCallUsesTheSameAuthAndHeaders()
    {
        var call = AiModels.ListCall(new AiConnection
        {
            Format = AiFormat.Anthropic, Endpoint = "https://api.anthropic.com/v1/messages", ApiKey = "dpapi:K",
            Headers = { new() { Name = "X-Org", Value = "o" } },
        }, Reveal)!;
        Assert.Equal("K", Header(call, "x-api-key"));
        Assert.Equal("2023-06-01", Header(call, "anthropic-version"));
        Assert.Equal("o", Header(call, "X-Org"));
        Assert.Equal("Bearer K", Header(AiModels.ListCall(new AiConnection { Endpoint = "https://h/v1/chat/completions", ApiKey = "K" }, Reveal)!, "Authorization"));
    }

    [Fact]
    public void CustomFormatCannotList() => Assert.Null(AiModels.ListCall(new AiConnection { Format = AiFormat.Custom, Endpoint = "https://x" }, Reveal));

    [Fact]
    public void ParsesEachFormat()
    {
        Check(AiModels.Parse(AiFormat.OpenAi, """{"object":"list","data":[{"id":"b-model"},{"id":"a-model"},{"id":"a-model"}]}"""), null, "a-model", "b-model");
        Check(AiModels.Parse(AiFormat.Anthropic, """{"data":[{"id":"claude-new","display_name":"New"},{"id":"claude-old"}],"has_more":true,"last_id":"claude-old"}"""), "claude-old", "claude-new", "claude-old");
        Check(AiModels.Parse(AiFormat.Gemini, """{"models":[{"name":"models/gemini-pro","supportedGenerationMethods":["generateContent"]},{"name":"models/embed","supportedGenerationMethods":["embedContent"]}],"nextPageToken":"tok"}"""), "tok", "gemini-pro");
        Check(AiModels.Parse(AiFormat.OpenAi, "not json"), null);
    }

    static void Check((List<string> Ids, string? Next) result, string? next, params string[] ids)
    {
        Assert.Equal(ids, result.Ids);
        Assert.Equal(next, result.Next);
    }

    sealed class Pages(params (HttpStatusCode Status, string Body)[] pages) : HttpMessageHandler
    {
        public readonly List<string> Urls = new();
        int _i;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            Assert.Equal(HttpMethod.Get, request.Method);
            var (status, body) = pages[Math.Min(_i++, pages.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task ClientFollowsPages()
    {
        var handler = new Pages(
            (HttpStatusCode.OK, """{"data":[{"id":"m1"}],"has_more":true,"last_id":"m1"}"""),
            (HttpStatusCode.OK, """{"data":[{"id":"m2"}],"has_more":false,"last_id":"m2"}"""));
        var ids = await new AiClient(handler).ListModelsAsync(new AiConnection { Format = AiFormat.Anthropic, Endpoint = "https://api.anthropic.com/v1/messages" }, Reveal, default);
        Assert.Equal(new[] { "m1", "m2" }, ids);
        Assert.Equal("https://api.anthropic.com/v1/models?limit=1000&after_id=m1", handler.Urls[1]);
    }

    [Fact]
    public async Task GeminiPagesUseTheToken()
    {
        var handler = new Pages(
            (HttpStatusCode.OK, """{"models":[{"name":"models/a","supportedGenerationMethods":["generateContent"]}],"nextPageToken":"T"}"""),
            (HttpStatusCode.OK, """{"models":[{"name":"models/b","supportedGenerationMethods":["generateContent"]}]}"""));
        var ids = await new AiClient(handler).ListModelsAsync(new AiConnection { Format = AiFormat.Gemini, Endpoint = AiRequests.DefaultEndpoint(AiFormat.Gemini) }, Reveal, default);
        Assert.Equal(new[] { "a", "b" }, ids);
        Assert.EndsWith("pageSize=1000&pageToken=T", handler.Urls[1]);
    }

    [Fact]
    public async Task BadKeyIsARedactedError()
    {
        var handler = new Pages((HttpStatusCode.Unauthorized, "bad key sk-9"));
        var ex = await Assert.ThrowsAsync<AiException>(() => new AiClient(handler).ListModelsAsync(new AiConnection { Endpoint = "https://h/v1/chat/completions", ApiKey = "sk-9" }, Reveal, default));
        Assert.StartsWith("HTTP 401", ex.Message);
        Assert.DoesNotContain("sk-9", ex.Message);
    }

    [Fact]
    public async Task CustomFormatListsNothing() =>
        Assert.Empty(await new AiClient(new Pages((HttpStatusCode.OK, "{}"))).ListModelsAsync(new AiConnection { Format = AiFormat.Custom, Endpoint = "https://x" }, Reveal, default));
}
