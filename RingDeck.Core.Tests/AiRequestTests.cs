using System.Text.Json;

namespace RingDeck.Core.Tests;

public class AiRequestTests
{
    static readonly ChatMessage[] Two = { new("user", "Hi"), new("assistant", "Hello!"), new("user", "Translate \"olá\"\n{x}") };
    static string Reveal(string stored) => stored.StartsWith("dpapi:") ? "REVEALED-" + stored[6..] : stored;
    static JsonElement Body(HttpCall call) => JsonDocument.Parse(call.Body).RootElement;
    static string? Header(HttpCall call, string name) => call.Headers.LastOrDefault(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    [Fact]
    public void OpenAiCompatibleRequest()
    {
        var c = new AiConnection { Format = AiFormat.OpenAi, Endpoint = "https://api.example/v1/chat/completions", Model = "m", ApiKey = "dpapi:K", Temperature = 0.2, MaxTokens = 99 };
        var call = AiRequests.Build(c, "Be brief", Two, Reveal);
        Assert.Equal("https://api.example/v1/chat/completions", call.Url);
        Assert.True(call.Stream);
        Assert.Equal("Bearer REVEALED-K", Header(call, "Authorization"));
        var b = Body(call);
        Assert.Equal("m", b.GetProperty("model").GetString());
        Assert.True(b.GetProperty("stream").GetBoolean());
        Assert.Equal(0.2, b.GetProperty("temperature").GetDouble());
        Assert.Equal(99, b.GetProperty("max_tokens").GetInt32());
        var messages = b.GetProperty("messages");
        Assert.Equal(4, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("Be brief", messages[0].GetProperty("content").GetString());
        Assert.Equal("Translate \"olá\"\n{x}", messages[3].GetProperty("content").GetString());
    }

    [Fact]
    public void OpenAiWithoutKeyOrOptionsSendsNoAuthAndNoNulls()
    {
        var call = AiRequests.Build(new AiConnection { Endpoint = "http://localhost:11434/v1/chat/completions", Model = "llama" }, "", Two, Reveal);
        Assert.Null(Header(call, "Authorization"));
        var b = Body(call);
        Assert.False(b.TryGetProperty("temperature", out _));
        Assert.False(b.TryGetProperty("max_tokens", out _));
        Assert.Equal("user", b.GetProperty("messages")[0].GetProperty("role").GetString());   // empty system prompt is left out
    }

    [Fact]
    public void AnthropicRequest()
    {
        var c = new AiConnection { Format = AiFormat.Anthropic, Endpoint = "https://api.anthropic.com/v1/messages", Model = "claude-x", ApiKey = "dpapi:A" };
        var call = AiRequests.Build(c, "Sys", Two, Reveal);
        Assert.Equal("REVEALED-A", Header(call, "x-api-key"));
        Assert.Equal("2023-06-01", Header(call, "anthropic-version"));
        Assert.Null(Header(call, "Authorization"));
        var b = Body(call);
        Assert.Equal(1024, b.GetProperty("max_tokens").GetInt32());
        Assert.Equal("Sys", b.GetProperty("system").GetString());
        Assert.Equal(3, b.GetProperty("messages").GetArrayLength());
        Assert.True(b.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public void GeminiRequest()
    {
        var c = new AiConnection { Format = AiFormat.Gemini, Endpoint = AiRequests.DefaultEndpoint(AiFormat.Gemini), Model = "gem", ApiKey = "dpapi:G", Temperature = 1, MaxTokens = 50 };
        var call = AiRequests.Build(c, "Sys", Two, Reveal);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gem:streamGenerateContent?alt=sse", call.Url);
        Assert.Equal("REVEALED-G", Header(call, "x-goog-api-key"));
        var b = Body(call);
        var contents = b.GetProperty("contents");
        Assert.Equal("model", contents[1].GetProperty("role").GetString());
        Assert.Equal("Hi", contents[0].GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal("Sys", b.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal(50, b.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32());
    }

    [Fact]
    public void UserHeadersComeLastReplaceSameNameAndCanUseTheKey()
    {
        var c = new AiConnection
        {
            Endpoint = "https://x/{{api_key}}/v1/chat/completions", Model = "m", ApiKey = "dpapi:K",
            Headers = { new() { Name = "authorization", Value = "Token {{api_key}}" }, new() { Name = "X-Org", Value = "dpapi:ORG", Secret = true }, new() { Name = " ", Value = "ignored" } },
        };
        var call = AiRequests.Build(c, null, Two, Reveal);
        Assert.Equal("https://x/REVEALED-K/v1/chat/completions", call.Url);
        Assert.Single(call.Headers, h => h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Token REVEALED-K", Header(call, "Authorization"));
        Assert.Equal("REVEALED-ORG", Header(call, "X-Org"));
        Assert.DoesNotContain(call.Headers, h => string.IsNullOrWhiteSpace(h.Key));
    }

    [Fact]
    public void CustomTemplateFillsEveryPlaceholder()
    {
        var c = new AiConnection
        {
            Format = AiFormat.Custom, Endpoint = "https://corp/ai", Model = "big \"one\"", ApiKey = "dpapi:K",
            BodyTemplate = """{"m":"{{model}}","q":"{{prompt}}","s":"{{system}}","k":"{{api_key}}","history":{{messages}}}""",
        };
        var call = AiRequests.Build(c, "Line1\nLine2", Two, Reveal);
        Assert.False(call.Stream);
        Assert.Null(Header(call, "Authorization"));
        var b = Body(call);
        Assert.Equal("big \"one\"", b.GetProperty("m").GetString());
        Assert.Equal("Translate \"olá\"\n{x}", b.GetProperty("q").GetString());
        Assert.Equal("Line1\nLine2", b.GetProperty("s").GetString());
        Assert.Equal("REVEALED-K", b.GetProperty("k").GetString());
        Assert.Equal(3, b.GetProperty("history").GetArrayLength());
        Assert.Equal("assistant", b.GetProperty("history")[1].GetProperty("role").GetString());
    }

    [Theory]
    [InlineData(AiFormat.OpenAi, "https://gateway.example.com/openai/v1", "https://gateway.example.com/openai/v1/chat/completions")]
    [InlineData(AiFormat.OpenAi, "https://gateway.example.com/openai/v1/", "https://gateway.example.com/openai/v1/chat/completions")]
    [InlineData(AiFormat.OpenAi, "http://localhost:11434/v1", "http://localhost:11434/v1/chat/completions")]
    [InlineData(AiFormat.OpenAi, "https://api.openai.com/v1/chat/completions", "https://api.openai.com/v1/chat/completions")]
    [InlineData(AiFormat.OpenAi, "https://x.openai.azure.com/openai/deployments/d/chat/completions?api-version=2024-10-21", "https://x.openai.azure.com/openai/deployments/d/chat/completions?api-version=2024-10-21")]
    [InlineData(AiFormat.OpenAi, "https://gw.corp/v1?team=a", "https://gw.corp/v1/chat/completions?team=a")]
    [InlineData(AiFormat.Anthropic, "https://api.anthropic.com/v1", "https://api.anthropic.com/v1/messages")]
    [InlineData(AiFormat.Anthropic, "https://api.anthropic.com/v1/messages", "https://api.anthropic.com/v1/messages")]
    [InlineData(AiFormat.Custom, "https://corp/ai/v1", "https://corp/ai/v1")]
    public void BaseUrlsGetTheChatPathAdded(AiFormat format, string endpoint, string expected) =>
        Assert.Equal(expected, AiRequests.Build(new AiConnection { Format = format, Endpoint = endpoint, Model = "m", BodyTemplate = "{}" }, null, Two, Reveal).Url);

    [Theory]
    [InlineData(AiFormat.OpenAi, "https://api.openai.com/v1/chat/completions")]
    [InlineData(AiFormat.Anthropic, "https://api.anthropic.com/v1/messages")]
    [InlineData(AiFormat.Custom, "")]
    public void DefaultEndpoints(AiFormat format, string url) => Assert.Equal(url, AiRequests.DefaultEndpoint(format));

    [Theory]
    [InlineData("http://localhost:11434/v1/chat/completions", false)]
    [InlineData("http://127.0.0.1:1234/x", false)]
    [InlineData("http://[::1]:8080/x", false)]
    [InlineData("https://api.example/x", false)]
    [InlineData("http://10.0.0.5/x", true)]
    [InlineData("http://api.example/x", true)]
    public void PlainHttpWarningOnlyForRemoteHosts(string url, bool warn) => Assert.Equal(warn, AiRequests.IsInsecure(url));
}
