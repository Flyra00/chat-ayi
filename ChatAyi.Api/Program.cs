using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ChatAyi.Api.Memory;
using ChatAyi.Api.Sessions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("upstream");
builder.Services.AddSingleton<MemoryIndex>();
builder.Services.AddSingleton<SessionStore>();

var app = builder.Build();

app.MapGet("/", (IConfiguration config) => Results.Json(new
{
    ok = true,
    service = "ChatAyi.Api",
    hasCerebrasKey = !string.IsNullOrWhiteSpace(config["CEREBRAS_API_KEY"]),
    build = "json-v2",
    cerebrasApiUrl = config["CEREBRAS_API_URL"] ?? config["Cerebras:ApiUrl"] ?? "https://api.cerebras.ai/v1/chat/completions",
    defaultModel = config["CEREBRAS_MODEL"] ?? config["Cerebras:DefaultModel"] ?? string.Empty,
    endpoints = new[]
    {
        "POST /api/chat/completions",
        "GET /api/models",
        "GET /api/memory/search?q=...",
        "POST /api/memory/append",
        "POST /api/memory/reload"
    }
}));

app.MapGet("/api/memory/search", async (HttpContext ctx, MemoryIndex memory) =>
{
    var q = ctx.Request.Query["q"].ToString();
    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest(new { error = new { message = "Missing query parameter 'q'" } });

    var result = await memory.SearchAsync(q, ctx.RequestAborted);
    return Results.Ok(result);
});

app.MapPost("/api/memory/append", async (HttpContext ctx, MemoryIndex memory) =>
{
    var ct = ctx.RequestAborted;

    MemoryAppendRequest? req;
    try
    {
        req = await ctx.Request.ReadFromJsonAsync<MemoryAppendRequest>(cancellationToken: ct);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            error = new
            {
                message = app.Environment.IsDevelopment()
                    ? $"Invalid JSON body: {ex.Message}"
                    : "Invalid JSON body"
            }
        });
    }

    if (req is null || string.IsNullOrWhiteSpace(req.Text))
        return Results.BadRequest(new { error = new { message = "Missing 'text'" } });

    var target = (req.Target ?? "daily").Trim().ToLowerInvariant();
    var text = req.Text.Trim();
    if (text.Length > 20_000)
        return Results.BadRequest(new { error = new { message = "Text too long" } });

    DateOnly? date = null;
    if (!string.IsNullOrWhiteSpace(req.Date) && DateOnly.TryParse(req.Date, out var parsed))
        date = parsed;

    string path;
    if (target is "longterm" or "long" or "memory")
        path = await memory.AppendLongTermAsync(text, ct);
    else
        path = await memory.AppendDailyAsync(text, date, ct);

    return Results.Ok(new { ok = true, target, path });
});

