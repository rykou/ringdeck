using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RingDeck.Core;

/// <summary>One message in a conversation. Role is "user" or "assistant".</summary>
public sealed record ChatMessage(string Role, string Content);

/// <summary>A request ready to send: always a POST with a JSON body.</summary>
public sealed record HttpCall(string Url, IReadOnlyList<KeyValuePair<string, string>> Headers, string Body, bool Stream);

/// <summary>Builds the HTTP request for each supported API format.</summary>
public static class AiRequests
{
    const string KeyPlaceholder = "{{api_key}}";
    static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string DefaultEndpoint(AiFormat format) => format switch
    {
        AiFormat.OpenAi => "https://api.openai.com/v1/chat/completions",
        AiFormat.Anthropic => "https://api.anthropic.com/v1/messages",
        AiFormat.Gemini => "https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse",
        _ => "",
    };

    /// <summary>True for plain http:// to anything but this computer, where a key would cross the network unencrypted.</summary>
    public static bool IsInsecure(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback
        && !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

    /// <param name="reveal">Turns a stored secret ("dpapi:...") back into plain text; plain values pass through.</param>
    public static HttpCall Build(AiConnection c, string? system, IReadOnlyList<ChatMessage> messages, Func<string, string> reveal)
    {
        var key = c.ApiKey.Length > 0 ? reveal(c.ApiKey) : "";
        var headers = new List<KeyValuePair<string, string>>();
        string url = c.Endpoint.Trim(), body;
        system = string.IsNullOrWhiteSpace(system) ? null : system;
        switch (c.Format)
        {
            case AiFormat.Anthropic:
                if (key.Length > 0) headers.Add(new("x-api-key", key));
                headers.Add(new("anthropic-version", "2023-06-01"));
                url = WithPath(url, "/messages");
                body = Anthropic(c, system, messages);
                break;
            case AiFormat.Gemini:
                if (key.Length > 0) headers.Add(new("x-goog-api-key", key));
                url = url.Replace("{model}", Uri.EscapeDataString(c.Model.Trim()));
                body = Gemini(c, system, messages);
                break;
            case AiFormat.Custom:
                body = Custom(c, system, messages, key);
                break;
            default:
                if (key.Length > 0) headers.Add(new("Authorization", "Bearer " + key));
                url = WithPath(url, "/chat/completions");
                body = OpenAi(c, system, messages);
                break;
        }
        // The user's own headers come last and replace any header of the same name.
        foreach (var h in c.Headers)
        {
            if (string.IsNullOrWhiteSpace(h.Name)) continue;
            var name = h.Name.Trim();
            var value = (h.Secret ? reveal(h.Value) : h.Value).Replace(KeyPlaceholder, key);
            headers.RemoveAll(x => x.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
            headers.Add(new(name, value));
        }
        return new HttpCall(url.Replace(KeyPlaceholder, Uri.EscapeDataString(key)), headers, body, c.Format != AiFormat.Custom);
    }

    /// <summary>Accepts a base URL ("…/v1") as well as the full one: adds the format's chat path when it is missing,
    /// keeping any query string.</summary>
    static string WithPath(string url, string chatPath)
    {
        var q = url.IndexOf('?');
        var (path, query) = q < 0 ? (url, "") : (url[..q], url[q..]);
        path = path.TrimEnd('/');
        return (path.EndsWith(chatPath, StringComparison.OrdinalIgnoreCase) ? path : path + chatPath) + query;
    }

    static string OpenAi(AiConnection c, string? system, IReadOnlyList<ChatMessage> messages)
    {
        var list = new JsonArray();
        if (system is not null) list.Add(new JsonObject { ["role"] = "system", ["content"] = system });
        foreach (var m in messages) list.Add(new JsonObject { ["role"] = m.Role, ["content"] = m.Content });
        var body = new JsonObject { ["model"] = c.Model.Trim(), ["messages"] = list, ["stream"] = true };
        if (c.Temperature is { } t) body["temperature"] = t;
        if (c.MaxTokens is { } n) body["max_tokens"] = n;
        return body.ToJsonString(Json);
    }

    static string Anthropic(AiConnection c, string? system, IReadOnlyList<ChatMessage> messages)
    {
        var list = new JsonArray();
        foreach (var m in messages) list.Add(new JsonObject { ["role"] = m.Role, ["content"] = m.Content });
        var body = new JsonObject { ["model"] = c.Model.Trim(), ["max_tokens"] = c.MaxTokens ?? 1024, ["messages"] = list, ["stream"] = true };
        if (system is not null) body["system"] = system;
        if (c.Temperature is { } t) body["temperature"] = t;
        return body.ToJsonString(Json);
    }

    static string Gemini(AiConnection c, string? system, IReadOnlyList<ChatMessage> messages)
    {
        var contents = new JsonArray();
        foreach (var m in messages)
            contents.Add(new JsonObject
            {
                ["role"] = m.Role == "assistant" ? "model" : "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = m.Content }),
            });
        var body = new JsonObject { ["contents"] = contents };
        if (system is not null) body["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = system }) };
        if (c.Temperature is not null || c.MaxTokens is not null)
        {
            var config = new JsonObject();
            if (c.Temperature is { } t) config["temperature"] = t;
            if (c.MaxTokens is { } n) config["maxOutputTokens"] = n;
            body["generationConfig"] = config;
        }
        return body.ToJsonString(Json);
    }

    static string Custom(AiConnection c, string? system, IReadOnlyList<ChatMessage> messages, string key)
    {
        var history = new JsonArray();
        foreach (var m in messages) history.Add(new JsonObject { ["role"] = m.Role, ["content"] = m.Content });
        var prompt = messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";
        return c.BodyTemplate
            .Replace("{{messages}}", history.ToJsonString(Json))
            .Replace("{{model}}", Escape(c.Model.Trim()))
            .Replace("{{prompt}}", Escape(prompt))
            .Replace("{{system}}", Escape(system ?? ""))
            .Replace(KeyPlaceholder, Escape(key));
    }

    /// <summary>JSON string contents without the surrounding quotes, so templates write "{{prompt}}".</summary>
    static string Escape(string text) { var s = JsonSerializer.Serialize(text, Json); return s[1..^1]; }
}
