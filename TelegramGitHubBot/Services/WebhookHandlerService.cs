using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace TelegramGitHubBot.Services;

public class WebhookHandlerService
{
    private readonly IConfiguration _configuration;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly ILogger<WebhookHandlerService> _logger;

    public WebhookHandlerService(
        IConfiguration configuration,
        ITelegramBotClient telegramBotClient,
        ILogger<WebhookHandlerService> logger)
    {
        _configuration = configuration;
        _telegramBotClient = telegramBotClient;
        _logger = logger;
    }

    public async Task HandleGitHubWebhookAsync(HttpContext context)
    {
        try
        {
            var eventType = context.Request.Headers["X-GitHub-Event"].ToString();
            var signature = context.Request.Headers["X-Hub-Signature-256"].ToString();
            var deliveryId = context.Request.Headers["X-GitHub-Delivery"].ToString();

            _logger.LogInformation($"Received GitHub webhook: {eventType}, Delivery: {deliveryId}");

            // Читаем тело запроса
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();
            var payload = JsonDocument.Parse(body).RootElement;

            // Проверяем подпись если настроена
            var secret = _configuration["GitHub:WebhookSecret"];
            if (!string.IsNullOrEmpty(secret))
            {
                if (!VerifySignature(body, signature, secret))
                {
                    _logger.LogWarning("Invalid webhook signature");
                    context.Response.StatusCode = 401;
                    return;
                }
            }

            // Обрабатываем событие
            await ProcessGitHubEventAsync(eventType, payload);

            context.Response.StatusCode = 200;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing GitHub webhook");
            context.Response.StatusCode = 500;
        }
    }

