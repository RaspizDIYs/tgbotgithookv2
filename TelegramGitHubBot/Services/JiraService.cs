using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TelegramGitHubBot.Services;

public sealed class JiraIssue
{
    public string Key { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Status { get; set; } = "";
    public string? AssigneeAccountId { get; set; }
    public string? AssigneeName { get; set; }
}

/// <summary>
/// Читает задачи из Jira Cloud (REST v3, Basic-auth email:api_token) и строит
/// дайджест канбана с разбивкой frontend/backend по исполнителям.
/// Конфиг: JIRA_BASE_URL, JIRA_EMAIL, JIRA_API_TOKEN, JIRA_PROJECT (=KAN),
/// JIRA_FRONTEND_USERS, JIRA_BACKEND_USERS (подстроки displayName через запятую).
/// </summary>
public sealed class JiraService
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _project;
    private readonly string? _auth; // base64(email:token)
    private readonly string[] _frontendUsers;
    private readonly string[] _backendUsers;

    public bool IsConfigured => _auth != null && !string.IsNullOrEmpty(_baseUrl);

    public JiraService(HttpClient http, IConfiguration cfg)
    {
        _http = http;
        _baseUrl = (cfg["JIRA_BASE_URL"] ?? "").TrimEnd('/');
        _project = cfg["JIRA_PROJECT"] ?? "KAN";
        var email = cfg["JIRA_EMAIL"];
        var token = cfg["JIRA_API_TOKEN"];
        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(token))
            _auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{token}"));
        _frontendUsers = ParseUsers(cfg["JIRA_FRONTEND_USERS"] ?? "Shpinat,Шпинат");
        _backendUsers = ParseUsers(cfg["JIRA_BACKEND_USERS"] ?? "Mejaikin,Межайкин");
        Console.WriteLine($"🗂 JiraService: {(IsConfigured ? $"{_baseUrl} project={_project}" : "не настроен (нет JIRA_EMAIL/JIRA_API_TOKEN)")}");
    }

    private static string[] ParseUsers(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public async Task<List<JiraIssue>> GetActiveIssuesAsync()
        => await SearchAsync($"project = {_project} AND statusCategory != Done ORDER BY created DESC");

    public async Task<List<JiraIssue>> SearchAsync(string jql)
    {
        var issues = new List<JiraIssue>();
        if (!IsConfigured) return issues;

        var url = $"{_baseUrl}/rest/api/3/search?jql={Uri.EscapeDataString(jql)}&fields=summary,status,assignee&maxResults=100";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", _auth);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"⚠️ Jira search failed: {(int)resp.StatusCode} {resp.StatusCode}");
            return issues;
        }

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        using var doc = JsonDocument.Parse(bytes);
        if (!doc.RootElement.TryGetProperty("issues", out var arr)) return issues;

        foreach (var it in arr.EnumerateArray())
        {
            var fields = it.GetProperty("fields");
            string? accId = null, name = null;
            if (fields.TryGetProperty("assignee", out var asg) && asg.ValueKind == JsonValueKind.Object)
            {
                accId = asg.TryGetProperty("accountId", out var a) ? a.GetString() : null;
                name = asg.TryGetProperty("displayName", out var d) ? d.GetString() : null;
            }
            issues.Add(new JiraIssue
            {
                Key = it.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "",
                Summary = fields.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "",
                Status = fields.TryGetProperty("status", out var st) && st.TryGetProperty("name", out var sn) ? sn.GetString() ?? "" : "",
                AssigneeAccountId = accId,
                AssigneeName = name,
            });
        }
        return issues;
    }

    public string RoleOf(JiraIssue issue)
    {
        var who = issue.AssigneeName ?? "";
        if (_frontendUsers.Any(u => who.Contains(u, StringComparison.OrdinalIgnoreCase))) return "frontend";
        if (_backendUsers.Any(u => who.Contains(u, StringComparison.OrdinalIgnoreCase))) return "backend";
        return "other";
    }

    /// <summary>Строит текст дайджеста (Markdown) по списку задач.</summary>
    public string BuildDigest(List<JiraIssue> issues)
    {
        if (!IsConfigured)
            return "🗂 Jira не настроена (задайте JIRA_EMAIL / JIRA_API_TOKEN).";
        if (issues.Count == 0)
            return $"🗂 *{_project}* — активных задач нет 🎉";

        var frontend = issues.Where(i => RoleOf(i) == "frontend").ToList();
        var backend = issues.Where(i => RoleOf(i) == "backend").ToList();
        var other = issues.Where(i => RoleOf(i) == "other").ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"🗂 *{_project}* — активных задач: *{issues.Count}*");
        sb.AppendLine();
        AppendGroup(sb, "🖥 Frontend", _frontendUsers.FirstOrDefault(), frontend);
        AppendGroup(sb, "⚙️ Backend", _backendUsers.FirstOrDefault(), backend);
        if (other.Count > 0)
            AppendGroup(sb, "❔ Прочее/без исполнителя", null, other);
        return sb.ToString().TrimEnd();
    }

    private static void AppendGroup(StringBuilder sb, string label, string? who, List<JiraIssue> group)
    {
        var title = who != null ? $"{label} · {who}" : label;
        sb.AppendLine($"{title} — *{group.Count}*");
        if (group.Count > 0)
            sb.AppendLine(string.Join(", ", group.Select(i => i.Key)));
        sb.AppendLine();
    }
}
