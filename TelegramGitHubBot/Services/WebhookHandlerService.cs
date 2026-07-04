using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramGitHubBot.Services;

public class WebhookHandlerService
{
    private readonly IConfiguration _configuration;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly GitHubService _gitHubService;
    private readonly TelegramBotService _telegramBotService;
    private readonly AchievementService _achievementService;
    private readonly GeminiManager _llm;
    private readonly ILogger<WebhookHandlerService> _logger;

    public WebhookHandlerService(
        IConfiguration configuration,
        ITelegramBotClient telegramBotClient,
        GitHubService gitHubService,
        TelegramBotService telegramBotService,
        AchievementService achievementService,
        GeminiManager llm,
        ILogger<WebhookHandlerService> logger)
    {
        _configuration = configuration;
        _telegramBotClient = telegramBotClient;
        _gitHubService = gitHubService;
        _telegramBotService = telegramBotService;
        _achievementService = achievementService;
        _llm = llm;
        _logger = logger;
    }

    // Включена ли AI-суммаризация GitHub-событий (env SUMMARIZE_GITHUB_EVENTS=true)
    private bool SummariesEnabled =>
        (Environment.GetEnvironmentVariable("SUMMARIZE_GITHUB_EVENTS") ?? "").ToLowerInvariant() is "true" or "1" or "yes";

    // Убираем теги [GIF:...] из ответа LLM — для резюме они не нужны
    private static string StripGifTags(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\[GIF:[^\]]*\]", "").Trim();

