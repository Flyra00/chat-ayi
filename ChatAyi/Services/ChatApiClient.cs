using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace ChatAyi.Services;

public sealed class ChatApiClient
{
    public enum Provider
    {
        Cerebras,
        NvidiaIntegrate,
        Inception
    }

    private readonly HttpClient _cerebras;
    private readonly HttpClient _nvidia;
    private readonly HttpClient _inception;

    private const string CerebrasKeyStorageKey = "CEREBRAS_API_KEY";
    private const string NvidiaKeyStorageKey = "NVIDIA_API_KEY";
    private const string InceptionKeyStorageKey = "INCEPTION_API_KEY";

    private const string DefaultCerebrasModel = "gpt-oss-120b";
    private const string DefaultNvidiaModel = "z-ai/glm5";
    private const string DefaultInceptionModel = "mercury-2";

    public ChatApiClient(HttpClient cerebras, HttpClient nvidia, HttpClient inception)
    {
        _cerebras = cerebras;
        _nvidia = nvidia;
        _inception = inception;
    }

    public async Task<string> GetApiKeyAsync(Provider provider)
    {
        var storageKey = provider switch
        {
            Provider.NvidiaIntegrate => NvidiaKeyStorageKey,
            Provider.Inception => InceptionKeyStorageKey,
            _ => CerebrasKeyStorageKey
        };
        return (await SecureStorage.GetAsync(storageKey))?.Trim() ?? string.Empty;
    }

    public async Task SetApiKeyAsync(Provider provider, string key)
    {
        var clean = (key ?? string.Empty).Trim();
        if (clean.Length == 0) return;

        var storageKey = provider switch
        {
            Provider.NvidiaIntegrate => NvidiaKeyStorageKey,
            Provider.Inception => InceptionKeyStorageKey,
            _ => CerebrasKeyStorageKey
        };
        await SecureStorage.SetAsync(storageKey, clean);
    }

    public async Task<IReadOnlyList<string>> GetModelsAsync(Provider provider, CancellationToken ct)
    {
        var apiKey = await GetApiKeyAsync(provider);
        if (string.IsNullOrWhiteSpace(apiKey))
            return Array.Empty<string>();

        var http = provider switch
        {
            Provider.NvidiaIntegrate => _nvidia,
            Provider.Inception => _inception,
            _ => _cerebras
        };

        var path = provider == Provider.NvidiaIntegrate ? "models" : "/v1/models";
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

        using var resp = await http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized || resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                var storageKey = provider switch
                {
                    Provider.NvidiaIntegrate => NvidiaKeyStorageKey,
                    Provider.Inception => InceptionKeyStorageKey,
                    _ => CerebrasKeyStorageKey
                };
                SecureStorage.Remove(storageKey);
                throw new HttpRequestException("Unauthorized. API key cleared; please enter it again.");
            }

            throw new HttpRequestException(string.IsNullOrWhiteSpace(text)
                ? $"HTTP {(int)resp.StatusCode}"
                : text);
        }

        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var result = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var idProp)) continue;
                    var id = idProp.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(id)) result.Add(id);
                }
            }
        }
        catch
        {
            return Array.Empty<string>();
        }

        return result
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        Provider provider,
        IEnumerable<object> messages,
        string model,
        bool enableThinking,
        [EnumeratorCancellation] CancellationToken ct)
    {
#if DEBUG
        var sw = Stopwatch.StartNew();
#endif
        var apiKey = await GetApiKeyAsync(provider);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var name = provider switch
            {
                Provider.NvidiaIntegrate => "NVIDIA",
                Provider.Inception => "Inception",
                _ => "Cerebras"
            };
            throw new InvalidOperationException($"Missing {name} API key (set in SecureStorage)");
        }

        if (string.IsNullOrWhiteSpace(model))
            model = provider switch
            {
                Provider.NvidiaIntegrate => DefaultNvidiaModel,
                Provider.Inception => DefaultInceptionModel,
                _ => DefaultCerebrasModel
            };

        object payload;
        var maxTokens = provider == Provider.NvidiaIntegrate ? 1024 : 4096;

        if (provider == Provider.NvidiaIntegrate)
        {
            // Keep payload strictly OpenAI-compatible for maximum cross-account compatibility.
            // Some NVIDIA accounts/models reject vendor-specific params (e.g. chat_template_kwargs/extra_body/nvext).
            // We only nudge via system prompt when thinking is disabled.
            var finalMessages = messages;
            if (!enableThinking)
            {
                var list = new List<object>
                {
                    new { role = "system", content = "/no_think" }
                };
                if (messages != null)
                    list.AddRange(messages);
                finalMessages = list;
            }

            payload = new
            {
                model,
                messages = finalMessages,
                temperature = 0.7,
                top_p = 1,
                max_tokens = maxTokens,
                stream = true
            };
        }
        else
        {
            payload = new
            {
                model,
                messages,
                temperature = 0.7,
                top_p = 1,
                max_tokens = maxTokens,
                stream = true
            };
        }

        var json = JsonSerializer.Serialize(payload);
        var http = provider switch
        {
            Provider.NvidiaIntegrate => _nvidia,
            Provider.Inception => _inception,
            _ => _cerebras
        };
        // NOTE: For HttpClient, a leading '/' resets the BaseAddress path.
        // NVIDIA BaseAddress includes '/v1/', so we must use a relative path.
        var path = provider == Provider.NvidiaIntegrate ? "chat/completions" : "/v1/chat/completions";

        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        req.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

