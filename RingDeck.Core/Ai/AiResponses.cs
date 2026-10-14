using System.Text;
using System.Text.Json;

namespace RingDeck.Core;

/// <summary>What one server-sent event contributed: some text, an error, or the end of the reply.</summary>
public readonly record struct SseResult(string? Text, string? Error, bool Done);

public static class AiResponses
{
    /// <summary>Reads the JSON of one "data:" line of a streamed reply.</summary>
    public static SseResult ParseSse(AiFormat format, string data)
    {
        if (data.Trim() == "[DONE]") return new(null, null, true);
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return default;
            if (root.TryGetProperty("error", out var error))
                return new(null, error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m) ? m.ToString() : error.ToString(), false);
            switch (format)
            {
                case AiFormat.Anthropic:
                    var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                    if (type == "message_stop") return new(null, null, true);
                    if (type == "content_block_delta" && root.TryGetProperty("delta", out var delta) && delta.TryGetProperty("text", out var text))
                        return new(text.GetString(), null, false);
                    return default;
                case AiFormat.Gemini:
                    if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0) return default;
                    if (!candidates[0].TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts)) return default;
                    var sb = new StringBuilder();
                    foreach (var part in parts.EnumerateArray())
                        if (part.TryGetProperty("text", out var p)) sb.Append(p.GetString());
                    return new(sb.Length > 0 ? sb.ToString() : null, null, false);
                default:
                    if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
                        && choices[0].TryGetProperty("delta", out var d) && d.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                        return new(c.GetString(), null, false);
                    return default;
            }
        }
        catch (JsonException) { return default; }
        catch (InvalidOperationException) { return default; }   // a field had an unexpected JSON type
    }

    /// <summary>Follows a path like "choices[0].message.content" and returns the value as text, or null.</summary>
    public static string? Extract(string json, string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var node = doc.RootElement;
            foreach (var raw in path.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                var part = raw.Trim();
                var bracket = part.IndexOf('[');
                var name = bracket < 0 ? part : part[..bracket];
                if (name.Length > 0)
                {
                    if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(name, out node)) return null;
                }
                while (bracket >= 0)
                {
                    var close = part.IndexOf(']', bracket);
                    if (close < 0 || !int.TryParse(part.AsSpan(bracket + 1, close - bracket - 1), out var index)) return null;
                    if (node.ValueKind != JsonValueKind.Array || index < 0 || index >= node.GetArrayLength()) return null;
                    node = node[index];
                    bracket = part.IndexOf('[', close);
                }
            }
            return node.ValueKind switch
            {
                JsonValueKind.String => node.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => node.GetRawText(),
            };
        }
        catch (JsonException) { return null; }
    }

    /// <summary>"HTTP 401: {first 300 characters of the body}", with the API key blanked out.</summary>
    public static string ErrorSummary(int status, string body, string? key)
    {
        var text = Shorten(body, key);
        return text.Length == 0 ? $"HTTP {status}" : $"HTTP {status}: {text}";
    }

    internal static string Shorten(string body, string? key)
    {
        var text = body.Trim();
        if (!string.IsNullOrEmpty(key)) text = text.Replace(key, "•••");
        return text.Length > 300 ? text[..300] + "…" : text;
    }
}

public static class AiChat
{
    /// <summary>The first message: the slice's instruction, a blank line, then the grabbed text inside
    /// &lt;text&gt; tags. The tags tell the model the text is something to work on, not a message to it; without them
    /// "Can you do that?" gets answered instead of rewritten.</summary>
    public static string Compose(string instruction, string? input)
    {
        var parts = new List<string>();
        if (instruction?.Trim() is { Length: > 0 } i) parts.Add(i);
        if (input?.Trim() is { Length: > 0 } text) parts.Add($"<text>\n{text}\n</text>");
        return string.Join("\n\n", parts);
    }

    /// <summary>One question, one answer, no window: what a slice with the chat window turned off does.</summary>
    public static async Task<string> AskOnceAsync(AiClient client, AiConnection connection, AiChatAction action, string? input,
        Func<string, string> reveal, CancellationToken ct)
    {
        var message = Compose(action.Instruction, input);
        if (message.Length == 0) throw new AiException("There was nothing to send: no text was selected and the slice has no instruction.");
        var reply = new System.Text.StringBuilder();
        await foreach (var piece in client.SendAsync(connection, action.SystemPrompt, new[] { new ChatMessage("user", message) }, reveal, ct))
            reply.Append(piece);
        return reply.ToString().Trim() is { Length: > 0 } text ? text : throw new AiException("The service sent back an empty answer.");
    }
}
