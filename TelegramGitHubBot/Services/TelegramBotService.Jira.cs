using System.Text;
using System.Text.Json;
using Telegram.Bot;

namespace TelegramGitHubBot.Services;

public partial class TelegramBotService
{
    private async Task HandleJiraDigestAsync(long chatId)
    {
        if (!_jiraService.IsConfigured)
        {
            await SendMessageWithBackButtonAsync(chatId,
                "🗂 Jira не настроена.\nЗадайте переменные окружения: `JIRA_BASE_URL`, `JIRA_EMAIL`, `JIRA_API_TOKEN`.");
            return;
        }

        try
        {
            var digest = await BuildJiraDigestAsync();
            await SendMessageWithBackButtonAsync(chatId, digest);
        }
        catch (Exception ex)
        {
            await SendMessageWithBackButtonAsync(chatId, $"❌ Ошибка Jira: {ex.Message}");
        }
    }

    /// <summary>
    /// Собирает дайджест активных задач с разбивкой frontend/backend.
    /// Роль берётся сперва из исполнителя; для задач без исполнителя (в KAN их
    /// обычно нет) — классифицируется по смыслу summary через LLM.
    /// </summary>
    public async Task<string> BuildJiraDigestAsync()
    {
        var issues = await _jiraService.GetActiveIssuesAsync();
        if (issues.Count == 0)
            return $"🗂 *{_jiraService.ProjectKey}* — активных задач нет 🎉";

        // Роль по исполнителю, где он есть.
        var roles = new Dictionary<string, string>();
        var needClassify = new List<JiraIssue>();
        foreach (var i in issues)
        {
            var r = _jiraService.RoleOf(i);
            if (r == "other") needClassify.Add(i);
            else roles[i.Key] = r;
        }

        // Остальное — по смыслу через LLM (одним запросом).
        if (needClassify.Count > 0)
        {
            var byLlm = await ClassifyIssuesByLlmAsync(needClassify);
            foreach (var i in needClassify)
                roles[i.Key] = byLlm.TryGetValue(i.Key, out var r) ? r : "other";
        }

        var frontend = issues.Where(i => roles.GetValueOrDefault(i.Key) == "frontend").ToList();
        var backend = issues.Where(i => roles.GetValueOrDefault(i.Key) == "backend").ToList();
        var other = issues.Where(i => roles.GetValueOrDefault(i.Key) is not ("frontend" or "backend")).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"🗂 *{_jiraService.ProjectKey}* — активных задач: *{issues.Count}*");
        sb.AppendLine();
        AppendJiraGroup(sb, $"🖥 Frontend · {_jiraService.FrontendLabel}", frontend);
        AppendJiraGroup(sb, $"⚙️ Backend · {_jiraService.BackendLabel}", backend);
        if (other.Count > 0)
            AppendJiraGroup(sb, "❔ Не удалось определить", other);
        return sb.ToString().TrimEnd();
    }

    private static void AppendJiraGroup(StringBuilder sb, string label, List<JiraIssue> group)
    {
        sb.AppendLine($"{label} — *{group.Count}*");
        if (group.Count > 0)
            sb.AppendLine(string.Join(", ", group.Select(i => i.Key)));
        sb.AppendLine();
    }

    /// <summary>Классифицирует задачи (frontend/backend/other) по смыслу summary через LLM. Одним запросом.</summary>
    private async Task<Dictionary<string, string>> ClassifyIssuesByLlmAsync(List<JiraIssue> issues)
    {
        var result = new Dictionary<string, string>();
        try
        {
            var list = string.Join("\n", issues.Select(i => $"{i.Key}: {i.Summary}"));
            var prompt =
                "Классифицируй каждую задачу по зоне разработки: \"frontend\" (UI, React/Next, клиент, вёрстка) " +
                "или \"backend\" (C#/.NET, API, БД, миграции, сущности, сервисы). Если не ясно — \"other\".\n" +
                "Верни ТОЛЬКО JSON вида {\"KAN-1\":\"backend\",\"KAN-2\":\"frontend\"} без пояснений и без markdown.\n\n" +
                "Задачи:\n" + list;

            var raw = await _geminiManager.GenerateRawResponseAsync(prompt);
            var json = ExtractJsonObject(raw);
            if (json == null) return result;

            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var role = (prop.Value.GetString() ?? "other").Trim().ToLowerInvariant();
                result[prop.Name] = role is "frontend" or "backend" ? role : "other";
            }
        }
        catch
        {
            // При сбое LLM — оставляем задачи неклассифицированными (попадут в «Не удалось определить»).
        }
        return result;
    }
}