    public async Task HandleGitHubWebhookAsync(HttpContext context)
    {
        try
        {
            var eventType = context.Request.Headers["X-GitHub-Event"].ToString();
            var signature = context.Request.Headers["X-Hub-Signature-256"].ToString();
            var deliveryId = context.Request.Headers["X-GitHub-Delivery"].ToString();

            _logger.LogInformation($"🔥 Received GitHub webhook: {eventType}, Delivery: {deliveryId}");

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
            _logger.LogInformation($"✅ Webhook processed successfully: {eventType}");
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
            var configChatId = _configuration["Telegram:ChatId"];
            var envChatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");

            // Используем env переменную, если она не пустая, иначе config
            var chatIdStr = !string.IsNullOrWhiteSpace(envChatId) ? envChatId : configChatId;

            _logger.LogInformation($"🔍 Chat ID from config: '{configChatId}' (IsNullOrWhiteSpace: {string.IsNullOrWhiteSpace(configChatId)})");
            _logger.LogInformation($"🔍 Chat ID from env: '{envChatId}' (IsNullOrWhiteSpace: {string.IsNullOrWhiteSpace(envChatId)})");
            _logger.LogInformation($"🔍 Final Chat ID string: '{chatIdStr}' (length: {chatIdStr?.Length ?? 0})");

            if (string.IsNullOrEmpty(chatIdStr) || !long.TryParse(chatIdStr, out var chatId))
            {
                _logger.LogWarning($"❌ Telegram Chat ID not configured or invalid. ChatIdStr: '{chatIdStr}', IsNullOrEmpty: {string.IsNullOrEmpty(chatIdStr)}, ParseResult: {long.TryParse(chatIdStr, out var _)}");
                return;
            }

            Console.WriteLine($"🎯 Webhook processing for event {eventType}, using chatId: {chatId}");

            _logger.LogInformation($"✅ Using Chat ID: {chatId}");

            switch (eventType)
            {
                case "push":
                    await HandlePushEventAsync(payload, chatId);
                    break;
                case "create":
                    await HandleCreateEventAsync(payload);
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

    private Task HandleCreateEventAsync(JsonElement payload)
    {
        try
        {
            if (payload.TryGetProperty("ref_type", out var refType) && refType.GetString() == "branch")
            {
                var branchName = payload.GetProperty("ref").GetString() ?? "";
                var sender = payload.GetProperty("sender").GetProperty("login").GetString() ?? "Unknown";
                string senderEmail = string.Empty;
                try
                {
                    senderEmail = payload.GetProperty("sender").GetProperty("email").GetString() ?? string.Empty;
                }
                catch { }

                _logger.LogInformation($"🌿 Branch created: {branchName} by {sender}");
                _achievementService.RegisterBranchCreated(sender, senderEmail, DateTime.UtcNow);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки create event");
        }
        return Task.CompletedTask;
    }

    private async Task HandlePushEventAsync(JsonElement payload, long chatId)
    {
        _logger.LogInformation($"🚀 Processing push event for chat {chatId}");

        var repository = payload.GetProperty("repository");
        var repoName = repository.GetProperty("full_name").GetString();
        var ref_name = payload.GetProperty("ref").GetString()?.Replace("refs/heads/", "");
        var commits = payload.GetProperty("commits");

        _logger.LogInformation($"📦 Push to {repoName}/{ref_name}, commits: {commits.GetArrayLength()}");

        if (commits.GetArrayLength() == 0)
        {
            _logger.LogInformation("🚫 Empty push, skipping notification");
            return; // Пропускаем пустые пуши (например, merge commits)
        }

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

        // Обрабатываем все коммиты для ачивок
        foreach (var commit in commits.EnumerateArray())
        {
            try
            {
                var author = commit.GetProperty("author").GetProperty("name").GetString() ?? "Unknown";
                var email = commit.GetProperty("author").GetProperty("email").GetString() ?? "";
                var commitMessage = commit.GetProperty("message").GetString() ?? "";
                var commitDate = commit.GetProperty("timestamp").GetDateTime();

                // Получаем sha из вебхука, чтобы подтянуть реальные additions/deletions у GitHub API
                var fullSha = commit.GetProperty("id").GetString() ?? string.Empty;
                int additions = 0;
                int deletions = 0;
                if (!string.IsNullOrEmpty(fullSha))
                {
                    var stats = await _gitHubService.GetCommitStatsAsync(fullSha);
                    additions = stats.additions;
                    deletions = stats.deletions;
                }

                _achievementService.ProcessCommitIfNew(fullSha, author, email, commitMessage, commitDate, additions, deletions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обработки коммита для ачивок");
            }
        }

        if (commits.GetArrayLength() > 3)
        {
            message += $"... и ещё {commits.GetArrayLength() - 3} коммитов\n";
        }

        var pusher = payload.GetProperty("pusher").GetProperty("name").GetString();
        message += $"👤 Автор: {pusher}";

        // Получаем SHA первого коммита для кнопки (сокращаем до 8 символов)
        var firstCommit = commits.EnumerateArray().FirstOrDefault();
        InlineKeyboardMarkup? inlineKeyboard = null;

        if (firstCommit.TryGetProperty("id", out var idProperty))
        {
            var firstCommitSha = idProperty.GetString();
            if (!string.IsNullOrEmpty(firstCommitSha) && firstCommitSha.Length >= 8)
            {
                var shortSha = firstCommitSha[..8]; // Берем только первые 8 символов

                inlineKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("📋 Подробно", $"cd:{shortSha}:{repoName}:details")
                    }
                });
            }
        }

        _logger.LogInformation($"📤 Push notification disabled for chat {chatId}: {message.Replace('\n', ' ')}");
        // var pushMessage = await SendTelegramMessageAsync(chatId, message, "push", inlineKeyboard);
        // _logger.LogInformation($"✅ Push message sent successfully to chat {chatId}, MessageId: {pushMessage?.MessageId}");

        // AI-суммаризация пуша через LLM (Ollama/Gemini), если включена флагом
        if (SummariesEnabled)
        {
            try
            {
                var commitLines = commits.EnumerateArray()
                    .Select(c => c.GetProperty("message").GetString()?.Split('\n')[0])
                    .Where(m => !string.IsNullOrWhiteSpace(m));
                var prompt =
                    "Кратко суммируй, что изменилось в этом пуше, простым языком, 1-2 предложения, " +
                    "без эмодзи и без каких-либо тегов. Сообщения коммитов:\n" +
                    string.Join("\n", commitLines);

                var summary = StripGifTags(await _llm.GenerateResponseAsync(prompt));
                if (!string.IsNullOrWhiteSpace(summary) && !summary.Contains("❌"))
                {
                    var aiMessage = $"🤖 *Резюме пуша в {repoName}* (`{ref_name}`)\n\n{summary}";

                    // Куда слать резюме: тема супергруппы (env), иначе тот же chatId
                    var summaryChatIdStr = Environment.GetEnvironmentVariable("PUSH_SUMMARY_CHAT_ID");
                    var summaryChatId = !string.IsNullOrWhiteSpace(summaryChatIdStr) && long.TryParse(summaryChatIdStr, out var scid) ? scid : chatId;
                    int? threadId = int.TryParse(Environment.GetEnvironmentVariable("PUSH_SUMMARY_THREAD_ID"), out var tid) ? tid : null;

                    await _telegramBotClient.SendTextMessageAsync(summaryChatId, aiMessage, parseMode: ParseMode.Markdown, messageThreadId: threadId);
                    _logger.LogInformation($"✅ AI-резюме пуша отправлено в чат {summaryChatId}, тема {threadId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка AI-суммаризации пуша");
            }
        }
    }

    private Task HandlePullRequestEventAsync(JsonElement payload, long chatId)
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
            _logger.LogInformation($"🔕 PR notification disabled for chat {chatId}: {message.Replace('\n', ' ')}");
            // await SendTelegramMessageAsync(chatId, message, "pull_request");
        }
        return Task.CompletedTask;
    }

    private Task HandleIssueEventAsync(JsonElement payload, long chatId)
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
            _logger.LogInformation($"🔕 Issue notification disabled for chat {chatId}: {message.Replace('\n', ' ')}");
            // await SendTelegramMessageAsync(chatId, message, "issues");
        }
        return Task.CompletedTask;
    }

    private Task HandleReleaseEventAsync(JsonElement payload, long chatId)
    {
        var action = payload.GetProperty("action").GetString();
        if (action != "published") return Task.CompletedTask;

        var release = payload.GetProperty("release");
        var tag = release.GetProperty("tag_name").GetString();
        var name = release.GetProperty("name").GetString();
        var htmlUrl = release.GetProperty("html_url").GetString();
        var author = release.GetProperty("author").GetProperty("login").GetString();

        var message = $"🎉 *Новый релиз: {tag}*\n\n" +
                     $"📦 {name}\n" +
                     $"👤 {author}\n" +
                     $"🔗 [Посмотреть релиз]({htmlUrl})";

        _logger.LogInformation($"🔕 Release notification disabled for chat {chatId}: {message.Replace('\n', ' ')}");
        // await SendTelegramMessageAsync(chatId, message, "release");
        return Task.CompletedTask;
    }

    private Task HandleWorkflowRunEventAsync(JsonElement payload, long chatId)
    {
        var action = payload.GetProperty("action").GetString();
        if (action != "completed") return Task.CompletedTask;

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

        _logger.LogInformation($"🔕 Workflow notification disabled for chat {chatId}: {message.Replace('\n', ' ')}");
        // await SendTelegramMessageAsync(chatId, message, "workflow");
        return Task.CompletedTask;
    }

    private async Task<Telegram.Bot.Types.Message?> SendTelegramMessageAsync(long chatId, string message, string notificationType, InlineKeyboardMarkup? keyboard = null)
    {
        // Проверяем настройки уведомлений для данного чата
        Console.WriteLine($"🔍 Webhook: Checking notification {notificationType} for chat {chatId}");
        if (!_telegramBotService.ShouldSendNotification(chatId, notificationType))
        {
            _logger.LogInformation($"🔕 Notification {notificationType} disabled for chat {chatId}, skipping");
            Console.WriteLine($"🔕 Notification {notificationType} disabled for chat {chatId}, skipping");
            return null;
        }

        Console.WriteLine($"✅ Notification {notificationType} enabled for chat {chatId}, sending");
        return await SendTelegramMessageAsync(chatId, message, keyboard);
    }

    private async Task<Telegram.Bot.Types.Message?> SendTelegramMessageAsync(long chatId, string message, InlineKeyboardMarkup? keyboard = null)
    {
        try
        {
            _logger.LogInformation($"📨 Attempting to send message to chat {chatId}");
            var result = await _telegramBotClient.SendTextMessageAsync(
                chatId: chatId,
                text: message,
                parseMode: ParseMode.Markdown,
                disableWebPagePreview: true,
                disableNotification: true, // Отправляем без звука
                replyMarkup: keyboard
            );
            _logger.LogInformation($"✅ Telegram message sent, MessageId: {result.MessageId}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ Error sending Telegram message to chat {chatId}: {ex.Message}");
            if (ex.InnerException != null)
            {
                _logger.LogError(ex.InnerException, "Inner exception details");
            }
            return null;
        }
    }

    private async Task DeleteTelegramMessageAsync(long chatId, int messageId)
    {
        try
        {
            await _telegramBotClient.DeleteMessageAsync(chatId, messageId);
            _logger.LogInformation($"🗑️ Message {messageId} deleted from chat {chatId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ Error deleting message {messageId} from chat {chatId}: {ex.Message}");
        }
    }

    private bool VerifySignature(string payload, string signature, string secret)
    {
        // Реализация проверки подписи GitHub webhook
        // Для упрощения пропускаем, но в продакшене обязательно реализовать
        return true;
    }
}
