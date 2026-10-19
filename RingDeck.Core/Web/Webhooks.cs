using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RingDeck.Core;

/// <summary>A failure worth telling the user about: a missing setting, an unreachable service, a bad status.</summary>
public sealed class WebhookException(string message) : Exception(message);

/// <summary>Builds the HTTP request for each web service RingDeck knows.</summary>
public static class Webhooks
{
    public static string Name(WebService service) => service switch
    {
        WebService.Discord => "Discord",
        WebService.Slack => "Slack",
        WebService.Teams => "Microsoft Teams",
        WebService.GoogleChat => "Google Chat",
        WebService.Telegram => "Telegram",
        WebService.Ntfy => "ntfy",
        WebService.Pushover => "Pushover",
        WebService.HomeAssistant => "Home Assistant",
        WebService.Automation => "n8n, Make, Zapier or IFTTT",
        _ => "a web address",
    };

    /// <summary>Services that take an uploaded picture; the others can only link to one that is already online.</summary>
    public static bool SupportsImages(WebService service) =>
        service is WebService.Discord or WebService.Telegram or WebService.Ntfy or WebService.Pushover or WebService.Automation or WebService.Teams;

    /// <summary>Teams refuses webhook messages over about 28 KB, picture included (base64 makes it a third bigger).
    /// The picture gets what is left after the text, within this many bytes.</summary>
    public const int TeamsMessageLimit = 27_000;

    /// <summary>How big the picture may be, as a file, for a Teams message with this text.</summary>
    public static int TeamsImageBudget(string message, string title) =>
        Math.Max(0, (TeamsMessageLimit - 1_500 - Encoding.UTF8.GetByteCount(message) * 2 - Encoding.UTF8.GetByteCount(title) * 2) * 3 / 4);

