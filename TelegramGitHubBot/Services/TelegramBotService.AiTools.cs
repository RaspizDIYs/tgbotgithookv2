using System.Text;
using System.Text.Json;
using Telegram.Bot;

namespace TelegramGitHubBot.Services;

// Агентный слой для /ask: LLM может вызывать собственные read-only «инструменты»
// бота (данные GitHub и статистики), а затем суммаризовать результат.
//
// Реализация провайдер-агностична: планировщик и финальный ответ идут через
// _geminiManager (Gemini или Ollama по LLM_PROVIDER), протоколы провайдеров не
// трогаются. Модель на шаге планирования возвращает JSON {tool,args}; если это
// не JSON или tool=null — цикл завершается и бот отвечает обычным текстом.
public partial class TelegramBotService
{
    private const int MaxAgenticSteps = 3;

    // Контекст команды: без него модель принимает «Шпинат» за овощ и т.п.
    private const string TeamContext =
        "Контекст: проект goodluckv2. «Шпинат» (Shpinat) — фронтенд-разработчик, " +
        "«Межайкин» (Mejaikin) — бэкенд-разработчик. Это НИКНЕЙМЫ ЛЮДЕЙ, а не еда/овощи. " +
        "Вопросы про «задачи Шпината/Межайкина/фронта/бэка» — это задачи из Jira (KAN): " +
        "бери их через get_jira_issues и раздели по смыслу (frontend/backend).";

    private const string ToolCatalog = @"- get_recent_commits(branch?: string, count?: int=10) — последние коммиты. НЕ указывай branch, если пользователь явно не назвал ветку — бот сам возьмёт дефолтную (master).
- get_repo_status() — общий статус репозитория
- get_branches() — список веток
- get_pull_requests() — открытые pull request'ы
- get_ci_status(branch?: string) — статусы CI/CD (workflow runs)
- get_active_authors(days?: int=30) — активные авторы за период
- search_commits(query: string, limit?: int=10) — поиск по коммитам
- get_leaderboard() — таблица лидеров по коммитам/стрикам
- get_jira_issues() — активные задачи Jira (KAN) с исполнителями и статусами";

    private async Task RunAgenticAskAsync(long chatId, string question)
    {
        var collected = new StringBuilder();

        for (var step = 0; step < MaxAgenticSteps; step++)
        {
            var planner = BuildPlannerPrompt(question, collected.ToString());
            var raw = await _geminiManager.GenerateRawResponseAsync(planner);
            var (tool, args) = ParseToolCall(raw);
            if (string.IsNullOrEmpty(tool)) break;

            var result = await InvokeToolAsync(tool!, args);
            collected.AppendLine($"### Инструмент {tool}:");
            collected.AppendLine(result);
            collected.AppendLine();
        }

        string answer;
        if (collected.Length == 0)
        {
            // Инструменты не понадобились — прямой ответ, но через guard-путь
            // (русский, без дрейфа) и с контекстом команды.
            var directPrompt = $"{TeamContext}\n\nВопрос: \"{question}\"\n\nОтветь кратко на русском по существу.";
            answer = await _geminiManager.GenerateRawResponseAsync(directPrompt);
        }
        else
        {
            var finalPrompt =
                $"{TeamContext}\n\n" +
                $"Пользователь спросил: \"{question}\"\n\n" +
                $"Данные, собранные инструментами бота:\n{collected}\n" +
                "Ответь пользователю по-русски обычным текстом (можно списком). " +
                "НЕ возвращай JSON и не дублируй сырые данные — сформулируй человекочитаемый ответ. " +
                "Для КОММИТОВ каждый пункт: «`короткий_sha` — первая строка сообщения — автор». " +
                "Для ЗАДАЧ Jira каждый пункт: «KAN-NN — описание (статус)»; если спрашивают про задачи " +
                "конкретного человека/зоны (Шпинат=фронтенд, Межайкин=бэкенд) — отбери ТОЛЬКО его задачи. " +
                "Не выдумывай того, чего нет в данных. " +
                "Если в данных есть строка ошибки (начинается с ❌) — сообщи точную причину дословно, не смягчай.";
            answer = await _geminiManager.GenerateRawResponseAsync(finalPrompt);
        }

        answer = System.Text.RegularExpressions.Regex.Replace(answer, @"\[GIF:[^\]]*\]", "").Trim();
        if (string.IsNullOrWhiteSpace(answer)) answer = "Не удалось сформировать ответ.";
        await _botClient.SendTextMessageAsync(chatId, answer, disableNotification: true);
    }

