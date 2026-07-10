using System.Net.Http.Headers;
using System.Text.Json;

namespace TelegramGitHubBot.Services;

public sealed class GlitchTipIssue
{
    public string Project { get; set; } = "";
    public string Title { get; set; } = "";
    public int Count { get; set; }
    public int UserCount { get; set; }
}

/// <summary>
/// Клиент к GlitchTip (Sentry-совместимый API): активные (unresolved) ошибки по
/// проектам. Конфиг: GLITCHTIP_URL, GLITCHTIP_TOKEN, GLITCHTIP_ORG, GLITCHTIP_PROJECTS.
/// </summary>
public sealed class GlitchTipService
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _org;
    private readonly string? _token;
    private readonly string[] _projects;

    public bool IsConfigured => !string.IsNullOrEmpty(_token) && !string.IsNullOrEmpty(_baseUrl);
    public IReadOnlyList<string> Projects => _projects;

    public GlitchTipService(HttpClient http, IConfiguration cfg)
    {
        _http = http;
        _baseUrl = (cfg["GLITCHTIP_URL"] ?? "").TrimEnd('/');
        _org = cfg["GLITCHTIP_ORG"] ?? "goodluck";
        _token = cfg["GLITCHTIP_TOKEN"];
        _projects = (cfg["GLITCHTIP_PROJECTS"] ?? "backend")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Console.WriteLine($"🐞 GlitchTipService: {(IsConfigured ? $"{_baseUrl} org={_org} projects={string.Join(",", _projects)}" : "не настроен (нет GLITCHTIP_TOKEN)")}");
    }

    public async Task<List<GlitchTipIssue>> GetUnresolvedAsync(string project, int limit = 30)
    {
        var issues = new List<GlitchTipIssue>();
        if (!IsConfigured) return issues;

        var url = $"{_baseUrl}/api/0/projects/{_org}/{project}/issues/?query={Uri.EscapeDataString("is:unresolved")}&limit={limit}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"GlitchTip {(int)resp.StatusCode} {resp.StatusCode} ({project})");

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        using var doc = JsonDocument.Parse(bytes);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return issues;

        foreach (var it in doc.RootElement.EnumerateArray())
        {
            var meta = it.TryGetProperty("metadata", out var m) && m.ValueKind == JsonValueKind.Object ? m : default;
            var title = it.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()
                : meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("value", out var v) ? v.GetString() : "";
            issues.Add(new GlitchTipIssue
            {
                Project = project,
                Title = title ?? "",
                Count = it.TryGetProperty("count", out var c) ? ParseInt(c) : 0,
                UserCount = it.TryGetProperty("userCount", out var u) ? ParseInt(u) : 0,
            });
        }
        return issues;
    }

    /// <summary>Собирает unresolved-ошибки по всем настроенным проектам (best-effort).</summary>
    public async Task<List<GlitchTipIssue>> GetAllUnresolvedAsync(int perProject = 30)
    {
        var all = new List<GlitchTipIssue>();
        foreach (var p in _projects)
        {
            try { all.AddRange(await GetUnresolvedAsync(p, perProject)); }
            catch (Exception ex) { Console.WriteLine($"⚠️ GlitchTip {p}: {ex.Message}"); }
        }
        return all;
    }

    private static int ParseInt(JsonElement e)
        => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) ? n
         : e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), out var n2) ? n2 : 0;
}