    /// <summary>The request for one message. <paramref name="expand"/> fills placeholders in a custom body, passing every
    /// value through the given escaping.</summary>
    public static HttpRequestMessage Build(WebRequest r, string message, ClipImage? image, Func<string, string> reveal,
        Func<string, Func<string, string>, string> expand)
    {
        var url = reveal(r.Url).Trim();
        var token = reveal(r.Token).Trim();
        var target = reveal(r.Target).Trim();
        var title = r.Title.Trim();
        if (!SupportsImages(r.Service)) image = null;
        if (r.Service != WebService.Custom && message.Trim().Length == 0 && image is null)
            throw new WebhookException(!SupportsImages(r.Service) ? "There is nothing to send: the message is empty."
                : r.SendImage ? "There is nothing to send: the message is empty and there is no image on the clipboard."
                : "There is nothing to send: the message is empty. To send the picture on the clipboard (a screenshot), tick "
                  + "\"Also send the picture on the clipboard\" in this request's settings.");

        switch (r.Service)
        {
            case WebService.Discord:
            {
                var payload = new JsonObject { ["content"] = Cut(message, 2000) };
                if (title.Length > 0) payload["username"] = Cut(title, 80);
                if (image is null) return Json(HttpMethod.Post, Address(url), payload);
                var form = new MultipartFormDataContent { { new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"), "payload_json" } };
                form.Add(File(image), "files[0]", image.FileName);
                return new HttpRequestMessage(HttpMethod.Post, Address(url)) { Content = form };
            }
            case WebService.Slack:
            case WebService.GoogleChat:
                return Json(HttpMethod.Post, Address(url), new JsonObject { ["text"] = title.Length > 0 ? $"*{title}*\n{message}" : message });
            case WebService.Teams:
            {
                var body = new JsonArray();
                if (title.Length > 0) body.Add(new JsonObject { ["type"] = "TextBlock", ["text"] = title, ["weight"] = "Bolder", ["size"] = "Medium", ["wrap"] = true });
                if (message.Trim().Length > 0) body.Add(new JsonObject { ["type"] = "TextBlock", ["text"] = message, ["wrap"] = true });
                if (image is not null)
                {
                    if (image.Data.Length > TeamsImageBudget(message, title))
                        throw new WebhookException("The picture is too big for a Teams message (about 28 KB in all), even made smaller.");
                    body.Add(new JsonObject
                    {
                        ["type"] = "Image", ["url"] = $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Data)}",
                        ["msTeams"] = new JsonObject { ["allowExpand"] = true },
                    });
                }
                var card = new JsonObject
                {
                    ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json", ["type"] = "AdaptiveCard", ["version"] = "1.4", ["body"] = body,
                };
                return Json(HttpMethod.Post, Address(url), new JsonObject
                {
                    ["type"] = "message",
                    ["attachments"] = new JsonArray(new JsonObject { ["contentType"] = "application/vnd.microsoft.card.adaptive", ["content"] = card }),
                });
            }
            case WebService.Telegram:
            {
                if (token.Length == 0) throw new WebhookException("Telegram needs the bot token (from @BotFather).");
                if (target.Length == 0) throw new WebhookException("Telegram needs the chat ID to send to.");
                var api = $"https://api.telegram.org/bot{token}/";
                if (image is null)
                    return Json(HttpMethod.Post, Address(api + "sendMessage"), new JsonObject { ["chat_id"] = target, ["text"] = Cut(message, 4096) });
                var form = new MultipartFormDataContent { { new StringContent(target), "chat_id" } };
                if (message.Trim().Length > 0) form.Add(new StringContent(Cut(message, 1024)), "caption");
                form.Add(File(image), "photo", image.FileName);
                return new HttpRequestMessage(HttpMethod.Post, Address(api + "sendPhoto")) { Content = form };
            }
            case WebService.Ntfy:
            {
                var topic = Address(url);
                HttpRequestMessage request;
                if (image is null)
                {
                    request = new HttpRequestMessage(HttpMethod.Post, WithQuery(topic, ("title", title)))
                    { Content = new StringContent(message, Encoding.UTF8, "text/plain") };
                }
                else
                {
                    // An attachment is the request body; the text goes in the query (headers can't carry every character).
                    request = new HttpRequestMessage(HttpMethod.Put, WithQuery(topic, ("filename", image.FileName), ("message", message), ("title", title)))
                    { Content = File(image) };
                }
                Bearer(request, token);
                return request;
            }
            case WebService.Pushover:
            {
                if (token.Length == 0) throw new WebhookException("Pushover needs the application's API token.");
                if (target.Length == 0) throw new WebhookException("Pushover needs your user key.");
                var form = new MultipartFormDataContent
                {
                    { new StringContent(token), "token" },
                    { new StringContent(target), "user" },
                    { new StringContent(Cut(message.Trim().Length > 0 ? message : "Image", 1024)), "message" },
                };
                if (title.Length > 0) form.Add(new StringContent(Cut(title, 250)), "title");
                if (image is not null) form.Add(File(image), "attachment", image.FileName);
                return new HttpRequestMessage(HttpMethod.Post, Address("https://api.pushover.net/1/messages.json")) { Content = form };
            }
            case WebService.HomeAssistant:
            {
                var request = Json(HttpMethod.Post, Address(url), new JsonObject { ["message"] = message, ["title"] = title });
                Bearer(request, token);
                return request;
            }
            case WebService.Automation:
            {
                // message/title for n8n, Make and Zapier; value1/value2 for IFTTT.
                if (image is null)
                    return Json(HttpMethod.Post, Address(url), new JsonObject { ["message"] = message, ["title"] = title, ["value1"] = message, ["value2"] = title });
                var form = new MultipartFormDataContent { { new StringContent(message), "message" }, { new StringContent(title), "title" } };
                form.Add(File(image), "image", image.FileName);
                return new HttpRequestMessage(HttpMethod.Post, Address(url)) { Content = form };
            }
            default:
                return Custom(r, url, token, reveal, expand);
        }
    }

