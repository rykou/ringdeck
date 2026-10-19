using System.Text.Json;

namespace RingDeck.Core.Tests;

public class AiImageTests
{
    static readonly ClipImage Png = new([0x89, 0x50, 0x4E, 0x47, 1, 2, 3], "image.png", "image/png");
    static readonly string Base64 = Convert.ToBase64String(Png.Data);
    static readonly ChatMessage[] Asked = { new("user", "Read this", Png), new("assistant", "It says hi."), new("user", "Thanks") };
    static JsonElement Body(HttpCall call) => JsonDocument.Parse(call.Body).RootElement;

    [Fact]
    public void OpenAiSendsThePictureAsAnImagePart()
    {
        var b = Body(AiRequests.Build(new AiConnection { Format = AiFormat.OpenAi, Endpoint = "https://api.example/v1", Model = "m" }, null, Asked, s => s));
        var content = b.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal("Read this", content[0].GetProperty("text").GetString());
        Assert.Equal($"data:image/png;base64,{Base64}", content[1].GetProperty("image_url").GetProperty("url").GetString());
        Assert.Equal("Thanks", b.GetProperty("messages")[2].GetProperty("content").GetString());   // plain messages stay plain
    }

    [Fact]
    public void AnthropicPutsThePictureBeforeTheQuestion()
    {
        var b = Body(AiRequests.Build(new AiConnection { Format = AiFormat.Anthropic, Endpoint = "https://api.anthropic.com/v1", Model = "m" }, null, Asked, s => s));
        var content = b.GetProperty("messages")[0].GetProperty("content");
        var source = content[0].GetProperty("source");
        Assert.Equal(("image", "base64", "image/png", Base64),
            (content[0].GetProperty("type").GetString(), source.GetProperty("type").GetString(), source.GetProperty("media_type").GetString(), source.GetProperty("data").GetString()));
        Assert.Equal("Read this", content[1].GetProperty("text").GetString());
    }

    [Fact]
    public void GeminiSendsInlineData()
    {
        var b = Body(AiRequests.Build(new AiConnection { Format = AiFormat.Gemini, Endpoint = AiRequests.DefaultEndpoint(AiFormat.Gemini), Model = "g" }, null, Asked, s => s));
        var parts = b.GetProperty("contents")[0].GetProperty("parts");
        Assert.Equal(Base64, parts[0].GetProperty("inline_data").GetProperty("data").GetString());
        Assert.Equal("image/png", parts[0].GetProperty("inline_data").GetProperty("mime_type").GetString());
        Assert.Equal("Read this", parts[1].GetProperty("text").GetString());
    }

    [Fact]
    public void CustomTemplatesTakeThePictureThroughPlaceholders()
    {
        var c = new AiConnection { Format = AiFormat.Custom, Endpoint = "https://x", BodyTemplate = """{"q":"{{prompt}}","img":"{{image}}","t":"{{image_type}}"}""" };
        var b = Body(AiRequests.Build(c, null, new[] { new ChatMessage("user", "Read this", Png) }, s => s));
        Assert.Equal((Base64, "image/png"), (b.GetProperty("img").GetString(), b.GetProperty("t").GetString()));
    }

    [Fact]
    public void APictureAloneStillAsksSomething()
    {
        Assert.Equal(AiChat.PictureQuestion, AiChat.Compose("", null, withPicture: true));
        Assert.Equal("Translate it", AiChat.Compose("Translate it", null, withPicture: true));
        Assert.Equal("", AiChat.Compose("", null));
    }
}