#if DEBUG
        Debug.WriteLine($"[{provider}] POST {http.BaseAddress}{path} stream=true; payload_bytes={Encoding.UTF8.GetByteCount(json)}");
#endif

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync(ct);

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized || resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                var storageKey = provider switch
                {
                    Provider.NvidiaIntegrate => NvidiaKeyStorageKey,
                    Provider.Inception => InceptionKeyStorageKey,
                    _ => CerebrasKeyStorageKey
                };
                SecureStorage.Remove(storageKey);
                throw new HttpRequestException("Unauthorized. API key cleared; please enter it again.");
            }

            try
            {
                using var doc = JsonDocument.Parse(text);
                if (TryExtractError(doc.RootElement, out var err) && !string.IsNullOrWhiteSpace(err))
                    throw new HttpRequestException(err);
            }
            catch
            {
                // ignore
            }

            throw new HttpRequestException(string.IsNullOrWhiteSpace(text)
                ? $"HTTP {(int)resp.StatusCode}"
                : text);
        }

        var mediaType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;

#if DEBUG
        Debug.WriteLine($"[{provider}] HTTP {(int)resp.StatusCode} {resp.StatusCode}; mediaType='{mediaType}'; t={sw.ElapsedMilliseconds}ms");
#endif

        // If upstream returns non-SSE JSON, parse it as a single-shot response.
        if (!IsEventStream(mediaType))
        {
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(text)) yield break;

            string final = null;
            string err = null;
            try
            {
                using var doc = JsonDocument.Parse(text);

                if (TryExtractError(doc.RootElement, out var e) && !string.IsNullOrWhiteSpace(e))
                    err = e;

                if (TryExtractFinalContent(doc.RootElement, out var content) && !string.IsNullOrWhiteSpace(content))
                    final = content;
            }
            catch
            {
                // Fall back to raw text.
            }

            if (!string.IsNullOrWhiteSpace(err))
                throw new HttpRequestException(err);

            if (!string.IsNullOrWhiteSpace(final))
            {
                yield return final;
                yield break;
            }

            yield return text;
            yield break;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var sb = new StringBuilder();
        var sawAnyDelta = false;
        var sawAnyEvent = false;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                break;

            // Event boundary.
            if (line.Length == 0)
            {
                if (sb.Length == 0) continue;

                var data = sb.ToString().Trim();
                sb.Clear();

                if (data == "[DONE]") yield break;
                if (data.Length == 0) continue;

#if DEBUG
                if (!sawAnyEvent)
                {
                    sawAnyEvent = true;
                    Debug.WriteLine($"[{provider}] first_event t={sw.ElapsedMilliseconds}ms");
                }
#endif

                // Fast-path: skip parsing chunks that cannot contain output text.
                // This is important for reasoning models that stream lots of reasoning-only deltas.
                if (!data.Contains("\"content\"", StringComparison.Ordinal)
                    && !data.Contains("\"text\"", StringComparison.Ordinal)
                    && !data.Contains("\"message\"", StringComparison.Ordinal))
                {
                    continue;
                }

                string delta;
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    if (!TryExtractDelta(doc.RootElement, out delta))
                        continue;
                }
                catch
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(delta))
                {
#if DEBUG
                    if (!sawAnyDelta)
                    {
                        sawAnyDelta = true;
                        Debug.WriteLine($"[{provider}] first_delta t={sw.ElapsedMilliseconds}ms");
                    }
#endif
                    yield return delta;
                }

                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var part = line.Substring(5).Trim();
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(part);
            }
        }

        // Flush a trailing event (if any).
        if (sb.Length > 0)
        {
            var data = sb.ToString().Trim();
            if (data != "[DONE]" && data.Length > 0)
            {
                if (!data.Contains("\"content\"", StringComparison.Ordinal)
                    && !data.Contains("\"text\"", StringComparison.Ordinal)
                    && !data.Contains("\"message\"", StringComparison.Ordinal))
                {
                    yield break;
                }

                string delta = null;
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    if (TryExtractDelta(doc.RootElement, out var d) && !string.IsNullOrEmpty(d))
                        delta = d;
                }
                catch { }

                if (!string.IsNullOrEmpty(delta))
                {
#if DEBUG
                    if (!sawAnyDelta)
                    {
                        sawAnyDelta = true;
                        Debug.WriteLine($"[{provider}] first_delta t={sw.ElapsedMilliseconds}ms");
                    }
#endif
                    yield return delta;
                }
            }
        }
    }

    public async Task<(List<string> Longterm, List<string> Daily)> ExtractMemoryAsync(
        Provider provider,
        string transcript,
        string note,
        string model,
        CancellationToken ct)
    {
        var apiKey = await GetApiKeyAsync(provider);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var name = provider switch
            {
                Provider.NvidiaIntegrate => "NVIDIA",
                Provider.Inception => "Inception",
                _ => "Cerebras"
            };
            throw new InvalidOperationException($"Missing {name} API key (set in SecureStorage)");
        }

        if (string.IsNullOrWhiteSpace(model))
            model = provider switch
            {
                Provider.NvidiaIntegrate => DefaultNvidiaModel,
                Provider.Inception => DefaultInceptionModel,
                _ => DefaultCerebrasModel
            };

        var instruction = new StringBuilder();
        instruction.AppendLine("You are a memory curator for a chat assistant.");
        instruction.AppendLine("Extract durable user-specific facts, preferences, constraints, and decisions from the transcript.");
        instruction.AppendLine("Do NOT include secrets, API keys, tokens, passwords, or personal identifiers.");
        instruction.AppendLine("Think step-by-step internally, then output ONLY valid JSON with this exact shape:");
        instruction.AppendLine("{\"longterm\":[\"...\"],\"daily\":[\"...\"]}");
        instruction.AppendLine("Keep each item short (1 sentence). 0-8 items per array.");
        if (!string.IsNullOrWhiteSpace(note))
        {
            instruction.AppendLine();
            instruction.AppendLine("User note/instruction:");
            instruction.AppendLine(note.Trim());
        }

        var payload = new
        {
            model,
            stream = false,
            temperature = 0,
            max_tokens = 500,
            messages = new object[]
            {
                new { role = "system", content = instruction.ToString().Trim() },
                new { role = "user", content = (transcript ?? string.Empty).Trim() }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        var http = provider switch
        {
            Provider.NvidiaIntegrate => _nvidia,
            Provider.Inception => _inception,
            _ => _cerebras
        };
        var path = provider == Provider.NvidiaIntegrate ? "chat/completions" : "/v1/chat/completions";

        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized || resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                var storageKey = provider switch
                {
                    Provider.NvidiaIntegrate => NvidiaKeyStorageKey,
                    Provider.Inception => InceptionKeyStorageKey,
                    _ => CerebrasKeyStorageKey
                };
                SecureStorage.Remove(storageKey);
                throw new HttpRequestException("Unauthorized. API key cleared; please enter it again.");
            }

            throw new HttpRequestException(string.IsNullOrWhiteSpace(text) ? $"HTTP {(int)resp.StatusCode}" : text);
        }

        string contentText;
        try
        {
            using var doc = JsonDocument.Parse(text);
            contentText = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        }
        catch
        {
            throw new InvalidOperationException("Failed to parse model response");
        }

        RememberResult parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<RememberResult>(contentText.Trim());
        }
        catch
        {
            throw new InvalidOperationException("Model did not return valid JSON for /remember");
        }

        var longterm = parsed?.longterm?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList() ?? new List<string>();
        var daily = parsed?.daily?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList() ?? new List<string>();
        return (longterm, daily);
    }

    private static bool IsEventStream(string mediaType)
        => string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase);

    private static bool TryExtractDelta(JsonElement root, out string delta)
    {
        delta = string.Empty;

        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            return false;
        if (choices.GetArrayLength() == 0) return false;

        var choice0 = choices[0];

        // OpenAI-style streaming: choices[0].delta.content
        if (choice0.TryGetProperty("delta", out var d) && d.ValueKind == JsonValueKind.Object)
        {
            if (d.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
            {
                delta = c.GetString() ?? string.Empty;
                return true;
            }

            // Some variants use `text` instead of `content`.
            if (d.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
            {
                delta = t.GetString() ?? string.Empty;
                return true;
            }
        }

        // Non-streaming fallback (if upstream ever returns full message): choices[0].message.content
        if (choice0.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object)
        {
            if (m.TryGetProperty("content", out var c2) && c2.ValueKind == JsonValueKind.String)
            {
                delta = c2.GetString() ?? string.Empty;
                return true;
            }
        }

        // Some servers stream `text` directly on the choice.
        if (choice0.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.String)
        {
            delta = txt.GetString() ?? string.Empty;
            return true;
        }

        return false;
    }

    private static bool TryExtractFinalContent(JsonElement root, out string content)
    {
        content = string.Empty;
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            return false;
        if (choices.GetArrayLength() == 0) return false;

        var choice0 = choices[0];
        if (choice0.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object)
        {
            if (m.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
            {
                content = c.GetString() ?? string.Empty;
                return true;
            }
        }

        if (choice0.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
        {
            content = t.GetString() ?? string.Empty;
            return true;
        }

        return false;
    }

    private static bool TryExtractError(JsonElement root, out string message)
    {
        message = string.Empty;
        if (!root.TryGetProperty("error", out var err) || err.ValueKind != JsonValueKind.Object)
            return false;
        if (err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
        {
            message = m.GetString() ?? string.Empty;
            return true;
        }
        return false;
    }

    private sealed record RememberResult(List<string> longterm, List<string> daily);
}