    static HttpRequestMessage Custom(WebRequest r, string url, string token, Func<string, string> reveal,
        Func<string, Func<string, string>, string> expand)
    {
        var method = new HttpMethod(string.IsNullOrWhiteSpace(r.Method) ? "POST" : r.Method.Trim().ToUpperInvariant());
        var json = r.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase);
        var request = new HttpRequestMessage(method, Address(expand(url, Uri.EscapeDataString)));
        var body = expand(r.Body, json ? JsonText : (v => v));
        if (body.Length > 0 && method != HttpMethod.Get && method != HttpMethod.Head)
        {
            request.Content = new StringContent(body, Encoding.UTF8);
            var type = MediaTypeHeaderValue.TryParse(r.ContentType.Trim(), out var parsed) ? parsed : new MediaTypeHeaderValue("text/plain");
            type.CharSet ??= "utf-8";
            request.Content.Headers.ContentType = type;
        }
        foreach (var h in r.Headers.Where(h => h.Name.Trim().Length > 0))
        {
            var value = h.Secret ? reveal(h.Value) : h.Value;
            if (!request.Headers.TryAddWithoutValidation(h.Name.Trim(), value)) request.Content?.Headers.TryAddWithoutValidation(h.Name.Trim(), value);
        }
        if (request.Headers.Authorization is null) Bearer(request, token);
        return request;
    }

    /// <summary>The secrets of a request, revealed: what must never appear in an error shown to the user.</summary>
    public static List<string> Secrets(WebRequest r, Func<string, string> reveal)
    {
        var list = new List<string> { reveal(r.Url).Trim(), reveal(r.Token).Trim(), reveal(r.Target).Trim() };
        list.AddRange(r.Headers.Where(h => h.Secret).Select(h => reveal(h.Value)));
        // A webhook URL's last part is its token; hide it on its own too, in case the service echoes only that.
        if (Uri.TryCreate(list[0], UriKind.Absolute, out var u) && u.Segments.Length > 1) list.Add(u.Segments[^1].Trim('/'));
        return list.Where(s => s.Length >= 6).Distinct().OrderByDescending(s => s.Length).ToList();
    }

    static Uri Address(string url)
    {
        if (url.Length == 0) throw new WebhookException("The web address is empty. Paste the webhook URL in the slice's settings.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new WebhookException("The web address should start with https://.");
        return uri;
    }

    static Uri WithQuery(Uri uri, params (string Name, string Value)[] parts)
    {
        var extra = string.Join("&", parts.Where(p => p.Value.Length > 0).Select(p => p.Name + "=" + Uri.EscapeDataString(p.Value)));
        if (extra.Length == 0) return uri;
        var builder = new UriBuilder(uri);
        builder.Query = builder.Query.TrimStart('?') is { Length: > 0 } q ? q + "&" + extra : extra;
        return builder.Uri;
    }

    static HttpRequestMessage Json(HttpMethod method, Uri url, JsonNode body) =>
        new(method, url) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    static ByteArrayContent File(ClipImage image)
    {
        var content = new ByteArrayContent(image.Data);
        content.Headers.ContentType = new MediaTypeHeaderValue(image.ContentType);
        return content;
    }

    static void Bearer(HttpRequestMessage request, string token)
    {
        if (token.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>A value as it goes inside a JSON string (without the quotes).</summary>
    public static string JsonText(string value) => JsonSerializer.Serialize(value)[1..^1];
}

/// <summary>Sends webhook requests and turns failures into short, secret-free messages.</summary>
public sealed class WebhookClient
{
    readonly HttpClient _http;

    public WebhookClient(HttpMessageHandler? handler = null) =>
        _http = new HttpClient(handler ?? new SocketsHttpHandler()) { Timeout = TimeSpan.FromSeconds(30) };

    public async Task SendAsync(HttpRequestMessage request, IReadOnlyList<string> secrets, CancellationToken ct)
    {
        HttpResponseMessage response;
        try { response = await _http.SendAsync(request, ct); }
        catch (HttpRequestException ex) { throw new WebhookException(Hide("Could not reach the service: " + ex.Message, secrets)); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new WebhookException("The service did not answer within 30 seconds."); }
        using (response)
        {
            if (response.IsSuccessStatusCode) return;
            var body = await response.Content.ReadAsStringAsync(ct);
            body = body.ReplaceLineEndings(" ").Trim();
            if (body.Length > 200) body = body[..200] + "…";
            throw new WebhookException(Hide($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}{(body.Length > 0 ? ": " + body : "")}", secrets));
        }
    }

    static string Hide(string text, IReadOnlyList<string> secrets)
    {
        foreach (var s in secrets) text = text.Replace(s, "•••", StringComparison.Ordinal);
        return text;
    }
}