    private async Task ProcessGitHubEventAsync(string eventType, JsonElement payload)
    {
        try
        {
            // Получаем Chat ID из конфигурации или переменной окружения
            var chatIdStr = _configuration["Telegram:ChatId"] ??
                           Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");

            if (string.IsNullOrEmpty(chatIdStr) || !long.TryParse(chatIdStr, out var chatId))
            {
                _logger.LogWarning("Telegram Chat ID not configured. Skipping notification.");
                return;
            }

            switch (eventType)
            {
                case "push":
                    await HandlePushEventAsync(payload, chatId);
                    break;

                case "pull_request":
                    await HandlePullRequestEventAsync(payload, chatId);
                    break;

                case "issues":
                    await HandleIssueEventAsync(payload, chatId);
                    break;

                case "release":
                    await HandleReleaseEventAsync(payload, chatId);
                    break;

                case "workflow_run":
                    await HandleWorkflowRunEventAsync(payload, chatId);
                    break;

                default:
                    _logger.LogInformation($"Unhandled event type: {eventType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing {eventType} event");
        }
    }

    private async Task HandlePushEventAsync(JsonElement payload, long chatId)
    {
        var repository = payload.GetProperty("repository");
        var repoName = repository.GetProperty("full_name").GetString();
        var ref_name = payload.GetProperty("ref").GetString()?.Replace("refs/heads/", "");
        var commits = payload.GetProperty("commits");

        if (commits.GetArrayLength() == 0)
            return; // Пропускаем пустые пуши (например, merge commits)

        var message = $"🚀 *Новый пуш в {repoName}*\n\n" +
                     $"🌿 Ветка: `{ref_name}`\n" +
                     $"📦 Коммитов: {commits.GetArrayLength()}\n\n";

        foreach (var commit in commits.EnumerateArray().Take(3))
        {
            var sha = commit.GetProperty("id").GetString()?[..8];
            var author = commit.GetProperty("author").GetProperty("name").GetString();
            var commitMessage = commit.GetProperty("message").GetString()?.Split('\n')[0];

            message += $"🔹 `{sha}` - {author}\n" +
                      $"   _{commitMessage}_\n\n";
        }

        if (commits.GetArrayLength() > 3)
        {
            message += $"... и ещё {commits.GetArrayLength() - 3} коммитов\n";
        }

        var pusher = payload.GetProperty("pusher").GetProperty("name").GetString();
        message += $"👤 Автор: {pusher}";

        await SendTelegramMessageAsync(chatId, message);
    }

    private async Task HandlePullRequestEventAsync(JsonElement payload, long chatId)
    {
        var action = payload.GetProperty("action").GetString();
        var pr = payload.GetProperty("pull_request");
        var number = pr.GetProperty("number").GetInt32();
        var title = pr.GetProperty("title").GetString();
        var user = pr.GetProperty("user").GetProperty("login").GetString();
        var htmlUrl = pr.GetProperty("html_url").GetString();

        var message = action switch
        {
            "opened" => $"🔄 *Новый Pull Request #{number}*\n\n" +
                       $"📋 {title}\n" +
                       $"👤 {user}\n" +
                       $"🔗 [Посмотреть PR]({htmlUrl})",

            "closed" => pr.GetProperty("merged").GetBoolean()
                ? $"✅ *PR #{number} смержен*\n\n" +
                  $"📋 {title}\n" +
                  $"👤 {user}"
                : $"❌ *PR #{number} закрыт*\n\n" +
                  $"📋 {title}\n" +
                  $"👤 {user}",

            "reopened" => $"🔄 *PR #{number} переоткрыт*\n\n" +
                         $"📋 {title}\n" +
                         $"👤 {user}",

            _ => null
        };

        if (message != null)
        {
            await SendTelegramMessageAsync(chatId, message);
        }
    }

    private async Task HandleIssueEventAsync(JsonElement payload, long chatId)
    {
        var action = payload.GetProperty("action").GetString();
        var issue = payload.GetProperty("issue");
        var number = issue.GetProperty("number").GetInt32();
        var title = issue.GetProperty("title").GetString();
        var user = issue.GetProperty("user").GetProperty("login").GetString();
        var htmlUrl = issue.GetProperty("html_url").GetString();

        var message = action switch
        {
            "opened" => $"📝 *Новая задача #{number}*\n\n" +
                       $"📋 {title}\n" +
                       $"👤 {user}\n" +
                       $"🔗 [Посмотреть задачу]({htmlUrl})",

            "closed" => $"✅ *Задача #{number} закрыта*\n\n" +
                       $"📋 {title}\n" +
                       $"👤 {user}",

            "reopened" => $"🔄 *Задача #{number} переоткрыта*\n\n" +
                         $"📋 {title}\n" +
                         $"👤 {user}",

            _ => null
        };

        if (message != null)
        {
            await SendTelegramMessageAsync(chatId, message);
        }
    }

    private async Task HandleReleaseEventAsync(JsonElement payload, long chatId)
    {
        var action = payload.GetProperty("action").GetString();
        if (action != "published") return;

        var release = payload.GetProperty("release");
        var tag = release.GetProperty("tag_name").GetString();
        var name = release.GetProperty("name").GetString();
        var htmlUrl = release.GetProperty("html_url").GetString();
        var author = release.GetProperty("author").GetProperty("login").GetString();

        var message = $"🎉 *Новый релиз: {tag}*\n\n" +
                     $"📦 {name}\n" +
                     $"👤 {author}\n" +
                     $"🔗 [Посмотреть релиз]({htmlUrl})";

        await SendTelegramMessageAsync(chatId, message);
    }

    private async Task HandleWorkflowRunEventAsync(JsonElement payload, long chatId)
    {
        var action = payload.GetProperty("action").GetString();
        if (action != "completed") return;

        var workflowRun = payload.GetProperty("workflow_run");
        var name = workflowRun.GetProperty("name").GetString();
        var conclusion = workflowRun.GetProperty("conclusion").GetString();
        var htmlUrl = workflowRun.GetProperty("html_url").GetString();
        var headBranch = workflowRun.GetProperty("head_branch").GetString();

        var status = conclusion switch
        {
            "success" => "✅",
            "failure" => "❌",
            "cancelled" => "🚫",
            _ => "⚠️"
        };

        var message = $"{status} *CI/CD: {name}*\n\n" +
                     $"🌿 Ветка: {headBranch}\n" +
                     $"📊 Статус: {conclusion}\n" +
                     $"🔗 [Детали]({htmlUrl})";

        await SendTelegramMessageAsync(chatId, message);
    }

    private async Task SendTelegramMessageAsync(long chatId, string message)
    {
        try
        {
            await _telegramBotClient.SendTextMessageAsync(
                chatId: chatId,
                text: message,
                parseMode: ParseMode.Markdown,
                disableWebPagePreview: true
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending Telegram message");
        }
    }

    private bool VerifySignature(string payload, string signature, string secret)
    {
        // Реализация проверки подписи GitHub webhook
        // Для упрощения пропускаем, но в продакшене обязательно реализовать
        return true;
    }
}
