using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;

namespace RingDeck.Core;

/// <summary>A failure worth showing in the chat: bad status, an error in the stream, an answer that isn't there.</summary>
public sealed class AiException(string message) : Exception(message);

/// <summary>Sends a conversation to a connection and yields the reply as it arrives.</summary>
public sealed class AiClient
{
    readonly HttpClient _http;

    public AiClient(HttpMessageHandler? handler = null) =>
        _http = new HttpClient(handler ?? new SocketsHttpHandler()) { Timeout = TimeSpan.FromMinutes(2) };

    public async IAsyncEnumerable<string> SendAsync(AiConnection connection, string? system, IReadOnlyList<ChatMessage> messages,
        Func<string, string> reveal, [EnumeratorCancellation] CancellationToken ct)
    {
        var call = AiRequests.Build(connection, system, messages, reveal);
        if (!Uri.TryCreate(call.Url, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            throw new AiException("This connection has no valid web address (it should start with https://).");
        var key = connection.ApiKey.Length > 0 ? reveal(connection.ApiKey) : null;

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(call.Body, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in call.Headers)
            if (!request.Headers.TryAddWithoutValidation(name, value)) request.Content.Headers.TryAddWithoutValidation(name, value);
        if (call.Stream) request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        HttpResponseMessage response;
        try { response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (HttpRequestException ex) { throw new AiException("Could not reach the service: " + ex.Message); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new AiException("The service took longer than 2 minutes to answer."); }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new AiException(AiResponses.ErrorSummary((int)response.StatusCode, await response.Content.ReadAsStringAsync(ct), key));

            if (!call.Stream)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                yield return AiResponses.Extract(json, connection.ResponsePath)
                    ?? throw new AiException($"The response had nothing at {connection.ResponsePath}: {AiResponses.Shorten(json, key)}");
                yield break;
            }

            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;   // event names, comments, blank separators
                var result = AiResponses.ParseSse(connection.Format, line[5..].TrimStart());
                if (result.Error is { } error) throw new AiException(AiResponses.Shorten(error, key));
                if (result.Text is { Length: > 0 } text) yield return text;
                if (result.Done) yield break;
            }
        }
    }

    /// <summary>The models the connection's key can use (all pages), or an empty list for the custom format.</summary>
    public async Task<List<string>> ListModelsAsync(AiConnection connection, Func<string, string> reveal, CancellationToken ct)
    {
        if (AiModels.ListCall(connection, reveal) is not { } call) return new List<string>();
        var key = connection.ApiKey.Length > 0 ? reveal(connection.ApiKey) : null;
        var all = new List<string>();
        var url = call.Url;
        for (var page = 0; page < 10; page++)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) throw new AiException("This connection has no valid web address (it should start with https://).");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            foreach (var (name, value) in call.Headers) request.Headers.TryAddWithoutValidation(name, value);
            HttpResponseMessage response;
            try { response = await _http.SendAsync(request, ct); }
            catch (HttpRequestException ex) { throw new AiException("Could not reach the service: " + ex.Message); }
            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode) throw new AiException(AiResponses.ErrorSummary((int)response.StatusCode, body, key));
                var (ids, next) = AiModels.Parse(connection.Format, body);
                all.AddRange(ids);
                if (next is null) break;
                url = AiModels.NextPage(connection.Format, call.Url, next);
            }
        }
        return all.Distinct().ToList();
    }
}
