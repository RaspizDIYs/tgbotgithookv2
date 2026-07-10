using Telegram.Bot;

namespace TelegramGitHubBot.Services;

// Порт джоб automation/stale_tasks.py и monthly_summary.py из goodluckv2:
// напоминание о зависших задачах и месячная сводка «что нового». Обе — Jira+LLM.
public partial class TelegramBotService
{
    private static int StaleDays =>
        int.TryParse(Environment.GetEnvironmentVariable("STALE_DAYS"), out var d) && d > 0 ? d : 7;

    // ── Зависшие задачи (KAN-57) ────────────────────────────────────────────
    public async Task<string> BuildStaleTasksAsync()
    {
        var days = StaleDays;
        var stale = await _jiraService.SearchSectionAsync($"statusCategory != Done AND updated <= -{days}d", 30);
        if (stale.Count == 0)
            return $"⏰ *Зависшие задачи*\n\nЗадач без движения >{days} дн. нет 👍";

        var lines = string.Join("\n", stale.Select(i => $"{i.Key} [{i.Status}] {i.Summary}"));
        var prompt =
            "Ты — помощник разработчика. По списку зависших задач составь короткое дружелюбное " +
            "напоминание на русском: что стоит подвигать в первую очередь. Буллетами, без воды, " +
            "не выдумывай ничего сверх списка.\n\n" +
            $"Задачи без движения более {days} дней:\n{lines}";
        var body = await GenerateReportBodyAsync(prompt);
        return $"⏰ *Зависшие задачи* (без движения >{days} дн.)\n\n{body}";
    }

    private async Task HandleStaleCommandAsync(long chatId)
    {
        if (!_jiraService.IsConfigured) { await SendMessageWithBackButtonAsync(chatId, "🗂 Jira не настроена."); return; }
        try { await SendMessageWithBackButtonAsync(chatId, await BuildStaleTasksAsync()); }
        catch (Exception ex) { await SendMessageWithBackButtonAsync(chatId, $"❌ Ошибка: {ex.Message}"); }
    }

    // ── Месячная сводка «что нового» ────────────────────────────────────────
    public async Task<string> BuildMonthlySummaryAsync()
    {
        var monthKey = DateTime.UtcNow.AddHours(3).ToString("yyyy-MM"); // МСК
        var done = await _jiraService.SearchSectionAsync("statusCategory = Done AND updated >= -31d", 50);
        if (done.Count == 0)
            return $"🗓 *Что нового за {monthKey}*\n\nЗа месяц закрытых задач нет.";

        var lines = string.Join("\n", done.Select(i => $"{i.Key}: {i.Summary}"));
        var prompt =
            "Ты — помощник команды разработки. Составь на русском краткую и понятную сводку " +
            "«что нового за месяц» по списку выполненных задач. Сгруппируй по смыслу, пиши для людей " +
            "(не технично), буллетами, без воды. Не выдумывай того, чего нет в списке.\n\n" +
            $"Выполненные задачи за месяц:\n{lines}";
        var body = await GenerateReportBodyAsync(prompt);
        return $"🗓 *Что нового за {monthKey}*\n\n{body}";
    }

    private async Task HandleMonthlyCommandAsync(long chatId)
    {
        if (!_jiraService.IsConfigured) { await SendMessageWithBackButtonAsync(chatId, "🗂 Jira не настроена."); return; }
        try { await SendMessageWithBackButtonAsync(chatId, await BuildMonthlySummaryAsync()); }
        catch (Exception ex) { await SendMessageWithBackButtonAsync(chatId, $"❌ Ошибка: {ex.Message}"); }
    }

    private async Task<string> GenerateReportBodyAsync(string prompt)
    {
        var body = await _geminiManager.GenerateRawResponseAsync(prompt);
        body = System.Text.RegularExpressions.Regex.Replace(body, @"\[GIF:[^\]]*\]", "").Trim();
        return string.IsNullOrWhiteSpace(body) ? "Не удалось сформировать текст." : body;
    }
}