    private static string BuildPlannerPrompt(string question, string collectedSoFar)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ты — планировщик агента в Telegram-боте по GitHub-репозиторию. Доступные инструменты (только чтение):");
        sb.AppendLine(ToolCatalog);
        sb.AppendLine();
        sb.AppendLine(TeamContext);
        sb.AppendLine();
        sb.AppendLine($"Вопрос пользователя: \"{question}\"");
        if (!string.IsNullOrWhiteSpace(collectedSoFar))
        {
            sb.AppendLine();
            sb.AppendLine("Уже собранные данные:");
            sb.AppendLine(collectedSoFar);
        }
        sb.AppendLine();
        sb.AppendLine("Если нужен инструмент — верни СТРОГО JSON без пояснений и без markdown: {\"tool\":\"имя\",\"args\":{...}}.");
        sb.AppendLine("Если данных достаточно или инструмент не нужен — верни: {\"tool\":null}.");
        return sb.ToString();
    }

    private static (string? tool, JsonElement args) ParseToolCall(string raw)
    {
        try
        {
            var json = ExtractJsonObject(raw);
            if (json == null) return (null, default);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tool", out var t) || t.ValueKind == JsonValueKind.Null)
                return (null, default);

            var name = t.GetString();
            var args = root.TryGetProperty("args", out var a) ? a.Clone() : default;
            return (name, args);
        }
        catch
        {
            return (null, default);
        }
    }

    private static string? ExtractJsonObject(string s)
    {
        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        return (start < 0 || end <= start) ? null : s.Substring(start, end - start + 1);
    }

    private static string ArgStr(JsonElement args, string key, string def = "")
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(key, out var v))
        {
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? def;
            if (v.ValueKind == JsonValueKind.Number) return v.ToString();
        }
        return def;
    }

    private static int ArgInt(JsonElement args, string key, int def)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(key, out var v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
            if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var n2)) return n2;
        }
        return def;
    }

    // Дефолтная ветка репозитория (goodluckv2 — это master, а не main).
    // Резолвим реальную; при неудаче берём первую доступную ветку, и лишь затем master.
    private async Task<string> ResolveDefaultBranchAsync()
    {
        var def = await _gitHubService.TryGetDefaultBranchAsync();
        if (!string.IsNullOrWhiteSpace(def)) return def!;
        try
        {
            var branches = await _gitHubService.GetBranchesListAsync();
            if (branches.Count > 0)
                return branches.Contains("master") ? "master" : branches.Contains("main") ? "main" : branches[0];
        }
        catch { }
        return "master";
    }

    private async Task<string> InvokeToolAsync(string tool, JsonElement args)
    {
        try
        {
            switch (tool.ToLowerInvariant())
            {
                case "get_recent_commits":
                {
                    var branch = ArgStr(args, "branch");
                    if (string.IsNullOrWhiteSpace(branch))
                        branch = await ResolveDefaultBranchAsync();
                    var count = Math.Clamp(ArgInt(args, "count", 10), 1, 30);
                    return await _gitHubService.GetRecentCommitsAsync(branch, count);
                }
                case "get_repo_status":
                    return await _gitHubService.GetRepositoryStatusAsync();
                case "get_branches":
                    return string.Join(", ", await _gitHubService.GetBranchesListAsync());
                case "get_pull_requests":
                    return await _gitHubService.GetPullRequestsAsync();
                case "get_ci_status":
                {
                    var branch = ArgStr(args, "branch");
                    return await _gitHubService.GetWorkflowRunsAsync(string.IsNullOrWhiteSpace(branch) ? null : branch, 10);
                }
                case "get_active_authors":
                    return await _gitHubService.GetActiveAuthorsAsync(Math.Clamp(ArgInt(args, "days", 30), 1, 365));
                case "search_commits":
                {
                    var q = ArgStr(args, "query");
                    if (string.IsNullOrWhiteSpace(q)) return "Ошибка: не задан query для search_commits.";
                    return await _gitHubService.SearchCommitsAsync(q, Math.Clamp(ArgInt(args, "limit", 10), 1, 30));
                }
                case "get_leaderboard":
                    return _achievementService.GetLeaderboard();
                case "get_jira_issues":
                {
                    if (!_jiraService.IsConfigured) return "Jira не настроена (нет JIRA_EMAIL/JIRA_API_TOKEN).";
                    var issues = await _jiraService.GetActiveIssuesAsync();
                    if (issues.Count == 0) return "Активных задач в Jira нет.";
                    return string.Join("\n", issues.Select(i =>
                        $"{i.Key} [{i.Status}] ({(_jiraService.RoleOf(i) == "frontend" ? "frontend" : _jiraService.RoleOf(i) == "backend" ? "backend" : "—")}, {i.AssigneeName ?? "без исполнителя"}): {i.Summary}"));
                }
                default:
                    return $"Неизвестный инструмент: {tool}";
            }
        }
        catch (Exception ex)
        {
            return $"Ошибка инструмента {tool}: {ex.Message}";
        }
    }
}
