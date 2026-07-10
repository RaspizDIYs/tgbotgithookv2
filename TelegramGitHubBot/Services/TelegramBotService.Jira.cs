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
            var issues = await _jiraService.GetActiveIssuesAsync();
            var digest = _jiraService.BuildDigest(issues);
            await SendMessageWithBackButtonAsync(chatId, digest);
        }
        catch (Exception ex)
        {
            await SendMessageWithBackButtonAsync(chatId, $"❌ Ошибка Jira: {ex.Message}");
        }
    }
}
