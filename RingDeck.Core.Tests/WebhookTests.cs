using System.Net;
using System.Text.Json;

namespace RingDeck.Core.Tests;

public class WebhookTests
{
    sealed class Capture : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string Body = "";
        public byte[] Bytes = [];
        public HttpStatusCode Status = HttpStatusCode.NoContent;
        public string Reply = "";
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Request = request;
            if (request.Content is not null) { Bytes = await request.Content.ReadAsByteArrayAsync(ct); Body = System.Text.Encoding.UTF8.GetString(Bytes); }
            return new HttpResponseMessage(Status) { Content = new StringContent(Reply) };
        }
    }

    sealed class Desk : IInputSender, IClipboard, ILauncher, INotifier
    {
        public string? Clip = "copied text";
        public ClipImage? Image;
        public List<string> Notes = new();
        public List<string> Order = new();
        public bool CanSendToForeground() => true;
        public void ReleaseModifiers() { }
        public void RestoreModifiers() { }
        public void SendCombo(Shortcut combo) { }
        public void SendText(string text) { }
        public string? GetText() => Clip;
        public bool TrySetText(string text) { Clip = text; return true; }
        public object? Capture() => Clip;
        public void Restore(object? snapshot) { }
        public ClipImage? GetImage() { Order.Add("image"); return Image; }
        public void Launch(string target, string arguments, string workingFolder) { }
        public void Notify(string title, string message) => Notes.Add(title + ": " + message);
    }

    static readonly ClipImage Png = new([0x89, 0x50, 0x4E, 0x47, 1, 2, 3], "image.png", "image/png");
    const string Hook = "https://discord.com/api/webhooks/123/SECRETTOKEN123";

    static (ActionRunner Runner, Capture Http, Desk Desk) Make(string? selection = "selected words", Func<ClipImage, int, ClipImage?>? shrink = null)
    {
        var http = new Capture();
        var desk = new Desk();
        var runner = new ActionRunner(desk, desk, desk, desk, () => new DateTime(2026, 10, 14, 9, 30, 0), (_, _) => Task.CompletedTask,
            webhooks: new WebhookClient(http), reveal: s => s.StartsWith("enc:") ? s[4..] : s,
            selection: _ => { desk.Order.Add("selection"); return Task.FromResult(selection); }, shrink: shrink);
        return (runner, http, desk);
    }

    static Task Run(ActionRunner runner, WebRequest r) => runner.RunAsync(new Slice { Label = "Send", Action = new WebhookAction { Request = r } }, default);

    static JsonElement Json(Capture http) => JsonDocument.Parse(http.Body).RootElement;

    /// <summary>The names of the parts of a multipart body.</summary>
    static HashSet<string> Parts(Capture http) =>
        System.Text.RegularExpressions.Regex.Matches(http.Body, @"form-data; name=""?([^"";\r\n]+)""?").Select(m => m.Groups[1].Value).ToHashSet();

    [Fact]
    public async Task DiscordPostsTheSelectionAsJson()
    {
        var (runner, http, desk) = Make();
        await Run(runner, new WebRequest { Service = WebService.Discord, Url = "enc:" + Hook, Message = "{selection} ({date:yyyy-MM-dd})", Title = "RingDeck" });
        Assert.Equal(HttpMethod.Post, http.Request!.Method);
        Assert.Equal(Hook, http.Request.RequestUri!.ToString());
        Assert.Equal("selected words (2026-10-14)", Json(http).GetProperty("content").GetString());
        Assert.Equal("RingDeck", Json(http).GetProperty("username").GetString());
        Assert.Equal("\"Send\" sent: To Discord.", Assert.Single(desk.Notes));
    }

    [Fact]
    public async Task DiscordWithAnImageUploadsItAsAFile()
    {
        var (runner, http, desk) = Make();
        desk.Image = Png;
        await Run(runner, new WebRequest { Service = WebService.Discord, Url = Hook, Message = "look", SendImage = true });
        Assert.Equal("multipart/form-data", http.Request!.Content!.Headers.ContentType!.MediaType);
        Assert.Equal(new HashSet<string> { "payload_json", "files[0]" }, Parts(http));
        Assert.Contains("{\"content\":\"look\"}", http.Body);
        Assert.Contains("image.png", http.Body);
        Assert.Contains("image/png", http.Body);
        // the picture is read before the selection, because reading the selection goes through the clipboard
        Assert.Equal(new[] { "image" }, desk.Order);
    }

    [Fact]
    public async Task TheImageIsReadBeforeTheSelection()
    {
        var (runner, _, desk) = Make();
        desk.Image = Png;
        await Run(runner, new WebRequest { Service = WebService.Telegram, Token = "1:abc", Target = "42", SendImage = true, Message = "{selection}" });
        Assert.Equal(new[] { "image", "selection" }, desk.Order);
    }

    [Fact]
    public async Task NoImageOnTheClipboardSendsJustTheText()
    {
        var (runner, http, _) = Make();
        await Run(runner, new WebRequest { Service = WebService.Discord, Url = Hook, Message = "hi", SendImage = true });
        Assert.Equal("application/json", http.Request!.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task ServicesWithoutUploadsNeverReadTheImage()
    {
        var (runner, http, desk) = Make();
        desk.Image = Png;
        await Run(runner, new WebRequest { Service = WebService.Slack, Url = "https://hooks.slack.com/services/T/B/X", Message = "hi", SendImage = true });
        Assert.Empty(desk.Order);
        Assert.Equal("hi", Json(http).GetProperty("text").GetString());
    }

    [Fact]
    public async Task NothingToSendIsReported()
    {
        var (runner, http, desk) = Make(selection: null);
        await Run(runner, new WebRequest { Service = WebService.Discord, Url = Hook, Message = "{selection}" });
        Assert.Equal(0, http.Calls);
        Assert.Contains("nothing to send", Assert.Single(desk.Notes));
    }

    [Fact]
    public async Task SlackAndGoogleChatPutTheTitleInBold()
    {
        var (runner, http, _) = Make();
        await Run(runner, new WebRequest { Service = WebService.GoogleChat, Url = "https://chat.googleapis.com/v1/spaces/x/messages?key=k", Message = "body", Title = "Head" });
        Assert.Equal("*Head*\nbody", Json(http).GetProperty("text").GetString());
    }

    [Fact]
    public async Task TeamsSendsAnAdaptiveCard()
    {
        var (runner, http, _) = Make();
        await Run(runner, new WebRequest { Service = WebService.Teams, Url = "https://prod.westus.logic.azure.com/workflows/abc", Message = "body", Title = "Head" });
        var attachment = Json(http).GetProperty("attachments")[0];
        Assert.Equal("application/vnd.microsoft.card.adaptive", attachment.GetProperty("contentType").GetString());
        var blocks = attachment.GetProperty("content").GetProperty("body");
        Assert.Equal("Head", blocks[0].GetProperty("text").GetString());
        Assert.Equal("body", blocks[1].GetProperty("text").GetString());
    }

    [Fact]
    public async Task TeamsShrinksThePictureIntoTheCard()
    {
        var budgets = new List<int>();
        var small = new ClipImage([1, 2, 3, 4], "image.jpg", "image/jpeg");
        var (runner, http, desk) = Make(shrink: (_, budget) => { budgets.Add(budget); return small; });
        desk.Image = new ClipImage(new byte[200_000], "image.png", "image/png");
        await Run(runner, new WebRequest { Service = WebService.Teams, Url = "https://prod.westus.logic.azure.com/workflows/abc", Message = "see this", SendImage = true });
        var blocks = Json(http).GetProperty("attachments")[0].GetProperty("content").GetProperty("body");
        Assert.Equal("Image", blocks[1].GetProperty("type").GetString());
        Assert.Equal("data:image/jpeg;base64,AQIDBA==", blocks[1].GetProperty("url").GetString());
        Assert.Equal(Webhooks.TeamsImageBudget("see this", ""), Assert.Single(budgets));
        Assert.True(http.Body.Length < Webhooks.TeamsMessageLimit);
    }

    [Fact]
    public async Task TeamsSaysSoWhenThePictureCannotFit()
    {
        var (runner, http, desk) = Make(shrink: (_, _) => null);
        desk.Image = new ClipImage(new byte[200_000], "image.png", "image/png");
        await Run(runner, new WebRequest { Service = WebService.Teams, Url = "https://prod.westus.logic.azure.com/workflows/abc", Message = "x", SendImage = true });
        Assert.Equal(0, http.Calls);
        Assert.Contains("too big for a Teams message", Assert.Single(desk.Notes));
    }

    [Fact]
    public void ALongTeamsMessageLeavesLessRoomForThePicture()
    {
        Assert.True(Webhooks.TeamsImageBudget(new string('x', 5000), "") < Webhooks.TeamsImageBudget("short", ""));
        Assert.Equal(0, Webhooks.TeamsImageBudget(new string('x', 20_000), ""));
    }

    [Fact]
    public async Task TelegramSendsTextOrAPhoto()
    {
        var (runner, http, desk) = Make();
        await Run(runner, new WebRequest { Service = WebService.Telegram, Token = "enc:123:ABC", Target = "-100", Message = "hello" });
        Assert.Equal("https://api.telegram.org/bot123:ABC/sendMessage", http.Request!.RequestUri!.ToString());
        Assert.Equal("-100", Json(http).GetProperty("chat_id").GetString());
        Assert.Equal("hello", Json(http).GetProperty("text").GetString());

        desk.Image = Png;
        await Run(runner, new WebRequest { Service = WebService.Telegram, Token = "123:ABC", Target = "-100", Message = "caption", SendImage = true });
        Assert.EndsWith("/sendPhoto", http.Request!.RequestUri!.ToString());
        Assert.Equal(new HashSet<string> { "chat_id", "caption", "photo" }, Parts(http));
    }

    [Fact]
    public async Task TelegramWithoutATokenSaysWhatIsMissing()
    {
        var (runner, http, desk) = Make();
        await Run(runner, new WebRequest { Service = WebService.Telegram, Target = "1", Message = "x" });
        Assert.Equal(0, http.Calls);
        Assert.Contains("bot token", Assert.Single(desk.Notes));
    }

    [Fact]
    public async Task NtfyPostsTextAndPutsImages()
    {
        var (runner, http, desk) = Make();
        await Run(runner, new WebRequest { Service = WebService.Ntfy, Url = "https://ntfy.sh/my-topic", Message = "ação feita", Title = "Done ✓", Token = "tk_1" });
        Assert.Equal(HttpMethod.Post, http.Request!.Method);
        Assert.Equal("ação feita", http.Body);
        Assert.Contains("title=Done%20%E2%9C%93", http.Request.RequestUri!.Query);
        Assert.Equal("Bearer tk_1", http.Request.Headers.Authorization!.ToString());

        desk.Image = Png;
        await Run(runner, new WebRequest { Service = WebService.Ntfy, Url = "https://ntfy.sh/my-topic", Message = "pic", SendImage = true });
        Assert.Equal(HttpMethod.Put, http.Request!.Method);
        Assert.Equal(Png.Data, http.Bytes);
        Assert.Contains("filename=image.png", http.Request.RequestUri!.Query);
        Assert.Contains("message=pic", http.Request.RequestUri.Query);
    }

    [Fact]
    public async Task PushoverSendsAFormWithTheAttachment()
    {
        var (runner, http, desk) = Make();
        desk.Image = Png;
        await Run(runner, new WebRequest { Service = WebService.Pushover, Token = "apptoken", Target = "userkey", Message = "", Title = "T", SendImage = true });
        Assert.Equal("https://api.pushover.net/1/messages.json", http.Request!.RequestUri!.ToString());
        Assert.Equal(new HashSet<string> { "token", "user", "message", "title", "attachment" }, Parts(http));
        foreach (var value in new[] { "apptoken", "userkey", "Image" }) Assert.Contains(value, http.Body);
    }

    [Fact]
    public async Task HomeAssistantAndAutomationsGetJson()
    {
        var (runner, http, _) = Make();
        await Run(runner, new WebRequest { Service = WebService.HomeAssistant, Url = "http://homeassistant.local:8123/api/webhook/lights", Message = "on" });
        Assert.Equal("on", Json(http).GetProperty("message").GetString());

        await Run(runner, new WebRequest { Service = WebService.Automation, Url = "https://hook.eu1.make.com/abc", Message = "m", Title = "t" });
        Assert.Equal("m", Json(http).GetProperty("value1").GetString());
        Assert.Equal("t", Json(http).GetProperty("value2").GetString());
    }

    [Fact]
    public async Task CustomFillsTheBodyWithEscapedValues()
    {
        var (runner, http, _) = Make(selection: "say \"hi\"\nbye");
        await Run(runner, new WebRequest
        {
            Service = WebService.Custom, Method = "put", Url = "https://api.example.com/notes?q={selection}", Message = "{selection}",
            Body = "{\"note\": \"{message}\", \"on\": \"{date:yyyy}\"}", ContentType = "application/json",
            Headers = { new AiHeader { Name = "X-Key", Value = "enc:abc123", Secret = true }, new AiHeader { Name = "X-Plain", Value = "p" } },
        });
        Assert.Equal(HttpMethod.Put, http.Request!.Method);
        Assert.Contains("q=say%20%22hi%22%0Abye", http.Request.RequestUri!.AbsoluteUri);
        Assert.Equal("say \"hi\"\nbye", Json(http).GetProperty("note").GetString());
        Assert.Equal("2026", Json(http).GetProperty("on").GetString());
        Assert.Equal("abc123", http.Request.Headers.GetValues("X-Key").Single());
        Assert.Equal("p", http.Request.Headers.GetValues("X-Plain").Single());
    }

    [Fact]
    public async Task ErrorsNeverShowTheWebhookAddress()
    {
        var (runner, http, desk) = Make();
        http.Status = HttpStatusCode.NotFound;
        http.Reply = "{\"message\": \"Unknown Webhook SECRETTOKEN123\"}";
        await Run(runner, new WebRequest { Service = WebService.Discord, Url = Hook, Message = "x" });
        var note = Assert.Single(desk.Notes);
        Assert.Contains("HTTP 404", note);
        Assert.DoesNotContain("SECRETTOKEN123", note);
    }

    [Fact]
    public async Task AFailingWebhookStepStopsTheWorkflow()
    {
        var (runner, http, desk) = Make();
        http.Status = HttpStatusCode.Unauthorized;
        var macro = new MacroAction { Steps = { new WebhookStep { Request = new WebRequest { Service = WebService.Slack, Url = "https://hooks.slack.com/x", Message = "x" } }, new TextStep { Text = "after" } } };
        await runner.RunAsync(new Slice { Label = "Flow", Action = macro }, default);
        Assert.Contains("Step 1 (send to Slack): HTTP 401", Assert.Single(desk.Notes));
    }

    [Fact]
    public void ExportsLeaveOutAddressesAndTokens()
    {
        var config = new AppConfig
        {
            Rings =
            {
                new Ring
                {
                    Slices =
                    {
                        new Slice { Action = new WebhookAction { Request = new WebRequest { Url = Hook, Token = "t", Target = "u", Message = "keep", Headers = { new AiHeader { Name = "k", Value = "s", Secret = true } } } } },
                        new Slice { Action = new MacroAction { Steps = { new WebhookStep { Request = new WebRequest { Url = Hook } } } } },
                    },
                },
            },
        };
        var json = ConfigStore.ToJson(ConfigTransfer.ExportAll(config));
        Assert.DoesNotContain("SECRETTOKEN123", json);
        var back = ConfigStore.FromJson(json)!;
        var requests = ConfigStore.WebRequests(back).ToList();
        Assert.Equal(2, requests.Count);
        Assert.All(requests, r => Assert.Equal("", r.Url));
        Assert.Equal("keep", requests[0].Message);
        Assert.Equal("", requests[0].Token);
        Assert.Equal("", requests[0].Headers[0].Value);
    }

    [Fact]
    public void WebhooksSurviveSavingAndBrokenFilesAreRepaired()
    {
        var json = ConfigStore.ToJson(new AppConfig { Rings = { new Ring { Slices = { new Slice { Action = new WebhookAction { Request = new WebRequest { Service = WebService.Ntfy, SendImage = true } } } } } } });
        Assert.Contains("\"type\": \"webhook\"", json);
        var back = (WebhookAction)ConfigStore.FromJson(json)!.Rings[0].Slices[0].Action!;
        Assert.Equal(WebService.Ntfy, back.Request.Service);
        Assert.True(back.Request.SendImage);

        var broken = ConfigStore.FromJson("""{ "rings": [ { "slices": [ { "action": { "type": "webhook", "request": null } }, { "action": { "type": "macro", "steps": [ { "type": "webhook" } ] } } ] } ] }""")!;
        Assert.All(ConfigStore.WebRequests(broken), r => Assert.NotNull(r.Headers));
    }

    [Fact]
    public void PlaceholdersInsideJsonStillWork()
    {
        Assert.Equal("{\"a\": \"2026\"}", Placeholders.Expand("{\"a\": \"{date:yyyy}\"}", new DateTime(2026, 1, 1), () => null));
        Assert.Equal("{nope} 2026", Placeholders.Expand("{nope} {date:yyyy}", new DateTime(2026, 1, 1), () => null));
        Assert.True(Placeholders.Uses("x {Selection}", "selection"));
        Assert.False(Placeholders.Uses("x {{selection}", "selection"));
    }
}
