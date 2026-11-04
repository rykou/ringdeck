using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RingDeck.Core;

/// <summary>Something the AI may ask RingDeck to do. <see cref="Parameters"/> is a JSON schema of type object, kept
/// to the plain subset (type, description, enum, properties, required) that all three services accept.</summary>
public sealed record AiTool(string Name, string Description, JsonObject Parameters);

/// <summary>The AI asking for a tool to run. <see cref="Id"/> ties the result back to it.</summary>
public sealed record ToolCall(string Id, string Name, JsonObject Arguments);

/// <summary>What running a tool gave back, sent to the AI on the next turn.</summary>
public sealed record ToolResult(string CallId, string Name, string Content, bool IsError = false);

public enum AgentRole { User, Assistant, Tools }

/// <summary>One turn of a conversation in which the AI uses tools: the user's text, the AI's reply (text and tool
/// calls), or the results of those calls.</summary>
public sealed class AgentMessage
{
    public AgentRole Role { get; init; }
    public string Text { get; init; } = "";
    public IReadOnlyList<ToolCall> Calls { get; init; } = Array.Empty<ToolCall>();
    /// <summary>Settable so a long conversation can shorten old results it no longer needs in full.</summary>
    public List<ToolResult> Results { get; init; } = new();
    /// <summary>The AI's turn exactly as the service sent it (Anthropic and Gemini), sent back unchanged: it can hold
    /// parts RingDeck doesn't read but the service needs to see again, such as Gemini's thought signatures.</summary>
    public JsonNode? Raw { get; init; }

    public static AgentMessage User(string text) => new() { Role = AgentRole.User, Text = text };
    public static AgentMessage Answer(AgentReply reply) => new() { Role = AgentRole.Assistant, Text = reply.Text, Calls = reply.Calls, Raw = reply.Raw };
    public static AgentMessage ToolResults(IEnumerable<ToolResult> results) => new() { Role = AgentRole.Tools, Results = results.ToList() };
}

/// <summary>The AI's turn: text (often empty when it calls tools), the tools it wants run, and whether it was cut
/// off by its token limit.</summary>
public sealed record AgentReply(string Text, IReadOnlyList<ToolCall> Calls, JsonNode? Raw, bool CutOff = false);

/// <summary>Builds tool-using requests (not streamed) and reads their replies, for the OpenAI, Anthropic and Gemini
/// formats. The custom format has no way to describe tools, so it can't be used.</summary>
public static class AiTools
{
    static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Room for a summary of several emails; the 1024 used for quick chat answers cuts them off.</summary>
    public const int DefaultMaxTokens = 4096;

    public static bool Supports(AiFormat format) => format != AiFormat.Custom;

    public static HttpCall Build(AiConnection c, string system, IReadOnlyList<AgentMessage> messages, IReadOnlyList<AiTool> tools, Func<string, string> reveal)
    {
        if (!Supports(c.Format)) throw new AiException("This connection uses the custom format, which can't use tools. Pick an OpenAI, Anthropic or Gemini connection.");
        // The address and the sign-in headers (including the user's own) are the same as for chat.
        var chat = AiRequests.Build(c, null, Array.Empty<ChatMessage>(), reveal);
        var url = c.Format == AiFormat.Gemini ? NotStreamed(chat.Url) : chat.Url;
        var body = c.Format switch
        {
            AiFormat.Anthropic => Anthropic(c, system, messages, tools),
            AiFormat.Gemini => Gemini(c, system, messages, tools),
            _ => OpenAi(c, system, messages, tools),
        };
        return new HttpCall(url, chat.Headers, body, false);
    }

    /// <summary>Gemini's streaming address, turned into the one that answers in one piece.</summary>
    internal static string NotStreamed(string url)
    {
        url = url.Replace(":streamGenerateContent", ":generateContent", StringComparison.OrdinalIgnoreCase);
        var q = url.IndexOf('?');
        if (q < 0) return url;
        var kept = url[(q + 1)..].Split('&').Where(p => p.Length > 0 && !p.Equals("alt=sse", StringComparison.OrdinalIgnoreCase)).ToList();
        return url[..q] + (kept.Count > 0 ? "?" + string.Join("&", kept) : "");
    }