app.MapPost("/api/memory/remember", async (HttpContext ctx, IConfiguration config, IHttpClientFactory httpFactory, MemoryIndex memory, SessionStore sessions) =>
{
    var ct = ctx.RequestAborted;
    var sessionId = sessions.GetSessionId(ctx);

    var apiKey = config["CEREBRAS_API_KEY"];
    if (string.IsNullOrWhiteSpace(apiKey))
        return Results.Problem("Missing CEREBRAS_API_KEY on server", statusCode: 500);

    var upstreamUrl = config["CEREBRAS_API_URL"]
        ?? config["Cerebras:ApiUrl"]
        ?? "https://api.cerebras.ai/v1/chat/completions";

    RememberRequest? body;
    try
    {
        body = await ctx.Request.ReadFromJsonAsync<RememberRequest>(cancellationToken: ct);
    }
    catch
    {
        body = null;
    }

    var target = (body?.Target ?? "both").Trim().ToLowerInvariant();
    var note = (body?.Note ?? string.Empty).Trim();

    var recent = await sessions.ReadRecentChatAsync(sessionId, maxMessages: 30, ct);
    if (recent.Count == 0)
        return Results.BadRequest(new { error = new { message = "No session transcript found yet" } });

    var transcript = new StringBuilder();
    foreach (var (role, content) in recent)
    {
        transcript.Append(role.ToUpperInvariant());
        transcript.Append(": ");
        transcript.AppendLine(content.Replace("\r\n", "\n").Replace("\r", "\n"));
        transcript.AppendLine();
    }

    var model = config["CEREBRAS_MODEL"] ?? config["Cerebras:DefaultModel"] ?? "gpt-oss-120b";

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
        instruction.AppendLine(note);
    }

    var upstreamReq = new HttpRequestMessage(HttpMethod.Post, upstreamUrl)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            model,
            stream = false,
            temperature = 0,
            max_tokens = 500,
            messages = new object[]
            {
                new { role = "system", content = instruction.ToString().Trim() },
                new { role = "user", content = transcript.ToString().Trim() }
            }
        }), Encoding.UTF8, "application/json")
    };
    upstreamReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    var client = httpFactory.CreateClient("upstream");
    using var upstream = await client.SendAsync(upstreamReq, HttpCompletionOption.ResponseHeadersRead, ct);
    var upstreamText = await upstream.Content.ReadAsStringAsync(ct);
    if (!upstream.IsSuccessStatusCode)
        return Results.Content(upstreamText, "application/json", Encoding.UTF8, (int)upstream.StatusCode);

    string jsonText;
    try
    {
        using var doc = JsonDocument.Parse(upstreamText);
        var root = doc.RootElement;
        var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        jsonText = (content ?? string.Empty).Trim();
    }
    catch
    {
        return Results.BadRequest(new { error = new { message = "Failed to parse upstream response" } });
    }

    RememberResult? extracted;
    try
    {
        extracted = JsonSerializer.Deserialize<RememberResult>(jsonText);
    }
    catch
    {
        return Results.BadRequest(new { error = new { message = "Model did not return valid JSON" }, raw = jsonText });
    }

    var longterm = extracted?.longterm?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList() ?? new List<string>();
    var daily = extracted?.daily?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList() ?? new List<string>();

    static bool LooksLikeSecret(string s)
    {
        var t = s.ToLowerInvariant();
        if (t.Contains("api key") || t.Contains("apikey") || t.Contains("token") || t.Contains("password")) return true;
        if (t.Contains("csk-") || t.Contains("sk-") || t.Contains("bearer ")) return true;
        return false;
    }

    longterm = longterm.Where(x => !LooksLikeSecret(x)).Take(8).ToList();
    daily = daily.Where(x => !LooksLikeSecret(x)).Take(8).ToList();

    var wroteLong = 0;
    var wroteDaily = 0;

    if (target is "both" or "all" or "*" or "longterm" or "long" or "memory")
    {
        if (target is "longterm" or "long" or "memory")
            daily.Clear();

        await memory.AppendLongTermManyAsync(longterm, ct);
        wroteLong = longterm.Count;
    }

    if (target is "both" or "all" or "*" or "daily")
    {
        if (target is "daily")
            longterm.Clear();

        await memory.AppendDailyManyAsync(daily, date: null, ct);
        wroteDaily = daily.Count;
    }

    await sessions.AppendAsync(sessionId, new
    {
        ts = DateTimeOffset.UtcNow,
        role = "system",
        content = $"/remember wrote longterm={wroteLong} daily={wroteDaily}"
    }, ct);

    return Results.Ok(new
    {
        ok = true,
        sessionId,
        wrote = new { longterm = wroteLong, daily = wroteDaily },
        extracted = new { longterm, daily }
    });
});

