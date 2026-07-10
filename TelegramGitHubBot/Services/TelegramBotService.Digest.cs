using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace TelegramGitHubBot.Services;

// Персональный утренний дайджест (порт джобы automation/morning_digest.py из
// goodluckv2): собирает Jira-срезы + активные ошибки GlitchTip, а LLM пишет
// тёплое приветствие и делит план на «Для <фронт>» / «Для <бэк>» по смыслу.
public partial class TelegramBotService
{
    private async Task HandleDigestCommandAsync(long chatId)
    {
        if (!_jiraService.IsConfigured)
        {
            await SendMessageWithBackButtonAsync(chatId,
                "🗂 Jira не настроена — дайджест недоступен. Задайте JIRA_EMAIL/JIRA_API_TOKEN.");
            return;
        }
        try
        {
            var digest = await BuildMorningDigestAsync();
            await SendMessageWithBackButtonAsync(chatId, digest);
        }
        catch (Exception ex)
        {
            await SendMessageWithBackButtonAsync(chatId, $"❌ Ошибка дайджеста: {ex.Message}");
        }
    }

    /// <summary>Собирает данные и просит LLM написать персональный утренний дайджест.</summary>
    public async Task<string> BuildMorningDigestAsync()
    {
        var raw = await CollectDigestDataAsync();
        var today = DateTime.UtcNow.AddHours(3).ToString("yyyy-MM-dd"); // МСК
        var prompt = DigestSystemPrompt() + "\n\nДанные:\n" + raw;
        var body = await _geminiManager.GenerateRawResponseAsync(prompt);
        body = System.Text.RegularExpressions.Regex.Replace(body, @"\[GIF:[^\]]*\]", "").Trim();
        if (string.IsNullOrWhiteSpace(body)) body = "Данных для дайджеста недостаточно.";
        return $"☀️ *Утренний дайджест* ({today})\n\n{body}";
    }

    private string DigestSystemPrompt()
    {
        var fe = _jiraService.FrontendLabel;
        var be = _jiraService.BackendLabel;
        return
            "Ты — помощник команды разработки. Составь тёплый персональный утренний дайджест-план на русском по данным ниже. " +
            $"Команда: {fe} — фронтенд-разработчик; {be} — бэкенд-разработчик (это ники людей, не еда). " +
            "Начни с ОДНОЙ короткой дружелюбной строки-приветствия/пожелания на день (по-доброму, без пафоса). " +
            $"Затем раздели план на персональные секции: «👤 Для {fe}» — фронтенд-задачи (UI, React/Next, клиент, вёрстка) и " +
            $"«👤 Для {be}» — бэкенд-задачи (C#/.NET, API, БД, миграции, сущности, сервисы). " +
            "Распределяй задачи ПО СМЫСЛУ их описания; ошибки GlitchTip относятся к бэкенду. " +
            "Спорные/общие задачи вынеси в короткую секцию «Общее». " +
            "Внутри секций по возможности отметь: на чём продолжить (в работе), что срочно (дедлайны/ошибки), что взять из бэклога. " +
            "Пиши буллетами, по делу, без воды и без выдумывания фактов. " +
            "Закрытые за сутки — одной короткой строкой-итогом (сколько), не перечисляй. Если данных мало — так и скажи одной строкой.";
    }

    private async Task<string> CollectDigestDataAsync()
    {
        var parts = new List<string>();

        // Активные ошибки GlitchTip
        if (_glitchTipService.IsConfigured)
        {
            try
            {
                var gt = await _glitchTipService.GetAllUnresolvedAsync(30);
                var lines = gt.Take(15).Select(i => $"[{i.Project}] {i.Title} — событий {i.Count}, пользователей {i.UserCount}").ToList();
                parts.Add("Активные ошибки GlitchTip:\n" + (lines.Count > 0 ? string.Join("\n", lines) : "нет"));
            }
            catch (Exception ex)
            {
                parts.Add($"Активные ошибки GlitchTip:\nошибка чтения: {ex.Message}");
            }
        }

        // Jira-срезы (порт из morning_digest.py)
        await AddSectionAsync(parts, "В работе сейчас", "statusCategory = \"In Progress\"");
        await AddSectionAsync(parts, "Новые задачи за сутки", "created >= -1d");
        await AddSectionAsync(parts, "Горит (просрочено / дедлайн сегодня)", "duedate <= endOfDay() AND statusCategory != Done");
        await AddSectionAsync(parts, "Бэклог — что взять следующим", "statusCategory = \"To Do\"", 10);

        try
        {
            var closed = await _jiraService.CountAsync("statusCategory = Done AND updated >= -1d");
            parts.Add($"Закрыто за сутки: {closed}");
        }
        catch (Exception ex)
        {
            parts.Add($"Закрыто за сутки: не удалось посчитать ({ex.Message})");
        }

        return string.Join("\n\n", parts);
    }

    private async Task AddSectionAsync(List<string> parts, string title, string jqlExtra, int limit = 20)
    {
        try
        {
            var issues = await _jiraService.SearchSectionAsync(jqlExtra, limit);
            var lines = issues.Select(i =>
            {
                var due = string.IsNullOrEmpty(i.DueDate) ? "" : $" (дедлайн {i.DueDate})";
                return $"{i.Key} [{i.Status}] {i.Summary}{due}";
            }).ToList();
            parts.Add($"{title}:\n" + (lines.Count > 0 ? string.Join("\n", lines) : "нет"));
        }
        catch (Exception ex)
        {
            parts.Add($"{title}:\nошибка чтения Jira: {ex.Message}");
        }
    }
}
