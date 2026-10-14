namespace RingDeck.Core.Tests;

public class AiResponseTests
{
    [Fact]
    public void OpenAiDeltas()
    {
        Assert.Equal(new SseResult("Hel", null, false), AiResponses.ParseSse(AiFormat.OpenAi, """{"choices":[{"delta":{"content":"Hel"}}]}"""));
        Assert.Equal(new SseResult(null, null, false), AiResponses.ParseSse(AiFormat.OpenAi, """{"choices":[{"delta":{"role":"assistant"}}]}"""));
        Assert.Equal(new SseResult(null, null, true), AiResponses.ParseSse(AiFormat.OpenAi, "[DONE]"));
        Assert.Equal("quota", AiResponses.ParseSse(AiFormat.OpenAi, """{"error":{"message":"quota"}}""").Error);
    }

    [Fact]
    public void AnthropicEvents()
    {
        Assert.Equal("lo", AiResponses.ParseSse(AiFormat.Anthropic, """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"lo"}}""").Text);
        Assert.Equal(new SseResult(null, null, false), AiResponses.ParseSse(AiFormat.Anthropic, """{"type":"message_start","message":{}}"""));
        Assert.True(AiResponses.ParseSse(AiFormat.Anthropic, """{"type":"message_stop"}""").Done);
        Assert.Equal("Overloaded", AiResponses.ParseSse(AiFormat.Anthropic, """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""").Error);
    }

    [Fact]
    public void GeminiJoinsParts() =>
        Assert.Equal("ab", AiResponses.ParseSse(AiFormat.Gemini, """{"candidates":[{"content":{"parts":[{"text":"a"},{"text":"b"}],"role":"model"}}]}""").Text);

    [Fact]
    public void MalformedDataIsIgnored() => Assert.Equal(new SseResult(null, null, false), AiResponses.ParseSse(AiFormat.OpenAi, "{not json"));

    [Theory]
    [InlineData("""{"choices":[{"message":{"content":"yes"}}]}""", "choices[0].message.content", "yes")]
    [InlineData("""{"a":{"b":[1,{"c":"deep"}]}}""", "a.b[1].c", "deep")]
    [InlineData("""{"answer":42}""", "answer", "42")]
    [InlineData("""{"answer":"x"}""", " answer ", "x")]
    public void PathFindsTheAnswer(string json, string path, string expected) => Assert.Equal(expected, AiResponses.Extract(json, path));

    [Theory]
    [InlineData("""{"a":1}""", "b")]
    [InlineData("""{"a":[1]}""", "a[3]")]
    [InlineData("""{"a":{"b":1}}""", "a[0]")]
    [InlineData("not json", "a")]
    [InlineData("""{"a":null}""", "a")]
    public void PathMissReturnsNull(string json, string path) => Assert.Null(AiResponses.Extract(json, path));

    [Fact]
    public void ErrorSummaryIsShortAndHidesTheKey()
    {
        var body = "{\"error\":\"bad key sk-SECRET123\"}" + new string('x', 500);
        var s = AiResponses.ErrorSummary(401, body, "sk-SECRET123");
        Assert.StartsWith("HTTP 401", s);
        Assert.DoesNotContain("sk-SECRET123", s);
        Assert.Contains("•••", s);
        Assert.True(s.Length < 340);
        Assert.Equal("HTTP 500", AiResponses.ErrorSummary(500, "  ", null));
    }

    [Theory]
    [InlineData("Translate", "olá", "Translate\n\n<text>\nolá\n</text>")]
    [InlineData("Translate", "", "Translate")]
    [InlineData("", "Can you do that?", "<text>\nCan you do that?\n</text>")]
    [InlineData("  ", null, "")]
    [InlineData("Fix it", "  line one\n  line two  ", "Fix it\n\n<text>\nline one\n  line two\n</text>")]
    public void ComposeJoinsInstructionAndInput(string instruction, string? input, string expected) => Assert.Equal(expected, AiChat.Compose(instruction, input));
}
