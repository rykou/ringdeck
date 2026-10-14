using System.Net;
using System.Text;

namespace RingDeck.Core.Tests;

public class AiQuickTests
{
    sealed class Fake(string sse) : HttpMessageHandler
    {
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") };
        }
    }

    static readonly AiConnection Conn = new() { Endpoint = "https://h/v1", Model = "m" };

    [Fact]
    public void ShowChatIsOnByDefaultAndRoundTrips()
    {
        Assert.True(new AiChatAction().ShowChat);
        var config = new AppConfig { Rings = { new Ring { Slices = { new Slice { Action = new AiChatAction { ShowChat = false } } } } } };
        Assert.False(((AiChatAction)ConfigStore.FromJson(ConfigStore.ToJson(config))!.Rings[0].Slices[0].Action!).ShowChat);
        Assert.True(((AiChatAction)ConfigStore.FromJson("""{"rings":[{"slices":[{"action":{"type":"ai"}}]}]}""")!.Rings[0].Slices[0].Action!).ShowChat);
    }

    [Fact]
    public async Task QuickAskReturnsTheWholeTrimmedReply()
    {
        var fake = new Fake("data: {\"choices\":[{\"delta\":{\"content\":\"  Could you \"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"do that, please?\\n\"}}]}\n\ndata: [DONE]\n\n");
        var reply = await AiChat.AskOnceAsync(new AiClient(fake), Conn, new AiChatAction { Instruction = "Rewrite", SystemPrompt = "Only the text" }, "Can you do that?", s => s, default);
        Assert.Equal("Could you do that, please?", reply);
        Assert.Contains("\"role\":\"system\",\"content\":\"Only the text\"", fake.Body);
        Assert.Contains("Rewrite\\n\\n<text>\\nCan you do that?\\n</text>", fake.Body);
    }

    [Fact]
    public async Task QuickAskWithNothingToSendIsAnError()
    {
        var ex = await Assert.ThrowsAsync<AiException>(() => AiChat.AskOnceAsync(new AiClient(new Fake("")), Conn, new AiChatAction(), null, s => s, default));
        Assert.Contains("nothing", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QuickAskWithAnEmptyAnswerIsAnError() =>
        await Assert.ThrowsAsync<AiException>(() => AiChat.AskOnceAsync(new AiClient(new Fake("data: [DONE]\n\n")), Conn, new AiChatAction { Instruction = "Hi" }, null, s => s, default));
}