    static string OpenAi(AiConnection c, string system, IReadOnlyList<AgentMessage> messages, IReadOnlyList<AiTool> tools)
    {
        var list = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = system } };
        foreach (var m in messages)
            switch (m.Role)
            {
                case AgentRole.User:
                    list.Add(new JsonObject { ["role"] = "user", ["content"] = m.Text });
                    break;
                case AgentRole.Assistant:
                    var turn = new JsonObject { ["role"] = "assistant", ["content"] = m.Text.Length > 0 ? m.Text : null };
                    if (m.Calls.Count > 0)
                        turn["tool_calls"] = new JsonArray(m.Calls.Select(call => (JsonNode)new JsonObject
                        {
                            ["id"] = call.Id, ["type"] = "function",
                            ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.Arguments.ToJsonString(Json) },
                        }).ToArray());
                    list.Add(turn);
                    break;
                default:
                    foreach (var r in m.Results)
                        list.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = r.CallId, ["content"] = r.IsError ? "Error: " + r.Content : r.Content });
                    break;
            }
        var body = new JsonObject
        {
            ["model"] = c.Model.Trim(),
            ["messages"] = list,
            ["tools"] = new JsonArray(tools.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = t.Parameters.DeepClone() },
            }).ToArray()),
        };
        if (c.Temperature is { } temperature) body["temperature"] = temperature;
        if (c.MaxTokens is { } n) body["max_tokens"] = n;
        return body.ToJsonString(Json);
    }

    static string Anthropic(AiConnection c, string system, IReadOnlyList<AgentMessage> messages, IReadOnlyList<AiTool> tools)
    {
        var list = new JsonArray();
        foreach (var m in messages)
            switch (m.Role)
            {
                case AgentRole.User:
                    list.Add(new JsonObject { ["role"] = "user", ["content"] = m.Text });
                    break;
                case AgentRole.Assistant:
                    list.Add(new JsonObject { ["role"] = "assistant", ["content"] = m.Raw?.DeepClone() ?? AnthropicContent(m) });
                    break;
                default:
                    list.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = new JsonArray(m.Results.Select(r => (JsonNode)new JsonObject
                        {
                            ["type"] = "tool_result", ["tool_use_id"] = r.CallId, ["content"] = r.Content, ["is_error"] = r.IsError,
                        }).ToArray()),
                    });
                    break;
            }
        var body = new JsonObject
        {
            ["model"] = c.Model.Trim(),
            ["max_tokens"] = c.MaxTokens ?? DefaultMaxTokens,
            ["system"] = system,
            ["messages"] = list,
            ["tools"] = new JsonArray(tools.Select(t => (JsonNode)new JsonObject
            {
                ["name"] = t.Name, ["description"] = t.Description, ["input_schema"] = t.Parameters.DeepClone(),
            }).ToArray()),
        };
        if (c.Temperature is { } temperature) body["temperature"] = temperature;
        return body.ToJsonString(Json);
    }

    /// <summary>An assistant turn rebuilt from its parts, for when the raw reply wasn't kept.</summary>
    static JsonArray AnthropicContent(AgentMessage m)
    {
        var parts = new JsonArray();
        if (m.Text.Length > 0) parts.Add(new JsonObject { ["type"] = "text", ["text"] = m.Text });
        foreach (var call in m.Calls)
            parts.Add(new JsonObject { ["type"] = "tool_use", ["id"] = call.Id, ["name"] = call.Name, ["input"] = call.Arguments.DeepClone() });
        return parts;
    }

    static string Gemini(AiConnection c, string system, IReadOnlyList<AgentMessage> messages, IReadOnlyList<AiTool> tools)
    {
        var contents = new JsonArray();
        foreach (var m in messages)
            switch (m.Role)
            {
                case AgentRole.User:
                    contents.Add(new JsonObject { ["role"] = "user", ["parts"] = new JsonArray(new JsonObject { ["text"] = m.Text }) });
                    break;
                case AgentRole.Assistant:
                    contents.Add(m.Raw?.DeepClone() ?? GeminiContent(m));
                    break;
                default:
                    contents.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["parts"] = new JsonArray(m.Results.Select(r =>
                        {
                            var response = new JsonObject { ["name"] = r.Name, ["response"] = new JsonObject { [r.IsError ? "error" : "result"] = r.Content } };
                            if (!r.CallId.StartsWith(GeminiIdPrefix, StringComparison.Ordinal)) response["id"] = r.CallId;   // only ids Gemini gave
                            return (JsonNode)new JsonObject { ["functionResponse"] = response };
                        }).ToArray()),
                    });
                    break;
            }
        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = system }) },
            ["contents"] = contents,
            ["tools"] = new JsonArray(new JsonObject
            {
                ["functionDeclarations"] = new JsonArray(tools.Select(t => (JsonNode)new JsonObject
                {
                    ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = t.Parameters.DeepClone(),
                }).ToArray()),
            }),
        };
        var config = new JsonObject { ["maxOutputTokens"] = c.MaxTokens ?? DefaultMaxTokens };
        if (c.Temperature is { } temperature) config["temperature"] = temperature;
        body["generationConfig"] = config;
        return body.ToJsonString(Json);
    }

    static JsonObject GeminiContent(AgentMessage m)
    {
        var parts = new JsonArray();
        if (m.Text.Length > 0) parts.Add(new JsonObject { ["text"] = m.Text });
        foreach (var call in m.Calls)
            parts.Add(new JsonObject { ["functionCall"] = new JsonObject { ["name"] = call.Name, ["args"] = call.Arguments.DeepClone() } });
        return new JsonObject { ["role"] = "model", ["parts"] = parts };
    }

    /// <summary>Marks ids RingDeck made up for Gemini calls that came without one.</summary>
    const string GeminiIdPrefix = "ringdeck-";

    /// <summary>Reads a whole (not streamed) reply. Throws <see cref="AiException"/> when it holds an error or no answer.</summary>
    public static AgentReply Parse(AiFormat format, string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { throw new AiException("The service sent back something that isn't JSON: " + AiResponses.Shorten(json, null)); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new AiException("The service sent back an unexpected answer: " + AiResponses.Shorten(json, null));
            if (root.TryGetProperty("error", out var error))
                throw new AiException(error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m) ? m.ToString() : error.ToString());
            try
            {
                return format switch
                {
                    AiFormat.Anthropic => ParseAnthropic(root),
                    AiFormat.Gemini => ParseGemini(root),
                    _ => ParseOpenAi(root),
                };
            }
            catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
            {
                throw new AiException("The service's answer was not in the expected shape: " + AiResponses.Shorten(json, null));
            }
        }
    }

    static AgentReply ParseOpenAi(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            throw new AiException("The service's answer had no choices.");
        var choice = choices[0];
        var message = choice.GetProperty("message");
        var text = message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString()! : "";
        var calls = new List<ToolCall>();
        if (message.TryGetProperty("tool_calls", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var call in list.EnumerateArray())
            {
                var function = call.GetProperty("function");
                var args = function.TryGetProperty("arguments", out var a) ? a : default;
                calls.Add(new ToolCall(call.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "", function.GetProperty("name").GetString() ?? "",
                    Arguments(args.ValueKind == JsonValueKind.String ? args.GetString() : args.ValueKind == JsonValueKind.Object ? args.GetRawText() : null)));
            }
        var cutOff = choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String && reason.GetString() == "length";
        return new AgentReply(text.Trim(), calls, null, cutOff);
    }

    static AgentReply ParseAnthropic(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            throw new AiException("The service's answer had no content.");
        var text = new List<string>();
        var calls = new List<ToolCall>();
        foreach (var block in content.EnumerateArray())
        {
            var type = block.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type == "text" && block.TryGetProperty("text", out var s)) text.Add(s.GetString() ?? "");
            else if (type == "tool_use")
                calls.Add(new ToolCall(block.GetProperty("id").GetString() ?? "", block.GetProperty("name").GetString() ?? "",
                    Arguments(block.TryGetProperty("input", out var input) ? input.GetRawText() : null)));
        }
        var cutOff = root.TryGetProperty("stop_reason", out var stop) && stop.ValueKind == JsonValueKind.String && stop.GetString() == "max_tokens";
        return new AgentReply(string.Join("\n", text).Trim(), calls, JsonNode.Parse(content.GetRawText()), cutOff);
    }

    static AgentReply ParseGemini(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
        {
            var blocked = root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out var why) ? why.ToString() : null;
            throw new AiException(blocked is null ? "The service's answer had no candidates." : "The service refused to answer: " + blocked);
        }
        var candidate = candidates[0];
        var text = new List<string>();
        var calls = new List<ToolCall>();
        JsonNode? raw = null;
        if (candidate.TryGetProperty("content", out var content))
        {
            raw = JsonNode.Parse(content.GetRawText());
            if (raw is JsonObject o && !o.ContainsKey("role")) o["role"] = "model";
            if (content.TryGetProperty("parts", out var parts))
                foreach (var part in parts.EnumerateArray())
                {
                    var thought = part.TryGetProperty("thought", out var th) && th.ValueKind == JsonValueKind.True;
                    if (part.TryGetProperty("text", out var s) && !thought) text.Add(s.GetString() ?? "");
                    if (part.TryGetProperty("functionCall", out var call))
                        calls.Add(new ToolCall(call.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } given ? given : GeminiIdPrefix + calls.Count,
                            call.GetProperty("name").GetString() ?? "", Arguments(call.TryGetProperty("args", out var args) ? args.GetRawText() : null)));
                }
        }
        var cutOff = candidate.TryGetProperty("finishReason", out var reason) && reason.ValueKind == JsonValueKind.String && reason.GetString() == "MAX_TOKENS";
        return new AgentReply(string.Join("", text).Trim(), calls, raw, cutOff);
    }

    /// <summary>A call's arguments as an object; anything unreadable becomes an empty object, and the tool then
    /// reports what's missing.</summary>
    static JsonObject Arguments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        try { return JsonNode.Parse(json) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }
}