app.MapPost("/api/chat/completions", async (HttpContext ctx, IConfiguration config, IHttpClientFactory httpFactory, MemoryIndex memory, SessionStore sessions) =>
{
    var ct = ctx.RequestAborted;
    var sessionId = sessions.GetSessionId(ctx);

    var apiKey = config["CEREBRAS_API_KEY"];
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await ctx.Response.WriteAsJsonAsync(new { error = new { message = "Missing CEREBRAS_API_KEY on server" } }, ct);
        return;
    }

    var upstreamUrl = config["CEREBRAS_API_URL"]
        ?? config["Cerebras:ApiUrl"]
        ?? "https://api.cerebras.ai/v1/chat/completions";

    JsonObject? obj;
    try
    {
        obj = await ctx.Request.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
    }
    catch (Exception ex)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new
        {
            error = new
            {
                message = app.Environment.IsDevelopment()
                    ? $"Invalid JSON body: {ex.Message}"
                    : "Invalid JSON body"
            }
        }, ct);
        return;
    }

    if (obj is null)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new { error = new { message = "JSON body must be an object" } }, ct);
        return;
    }

    // Ensure model exists.
    var model = obj["model"]?.GetValue<string>()?.Trim();
    if (string.IsNullOrWhiteSpace(model))
    {
        var defaultModel = config["CEREBRAS_MODEL"] ?? config["Cerebras:DefaultModel"];
        if (string.IsNullOrWhiteSpace(defaultModel))
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new { error = new { message = "Missing 'model'. Set in request body or server env CEREBRAS_MODEL" } }, ct);
            return;
        }
        obj["model"] = defaultModel;
    }

    // Ensure streaming.
    obj["stream"] = true;

    // Read messages.
    var messages = obj["messages"] as JsonArray ?? new JsonArray();
    obj["messages"] = messages;

    // Find last user content for retrieval.
    string? lastUser = null;
    for (var i = messages.Count - 1; i >= 0; i--)
    {
        if (messages[i] is not JsonObject m) continue;
        var role = m["role"]?.GetValue<string>();
        if (!string.Equals(role, "user", StringComparison.Ordinal)) continue;
        var content = m["content"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(content)) { lastUser = content; break; }
    }

    if (!string.IsNullOrWhiteSpace(lastUser))
    {
        await sessions.AppendAsync(sessionId, new
        {
            ts = DateTimeOffset.UtcNow,
            role = "user",
            content = lastUser,
            model = obj["model"]?.GetValue<string>() ?? string.Empty
        }, ct);

        var memoryContext = await memory.GetContextAsync(lastUser, ct);
        if (!string.IsNullOrWhiteSpace(memoryContext))
        {
            messages.Insert(0, new JsonObject
            {
                ["role"] = "system",
                ["content"] =
                    "Use the following knowledge base excerpts when helpful. " +
                    "If they are not relevant, ignore them.\n\n" +
                    memoryContext
            });
        }
    }

    var body = obj.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

    var req = new HttpRequestMessage(HttpMethod.Post, upstreamUrl)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    var client = httpFactory.CreateClient("upstream");
    using var upstream = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

    ctx.Response.StatusCode = (int)upstream.StatusCode;
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";

    var contentType = upstream.Content.Headers.ContentType?.ToString();
    ctx.Response.ContentType = string.IsNullOrWhiteSpace(contentType) ? "text/event-stream" : contentType;

    if (!upstream.IsSuccessStatusCode)
    {
        var errorText = await upstream.Content.ReadAsStringAsync(ct);
        if (!string.IsNullOrWhiteSpace(errorText))
            await ctx.Response.WriteAsync(errorText, ct);

        await sessions.AppendAsync(sessionId, new
        {
            ts = DateTimeOffset.UtcNow,
            role = "error",
            status = (int)upstream.StatusCode,
            content = errorText
        }, ct);

        return;
    }

    static bool TryExtractDeltaContent(JsonElement root, out string delta)
    {
        delta = string.Empty;

        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            return false;
        if (choices.GetArrayLength() == 0) return false;

        var choice0 = choices[0];

        if (choice0.TryGetProperty("delta", out var d) && d.ValueKind == JsonValueKind.Object)
        {
            if (d.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
            {
                delta = c.GetString() ?? string.Empty;
                return true;
            }
            if (d.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
            {
                delta = t.GetString() ?? string.Empty;
                return true;
            }
        }

        if (choice0.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.String)
        {
            delta = txt.GetString() ?? string.Empty;
            return true;
        }

        return false;
    }

    var assistant = new StringBuilder();
    await using var upstreamStream = await upstream.Content.ReadAsStreamAsync(ct);
    using var reader = new StreamReader(upstreamStream);

    while (!reader.EndOfStream && !ct.IsCancellationRequested)
    {
        var line = await reader.ReadLineAsync(ct);
        if (line is null) break;

        await ctx.Response.WriteAsync(line + "\n", ct);

        // Flush per SSE event boundary.
        if (line.Length == 0)
        {
            await ctx.Response.Body.FlushAsync(ct);
            continue;
        }

        if (!line.StartsWith("data:", StringComparison.Ordinal))
            continue;

        var data = line.Substring(5).Trim();
        if (data == "[DONE]")
        {
            await ctx.Response.Body.FlushAsync(ct);
            break;
        }
        if (data.Length == 0) continue;

        try
        {
            using var doc = JsonDocument.Parse(data);
            if (TryExtractDeltaContent(doc.RootElement, out var delta) && !string.IsNullOrEmpty(delta))
                assistant.Append(delta);
        }
        catch
        {
            // Ignore malformed event.
        }
    }

    if (assistant.Length > 0)
    {
        await sessions.AppendAsync(sessionId, new
        {
            ts = DateTimeOffset.UtcNow,
            role = "assistant",
            content = assistant.ToString(),
            model = obj["model"]?.GetValue<string>() ?? string.Empty
        }, ct);
    }
});

app.MapPost("/api/memory/reload", async (MemoryIndex memory, CancellationToken ct) =>
{
    await memory.ReloadAsync(ct);
    return Results.Ok(new { ok = true });
});

app.MapGet("/api/models", async (HttpContext ctx, IConfiguration config, IHttpClientFactory httpFactory) =>
{
    var ct = ctx.RequestAborted;
    var apiKey = config["CEREBRAS_API_KEY"];
    if (string.IsNullOrWhiteSpace(apiKey))
        return Results.Problem("Missing CEREBRAS_API_KEY on server", statusCode: 500);

    var upstreamUrl = config["CEREBRAS_API_URL"]
        ?? config["Cerebras:ApiUrl"]
        ?? "https://api.cerebras.ai/v1/chat/completions";

    var modelsUrl = new UriBuilder(new Uri(upstreamUrl)) { Path = "/v1/models", Query = string.Empty }.Uri;

    var req = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    var client = httpFactory.CreateClient("upstream");
    using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
    var text = await resp.Content.ReadAsStringAsync(ct);

    return Results.Content(text, "application/json", Encoding.UTF8, (int)resp.StatusCode);
});

app.Run();

internal sealed record MemoryAppendRequest(string? Target, string? Text, string? Date);
internal sealed record RememberRequest(string? Target, string? Note);
internal sealed record RememberResult(List<string>? longterm, List<string>? daily);
