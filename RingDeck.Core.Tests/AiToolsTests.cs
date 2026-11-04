using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RingDeck.Core.Tests;

public class AiToolsTests
{
    static string Plain(string s) => s;
    static readonly AiTool[] Tools = { new("click", "Click", new JsonObject { ["type"] = "object" }) };

    /// <summary>A user turn, the AI's click, and its result: the shape every agent conversation has.</summary>
    static List<AgentMessage> Conversation(JsonNode? raw = null, string id = "c1") => new()
    {
        AgentMessage.User("1. Open Edge"),
        new AgentMessage { Role = AgentRole.Assistant, Calls = new[] { new ToolCall(id, "click", new JsonObject { ["ref"] = "@a:e1" }) }, Raw = raw },
        AgentMessage.ToolResults(new[] { new ToolResult(id, "click", "clicked", IsError: true) }),
    };

    static JsonElement Body(HttpCall call) => JsonDocument.Parse(call.Body).RootElement;

    [Fact]
    public void OpenAiSendsToolsCallsAndResults()
    {
        var c = new AiConnection { Format = AiFormat.OpenAi, Endpoint = "https://api.openai.com/v1", Model = "gpt", ApiKey = "k" };
        var call = AiTools.Build(c, "rules", Conversation(), Tools, Plain);
        Assert.Equal("https://api.openai.com/v1/chat/completions", call.Url);
        Assert.False(call.Stream);
        var body = Body(call);
        Assert.False(body.TryGetProperty("stream", out _));
        Assert.Equal("click", body.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
        var messages = body.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        var calls = messages[2].GetProperty("tool_calls");
        Assert.Equal("c1", calls[0].GetProperty("id").GetString());
        Assert.Equal("{\"ref\":\"@a:e1\"}", calls[0].GetProperty("function").GetProperty("arguments").GetString());   // a JSON string, as OpenAI wants
        Assert.Equal("tool", messages[3].GetProperty("role").GetString());
        Assert.Equal("c1", messages[3].GetProperty("tool_call_id").GetString());
        Assert.Equal("Error: clicked", messages[3].GetProperty("content").GetString());
    }

    [Fact]
    public void AnthropicSendsTheRawTurnBackAndResultsAsUserContent()
    {
        var raw = JsonNode.Parse("[{\"type\":\"thinking\",\"thinking\":\"hm\",\"signature\":\"sig\"},{\"type\":\"tool_use\",\"id\":\"c1\",\"name\":\"click\",\"input\":{\"ref\":\"@a:e1\"}}]");
        var c = new AiConnection { Format = AiFormat.Anthropic, Endpoint = "https://api.anthropic.com/v1", Model = "claude" };
        var body = Body(AiTools.Build(c, "rules", Conversation(raw), Tools, Plain));
        Assert.Equal(AiTools.DefaultMaxTokens, body.GetProperty("max_tokens").GetInt32());
        Assert.Equal("rules", body.GetProperty("system").GetString());
        Assert.Equal("click", body.GetProperty("tools")[0].GetProperty("name").GetString());
        Assert.True(body.GetProperty("tools")[0].TryGetProperty("input_schema", out _));
        var messages = body.GetProperty("messages");
        Assert.Equal("sig", messages[1].GetProperty("content")[0].GetProperty("signature").GetString());
        var result = messages[2].GetProperty("content")[0];
        Assert.Equal("user", messages[2].GetProperty("role").GetString());
        Assert.Equal("tool_result", result.GetProperty("type").GetString());
        Assert.Equal("c1", result.GetProperty("tool_use_id").GetString());
        Assert.True(result.GetProperty("is_error").GetBoolean());
    }

    [Fact]
    public void GeminiAsksForAWholeAnswerAndNamesEachResult()
    {
        var raw = JsonNode.Parse("{\"role\":\"model\",\"parts\":[{\"functionCall\":{\"name\":\"click\",\"args\":{}},\"thoughtSignature\":\"ts\"}]}");
        var c = new AiConnection { Format = AiFormat.Gemini, Endpoint = AiRequests.DefaultEndpoint(AiFormat.Gemini), Model = "gemini-pro" };
        var call = AiTools.Build(c, "rules", Conversation(raw, "ringdeck-0"), Tools, Plain);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-pro:generateContent", call.Url);
        var body = Body(call);
        Assert.Equal("ts", body.GetProperty("contents")[1].GetProperty("parts")[0].GetProperty("thoughtSignature").GetString());
        var response = body.GetProperty("contents")[2].GetProperty("parts")[0].GetProperty("functionResponse");
        Assert.Equal("click", response.GetProperty("name").GetString());
        Assert.False(response.TryGetProperty("id", out _));                         // a made-up id isn't sent
        Assert.Equal("clicked", response.GetProperty("response").GetProperty("error").GetString());
        Assert.Equal("click", body.GetProperty("tools")[0].GetProperty("functionDeclarations")[0].GetProperty("name").GetString());
        Assert.Equal("rules", body.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("https://x/models/m:streamGenerateContent?alt=sse", "https://x/models/m:generateContent")]
    [InlineData("https://x/models/m:streamGenerateContent?alt=sse&key=1", "https://x/models/m:generateContent?key=1")]
    [InlineData("https://x/models/m:generateContent", "https://x/models/m:generateContent")]
    public void GeminiStreamingAddressBecomesTheWholeOne(string url, string expected) => Assert.Equal(expected, AiTools.NotStreamed(url));

    [Fact]
    public void CustomConnectionsCantUseTools()
    {
        var ex = Assert.Throws<AiException>(() => AiTools.Build(new AiConnection { Format = AiFormat.Custom }, "", Conversation(), Tools, Plain));
        Assert.Contains("custom format", ex.Message);
    }

    [Fact]
    public void ReadsOpenAiToolCalls()
    {
        var reply = AiTools.Parse(AiFormat.OpenAi, "{\"choices\":[{\"finish_reason\":\"tool_calls\",\"message\":{\"content\":null,\"tool_calls\":[{\"id\":\"a\",\"type\":\"function\",\"function\":{\"name\":\"click\",\"arguments\":\"{\\\"ref\\\":\\\"@x:e2\\\"}\"}}]}}]}");
        Assert.Equal("", reply.Text);
        Assert.Equal("a", reply.Calls[0].Id);
        Assert.Equal("@x:e2", reply.Calls[0].Arguments["ref"]!.GetValue<string>());
        Assert.False(reply.CutOff);
    }

    [Fact]
    public void BrokenArgumentsBecomeAnEmptyObject()
    {
        var reply = AiTools.Parse(AiFormat.OpenAi, "{\"choices\":[{\"message\":{\"tool_calls\":[{\"id\":\"a\",\"function\":{\"name\":\"click\",\"arguments\":\"{oops\"}}]}}]}");
        Assert.Empty(reply.Calls[0].Arguments);
    }

    [Fact]
    public void ReadsAnthropicTextAndToolUse()
    {
        var reply = AiTools.Parse(AiFormat.Anthropic, "{\"stop_reason\":\"max_tokens\",\"content\":[{\"type\":\"text\",\"text\":\"Looking\"},{\"type\":\"tool_use\",\"id\":\"t\",\"name\":\"find\",\"input\":{\"text\":\"Inbox\"}}]}");
        Assert.Equal("Looking", reply.Text);
        Assert.Equal(("t", "find"), (reply.Calls[0].Id, reply.Calls[0].Name));
        Assert.True(reply.CutOff);
        Assert.Equal(2, reply.Raw!.AsArray().Count);
    }

    [Fact]
    public void ReadsGeminiCallsSkippingThoughtsAndGivesThemIds()
    {
        var reply = AiTools.Parse(AiFormat.Gemini, "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"thinking\",\"thought\":true},{\"text\":\"Hi\"},{\"functionCall\":{\"name\":\"launch\",\"args\":{\"app\":\"msedge\"}}},{\"functionCall\":{\"id\":\"g\",\"name\":\"click\",\"args\":{}}}]}}]}");
        Assert.Equal("Hi", reply.Text);
        Assert.Equal("ringdeck-0", reply.Calls[0].Id);
        Assert.Equal("g", reply.Calls[1].Id);
        Assert.Equal("model", reply.Raw!["role"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(AiFormat.OpenAi, "{\"error\":{\"message\":\"bad key\"}}", "bad key")]
    [InlineData(AiFormat.Gemini, "{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}", "SAFETY")]
    [InlineData(AiFormat.Anthropic, "not json", "isn't JSON")]
    [InlineData(AiFormat.OpenAi, "{\"choices\":[]}", "no choices")]
    public void BadAnswersSayWhat(AiFormat format, string json, string expected) =>
        Assert.Contains(expected, Assert.Throws<AiException>(() => AiTools.Parse(format, json)).Message);

    sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Sent;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Sent = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task ClientSendsAndReadsATurn()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{\"content\":[{\"type\":\"tool_use\",\"id\":\"t\",\"name\":\"click\",\"input\":{}}]}");
        var c = new AiConnection { Format = AiFormat.Anthropic, Endpoint = "https://a/v1/messages", Model = "m" };
        var reply = await new AiClient(handler).SendWithToolsAsync(c, "rules", Conversation(), Tools, Plain, default);
        Assert.Equal("click", reply.Calls.Single().Name);
        Assert.Contains("\"tools\"", handler.Sent);
    }

    [Fact]
    public async Task ClientHidesTheKeyInErrors()
    {
        var handler = new FakeHandler(HttpStatusCode.Unauthorized, "{\"error\":\"key sk-secret is wrong\"}");
        var c = new AiConnection { Format = AiFormat.OpenAi, Endpoint = "https://a/v1", Model = "m", ApiKey = "sk-secret" };
        var ex = await Assert.ThrowsAsync<AiException>(() => new AiClient(handler).SendWithToolsAsync(c, "rules", Conversation(), Tools, Plain, default));
        Assert.StartsWith("HTTP 401", ex.Message);
        Assert.DoesNotContain("sk-secret", ex.Message);
    }
}
