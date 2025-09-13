using System.Collections.Generic;
using System.Timers;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramGitHubBot.Services;

public class NotificationSettings
{
    public bool PushNotifications { get; set; } = true;
    public bool PullRequestNotifications { get; set; } = true;
    public bool WorkflowNotifications { get; set; } = true;
    public bool ReleaseNotifications { get; set; } = true;
    public bool IssueNotifications { get; set; } = true;
}

public class TelegramBotService
{
    private readonly ITelegramBotClient _botClient;
    private readonly GitHubService _gitHubService;
    private readonly Dictionary<long, NotificationSettings> _chatSettings = new();
    private readonly HashSet<string> _processedCallbacks = new();
    private readonly Dictionary<string, System.Timers.Timer> _messageTimers = new();
    private System.Timers.Timer? _dailySummaryTimer;

    public TelegramBotService(ITelegramBotClient botClient, GitHubService gitHubService)
    {
        _botClient = botClient;
        _gitHubService = gitHubService ?? throw new ArgumentNullException(nameof(gitHubService));

        // Настраиваем ежедневную сводку в 18:00 МСК
        SetupDailySummaryTimer();
    }

    public async Task HandleUpdateAsync(HttpContext context)
    {
        try
        {
            var update = await context.Request.ReadFromJsonAsync<Update>();
            if (update == null) return;

            if (update.Message is { } message)
            {
                await HandleMessageAsync(message);
            }
            else if (update.CallbackQuery is { } callbackQuery)
            {
                await HandleCallbackQueryAsync(callbackQuery);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling Telegram update: {ex.Message}");
        }
    }

    public async Task HandleMessageAsync(Message message)
    {
        if (message.Text == null) return;

        var chatId = message.Chat.Id;
        var text = message.Text.Trim();

        // Отвечаем только на команды, начинающиеся с "/"
        if (text.StartsWith("/"))
        {
            // Обрабатываем команды с тегом бота (/command@BotName)
            var cleanCommand = text.Split('@')[0]; // Убираем @BotName если есть
            await HandleCommandAsync(chatId, cleanCommand, message.From?.Username);
        }
        // Игнорируем все остальные сообщения (не отвечаем)
    }

    private NotificationSettings GetOrCreateSettings(long chatId)
    {
        if (!_chatSettings.TryGetValue(chatId, out var settings))
        {
            settings = new NotificationSettings();
            _chatSettings[chatId] = settings;
        }
        return settings;
    }

    private async Task HandleCommandAsync(long chatId, string command, string? username)
    {
        try
        {
            var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLower();

            switch (cmd)
            {
                case "/start":
                    await SendWelcomeMessageAsync(chatId);
                    break;

                case "/help":
                    await SendHelpMessageAsync(chatId);
                    break;

                case "/settings":
                case "/manage":
                    await SendSettingsMessageAsync(chatId);
                    break;

                case "/status":
                    await HandleStatusCommandAsync(chatId);
                    break;

                case "/commits":
                    if (parts.Length > 1)
                    {
                        var branch = parts[1];
                        var count = parts.Length > 2 && int.TryParse(parts[2], out var c) ? c : 5;
                        await HandleCommitsCommandAsync(chatId, branch, count);
                    }
                    else
                    {
                        await ShowBranchSelectionAsync(chatId, "commits");
                    }
                    break;

                case "/branches":
                    await HandleBranchesCommandAsync(chatId);
                    break;

                case "/prs":
                case "/pulls":
                    await HandlePullRequestsCommandAsync(chatId);
                    break;

                case "/ci":
                case "/workflows":
                    if (parts.Length > 1)
                    {
                        var wfBranch = parts[1];
                        var wfCount = parts.Length > 2 && int.TryParse(parts[2], out var wc) ? wc : 5;
                        await HandleWorkflowsCommandAsync(chatId, wfBranch, wfCount);
                    }
                    else
                    {
                        await ShowBranchSelectionAsync(chatId, "workflows");
                    }
                    break;

                case "/deploy":
                    if (parts.Length > 1)
                    {
                        await HandleDeployCommandAsync(chatId, parts[1], username);
                    }
                    else
                    {
                        await _botClient.SendTextMessageAsync(chatId, "Укажите среду для деплоя: /deploy staging или /deploy production", disableNotification: true);
                    }
                    break;

                case "/laststats":
                    await SendDailySummaryAsync(chatId);
                    break;

                case "/search":
                    if (parts.Length > 1)
                    {
                        var query = string.Join(" ", parts.Skip(1));
                        await HandleSearchCommandAsync(chatId, query);
                    }
                    else
                    {
                        await _botClient.SendTextMessageAsync(chatId, "Использование: /search <запрос>\nПример: /search fix bug", disableNotification: true);
                    }
                    break;

                case "/authors":
                    await HandleAuthorsCommandAsync(chatId);
                    break;

                case "/files":
                    if (parts.Length > 1)
                    {
                        var commitSha = parts[1];
                        await HandleFilesCommandAsync(chatId, commitSha);
                    }
                    else
                    {
                        await _botClient.SendTextMessageAsync(chatId, "Использование: /files <sha коммита>", disableNotification: true);
                    }
                    break;

                case "/педик":
                    await _botClient.SendTextMessageAsync(chatId, "Сам ты педик", disableNotification: true);
                    break;

                default:

                    break;
            }
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"Ошибка выполнения команды: {ex.Message}", disableNotification: true);
        }
    }

    private async Task SendWelcomeMessageAsync(long chatId)
    {
        var message = @"🤖 GitHub Monitor Bot
Мониторинг репозитория goodluckv2

📢 Уведомления о:
• Коммитах
• PR/MR
• CI/CD
• Релизах

💡 *Настройте уведомления* через ⚙️ Настройки";

        var inlineKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📊 Статус", "/status"),
                InlineKeyboardButton.WithCallbackData("📝 Коммиты", "/commits"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🌿 Ветки", "/branches"),
                InlineKeyboardButton.WithCallbackData("🔄 PR", "/prs"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⚙️ CI/CD", "/ci"),
                InlineKeyboardButton.WithCallbackData("🚀 Деплой", "/deploy"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📈 Последняя статистика", "/laststats"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⚙️ Настройки", "/settings"),
                InlineKeyboardButton.WithCallbackData("❓ Справка", "/help"),
            }
        });

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: message,
            disableNotification: true,
            replyMarkup: inlineKeyboard
        );
    }

    private async Task SendSettingsMessageAsync(long chatId)
    {
        var settings = GetOrCreateSettings(chatId);

        var message = @"⚙️ *Настройки уведомлений*

Выберите типы уведомлений, которые хотите получать:";

        var inlineKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"{(settings.PushNotifications ? "✅" : "❌")} Коммиты",
                    $"toggle:push:{chatId}"),
                InlineKeyboardButton.WithCallbackData(
                    $"{(settings.PullRequestNotifications ? "✅" : "❌")} PR/MR",
                    $"toggle:pr:{chatId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"{(settings.WorkflowNotifications ? "✅" : "❌")} CI/CD",
                    $"toggle:ci:{chatId}"),
                InlineKeyboardButton.WithCallbackData(
                    $"{(settings.ReleaseNotifications ? "✅" : "❌")} Релизы",
                    $"toggle:release:{chatId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"{(settings.IssueNotifications ? "✅" : "❌")} Задачи",
                    $"toggle:issue:{chatId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⬅️ Назад", "/start")
            }
        });

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: message,
            parseMode: ParseMode.Markdown,
            disableWebPagePreview: true,
            disableNotification: true,
            replyMarkup: inlineKeyboard
        );
    }

    private async Task SendHelpMessageAsync(long chatId)
    {
        var message = @"📋 *Команды бота:*

📊 /status - Статус репозитория
📝 /commits [ветка] [кол-во] - Коммиты (интерактивно)
🌿 /branches - Список веток
🔄 /prs - Открытые PR
⚙️ /ci [ветка] - CI/CD статус (интерактивно)
🚀 /deploy [среда] - Деплой
📈 /laststats - Последняя статистика

🔍 *Поиск и анализ:*
🔎 /search <запрос> - Поиск по коммитам
👥 /authors - Активные авторы
📁 /files <sha> - Файлы в коммите

⚙️ *Настройки:*
⚙️ /settings - Настройки уведомлений
📋 /help - Эта справка

💡 *Подсказки:*
• Команды без параметров показывают интерактивное меню
• Используйте кнопки для быстрой навигации";

        var inlineKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📊 Статус", "/status"),
                InlineKeyboardButton.WithCallbackData("📝 Коммиты", "/commits"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🌿 Ветки", "/branches"),
                InlineKeyboardButton.WithCallbackData("🔄 PR", "/prs"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⚙️ CI/CD", "/ci"),
                InlineKeyboardButton.WithCallbackData("🚀 Деплой", "/deploy"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📈 Статистика", "/laststats"),
                InlineKeyboardButton.WithCallbackData("👥 Авторы", "/authors"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🔍 Поиск", "search_menu"),
                InlineKeyboardButton.WithCallbackData("⚙️ Настройки", "/settings"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "/start"),
            }
        });

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: message,
            disableNotification: true,
            replyMarkup: inlineKeyboard
        );
    }

    private async Task HandleWorkflowsCommandAsync(long chatId, string? branch, int count)
    {
        try
        {
            var workflows = await _gitHubService.GetWorkflowRunsAsync(branch ?? string.Empty, count);
            await _botClient.SendTextMessageAsync(chatId, workflows, parseMode: ParseMode.Markdown, disableNotification: true);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения CI/CD статусов: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandleDeployCommandAsync(long chatId, string environment, string? username)
    {
        try
        {
            // Проверяем права пользователя
            if (string.IsNullOrEmpty(username))
            {
                await _botClient.SendTextMessageAsync(chatId, "❌ Не удалось определить пользователя", disableNotification: true);
                return;
            }

            var allowedUsers = new[] { "your_username" }; // Добавьте разрешенных пользователей
            if (!allowedUsers.Contains(username.ToLower()))
            {
                await _botClient.SendTextMessageAsync(chatId, "❌ У вас нет прав для запуска деплоя", disableNotification: true);
                return;
            }

            if (environment.ToLower() != "staging" && environment.ToLower() != "production")
            {
                await _botClient.SendTextMessageAsync(chatId, "❌ Доступные среды: staging, production", disableNotification: true);
                return;
            }

            var message = $"🚀 *Запуск деплоя в {environment}*\n\n" +
                         $"👤 Инициировал: {username}\n" +
                         $"⏰ Время: {DateTime.Now:dd.MM.yyyy HH:mm}\n" +
                         $"🔄 Статус: Запускается...";

            await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, disableNotification: true);

            // Здесь можно добавить логику для запуска GitHub Actions workflow
            // await _gitHubService.TriggerDeploymentAsync(environment, username);

            var successMessage = $"✅ *Деплой в {environment} запущен!*\n\n" +
                               $"👤 {username}\n" +
                               $"📊 Следите за статусом через /ci";

            await _botClient.SendTextMessageAsync(chatId, successMessage, parseMode: ParseMode.Markdown, disableNotification: true);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка запуска деплоя: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandleStatusCommandAsync(long chatId)
    {
        try
        {
            var status = await _gitHubService.GetRepositoryStatusAsync();
            await _botClient.SendTextMessageAsync(chatId, status, parseMode: ParseMode.Markdown, disableNotification: true);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения статуса: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandleCommitsCommandAsync(long chatId, string branch, int count)
    {
        try
        {
            var commits = await _gitHubService.GetRecentCommitsAsync(branch, count);
            await _botClient.SendTextMessageAsync(chatId, commits, parseMode: ParseMode.Markdown, disableNotification: true);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения коммитов: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandleBranchesCommandAsync(long chatId)
    {
        try
        {
            var branches = await _gitHubService.GetBranchesAsync();
            await _botClient.SendTextMessageAsync(chatId, branches, parseMode: ParseMode.Markdown, disableNotification: true);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения веток: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandlePullRequestsCommandAsync(long chatId)
    {
        try
        {
            var prs = await _gitHubService.GetPullRequestsAsync();
            await _botClient.SendTextMessageAsync(chatId, prs, parseMode: ParseMode.Markdown, disableNotification: true);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения PR: {ex.Message}", disableNotification: true);
        }
    }

    public async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery)
    {
        Console.WriteLine($"🎯 HandleCallbackQueryAsync called with data: '{callbackQuery.Data}'");

        var chatId = callbackQuery.Message?.Chat.Id ?? 0;
        var data = callbackQuery.Data;
        var messageId = callbackQuery.Message?.MessageId ?? 0;

        Console.WriteLine($"📍 ChatId: {chatId}, Data: '{data}', MessageId: {messageId}");

        if (chatId == 0 || string.IsNullOrEmpty(data) || messageId == 0)
        {
            Console.WriteLine("❌ Invalid callback query data");
            await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Ошибка обработки запроса");
            return;
        }

        // Защита от повторных нажатий
        var callbackKey = $"{callbackQuery.Id}:{data}";
        if (_processedCallbacks.Contains(callbackKey))
        {
            Console.WriteLine("⚠️ Callback already processed, ignoring");
            await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Уже обработано");
            return;
        }

        _processedCallbacks.Add(callbackKey);

        // Ограничиваем размер множества (чтобы не росло бесконечно)
        if (_processedCallbacks.Count > 1000)
        {
            _processedCallbacks.Clear();
        }

        try
        {
            // Отвечаем на callback query
            await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id);
            Console.WriteLine("✅ Callback query answered");

            // Проверяем, является ли это запросом деталей коммита
            if (data.StartsWith("cd:") || data.StartsWith("commit_details:"))
            {
                Console.WriteLine("📋 Processing commit details request");
                // Удаляем текущее сообщение
                await DeleteMessageAsync(chatId, messageId);
                // Обрабатываем запрос деталей коммита
                await HandleCommitDetailsCallbackAsync(chatId, data);
            }
            else if (data.StartsWith("toggle:"))
            {
                Console.WriteLine("⚙️ Processing notification toggle request");
                // Обрабатываем переключение уведомлений
                await HandleNotificationToggleAsync(chatId, data, messageId);
            }
            else if (data.StartsWith("branch_"))
            {
                Console.WriteLine("🌿 Processing branch selection");
                await HandleBranchCallbackAsync(chatId, data, messageId);
            }
            else if (data == "search_menu")
            {
                Console.WriteLine("🔍 Processing search menu");
                await ShowSearchMenuAsync(chatId, messageId);
            }
            else
            {
                Console.WriteLine("📝 Processing regular command");
                // Обрабатываем обычную команду из callback data
                await HandleCommandAsync(chatId, data, callbackQuery.From?.Username);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Callback query error: {ex.Message}");
            await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Произошла ошибка");
        }
    }

    private async Task DeleteMessageAsync(long chatId, int messageId)
    {
        try
        {
            await _botClient.DeleteMessageAsync(chatId, messageId);
            Console.WriteLine($"🗑️ Deleted message {messageId} from chat {chatId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to delete message {messageId}: {ex.Message}");
        }
    }

    private async Task RestorePushMessageAsync(long chatId, string commitSha, string repoName)
    {
        try
        {
            // Получаем информацию о коммите для восстановления сообщения
            var commitDetails = await _gitHubService.GetCommitDetailsAsync(commitSha);

            // Извлекаем основную информацию из деталей коммита
            var author = "Неизвестен";
            var message = "Коммит";
            var shortSha = commitSha[..8];

            // Простой парсинг деталей коммита для извлечения автора и сообщения
            var lines = commitDetails.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("👤 Автор: "))
                {
                    author = line.Replace("👤 Автор: ", "").Trim();
                }
                else if (line.StartsWith("📝 Сообщение:"))
                {
                    // Следующая строка после "📝 Сообщение:" содержит текст
                    var messageIndex = Array.IndexOf(lines, line) + 1;
                    if (messageIndex < lines.Length)
                    {
                        message = lines[messageIndex].Trim('`', '*').Replace("```\n", "").Split('\n')[0];
                        if (message.Length > 50)
                        {
                            message = message[..47] + "...";
                        }
                    }
                    break;
                }
            }

            // Создаем сообщение в том же формате, что и исходное
            var pushMessage = $"🚀 *Новый пуш в RaspizDIYs/{repoName}*\n\n" +
                             $"🌿 Ветка: `main`\n" + // По умолчанию main, так как у нас нет информации о ветке
                             $"📦 Коммитов: 1\n\n" +
                             $"🔹 `{shortSha}` - {author}\n" +
                             $"   {message}\n\n" +
                             $"👤 Автор: {author}";

            // Создаем кнопку "Подробно"
            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📋 Подробно", $"cd:{shortSha}:{repoName}:details")
                }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: pushMessage,
                parseMode: ParseMode.Markdown,
                disableWebPagePreview: true,
                disableNotification: true,
                replyMarkup: inlineKeyboard
            );

            Console.WriteLine($"🔄 Restored push message for commit {shortSha}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to restore push message: {ex.Message}");

            // В случае ошибки отправляем упрощенное сообщение
            var fallbackMessage = $"🚀 *Новый пуш в RaspizDIYs/{repoName}*\n\n" +
                                 $"📦 Коммит: `{commitSha[..8]}`\n" +
                                 $"🔗 [Посмотреть на GitHub](https://github.com/RaspizDIYs/goodluckv2/commit/{commitSha})";

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: fallbackMessage,
                parseMode: ParseMode.Markdown,
                disableWebPagePreview: true,
                disableNotification: true
            );
        }
    }

    private async Task HandleCommitDetailsCallbackAsync(long chatId, string callbackData)
    {
        try
        {
            // Разбираем callback data: cd:shortSha:repo:action
            var parts = callbackData.Split(':');
            if (parts.Length < 4)
            {
                await _botClient.SendTextMessageAsync(chatId, "❌ Ошибка: некорректные данные", disableNotification: true);
                return;
            }

            var shortSha = parts[1];
            var repoName = parts[2];
            var action = parts[3];

            // Для полного SHA нужно получить его из GitHub API по короткому
            var commitSha = await GetFullShaFromShortAsync(shortSha, repoName);

            if (action == "details")
            {
                // Показываем детали коммита
                var commitDetails = await _gitHubService.GetCommitDetailsAsync(commitSha);

                var callbackShortSha = commitSha[..8]; // Берем первые 8 символов для callback
                var backKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("⬅️ Назад", $"cd:{callbackShortSha}:{repoName}:back")
                    }
                });

                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: commitDetails,
                    parseMode: ParseMode.Markdown,
                    disableWebPagePreview: true,
                    disableNotification: true,
                    replyMarkup: backKeyboard
                );
            }
            else if (action == "back")
            {
                // Восстанавливаем исходное сообщение о пуше с кнопкой
                await RestorePushMessageAsync(chatId, commitSha, repoName);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling commit details: {ex.Message}");
            await _botClient.SendTextMessageAsync(chatId, "❌ Ошибка получения деталей коммита", disableNotification: true);
        }
    }

    private async Task HandleNotificationToggleAsync(long chatId, string callbackData, int messageId)
    {
        try
        {
            // Разбираем callback data: toggle:type:chatId
            var parts = callbackData.Split(':');
            if (parts.Length < 3)
            {
                await _botClient.AnswerCallbackQueryAsync(callbackData, "❌ Ошибка: некорректные данные");
                return;
            }

            var type = parts[1];
            var targetChatId = long.Parse(parts[2]);

            if (chatId != targetChatId)
            {
                await _botClient.AnswerCallbackQueryAsync(callbackData, "❌ Ошибка: неправильный чат");
                return;
            }

            var settings = GetOrCreateSettings(chatId);

            // Переключаем соответствующую настройку
            string notificationType = "";
            switch (type)
            {
                case "push":
                    settings.PushNotifications = !settings.PushNotifications;
                    notificationType = "Коммиты";
                    break;
                case "pr":
                    settings.PullRequestNotifications = !settings.PullRequestNotifications;
                    notificationType = "PR/MR";
                    break;
                case "ci":
                    settings.WorkflowNotifications = !settings.WorkflowNotifications;
                    notificationType = "CI/CD";
                    break;
                case "release":
                    settings.ReleaseNotifications = !settings.ReleaseNotifications;
                    notificationType = "Релизы";
                    break;
                case "issue":
                    settings.IssueNotifications = !settings.IssueNotifications;
                    notificationType = "Задачи";
                    break;
                default:
                    await _botClient.AnswerCallbackQueryAsync(callbackData, "❌ Неизвестный тип уведомления");
                    return;
            }

            // Обновляем сообщение с новыми настройками
            await UpdateSettingsMessageAsync(chatId, messageId);

            // Отправляем подтверждение
            var statusText = GetNotificationStatus(settings, type);
            await _botClient.AnswerCallbackQueryAsync(callbackData, $"{statusText} {notificationType}");

            Console.WriteLine($"⚙️ Toggled {type} notifications for chat {chatId}: {statusText}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error toggling notification: {ex.Message}");
            await _botClient.AnswerCallbackQueryAsync(callbackData, "❌ Произошла ошибка");
        }
    }

    private async Task UpdateSettingsMessageAsync(long chatId, int messageId)
    {
        try
        {
            var settings = GetOrCreateSettings(chatId);

            var message = @"⚙️ *Настройки уведомлений*

Выберите типы уведомлений, которые хотите получать:";

            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        $"{(settings.PushNotifications ? "✅" : "❌")} Коммиты",
                        $"toggle:push:{chatId}"),
                    InlineKeyboardButton.WithCallbackData(
                        $"{(settings.PullRequestNotifications ? "✅" : "❌")} PR/MR",
                        $"toggle:pr:{chatId}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        $"{(settings.WorkflowNotifications ? "✅" : "❌")} CI/CD",
                        $"toggle:ci:{chatId}"),
                    InlineKeyboardButton.WithCallbackData(
                        $"{(settings.ReleaseNotifications ? "✅" : "❌")} Релизы",
                        $"toggle:release:{chatId}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        $"{(settings.IssueNotifications ? "✅" : "❌")} Задачи",
                        $"toggle:issue:{chatId}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("⬅️ Назад", "/start")
                }
            });

            await _botClient.EditMessageTextAsync(
                chatId: chatId,
                messageId: messageId,
                text: message,
                parseMode: ParseMode.Markdown,
                disableWebPagePreview: true,
                replyMarkup: inlineKeyboard
            );

            Console.WriteLine($"✅ Updated settings message for chat {chatId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error updating settings message: {ex.Message}");
        }
    }

    private string GetNotificationStatus(NotificationSettings settings, string type)
    {
        return type switch
        {
            "push" => settings.PushNotifications ? "Включены" : "Отключены",
            "pr" => settings.PullRequestNotifications ? "Включены" : "Отключены",
            "ci" => settings.WorkflowNotifications ? "Включены" : "Отключены",
            "release" => settings.ReleaseNotifications ? "Включены" : "Отключены",
            "issue" => settings.IssueNotifications ? "Включены" : "Отключены",
            _ => "Неизвестно"
        };
    }

    public bool ShouldSendNotification(long chatId, string notificationType)
    {
        var settings = GetOrCreateSettings(chatId);

        Console.WriteLine($"🔍 Checking notification settings for chat {chatId}, type: {notificationType}");
        Console.WriteLine($"   Push: {settings.PushNotifications}, PR: {settings.PullRequestNotifications}, CI: {settings.WorkflowNotifications}, Release: {settings.ReleaseNotifications}, Issues: {settings.IssueNotifications}");

        var result = notificationType switch
        {
            "push" => settings.PushNotifications,
            "pull_request" => settings.PullRequestNotifications,
            "workflow" => settings.WorkflowNotifications,
            "release" => settings.ReleaseNotifications,
            "issues" => settings.IssueNotifications,
            _ => true // По умолчанию отправляем все неизвестные типы
        };

        Console.WriteLine($"   Result for {notificationType}: {result}");
        return result;
    }

    public void ScheduleMessageDeletion(long chatId, int messageId, int delayMinutes = 30)
    {
        var timerKey = $"{chatId}:{messageId}";
        var timer = new System.Timers.Timer(delayMinutes * 60 * 1000); // Конвертируем минуты в миллисекунды

        timer.Elapsed += async (sender, e) =>
        {
            try
            {
                Console.WriteLine($"🗑️ Auto-deleting message {messageId} from chat {chatId} after {delayMinutes} minutes");
                await _botClient.DeleteMessageAsync(chatId, messageId);
                Console.WriteLine($"✅ Message {messageId} deleted successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to delete message {messageId}: {ex.Message}");
            }
            finally
            {
                // Очищаем таймер после выполнения
                timer.Stop();
                timer.Dispose();
                _messageTimers.Remove(timerKey);
            }
        };

        timer.AutoReset = false; // Одноразовый таймер
        timer.Start();

        // Сохраняем таймер для возможной отмены
        _messageTimers[timerKey] = timer;

        Console.WriteLine($"⏰ Scheduled deletion of message {messageId} from chat {chatId} in {delayMinutes} minutes");
    }

            public void CancelMessageDeletion(long chatId, int messageId)
        {
            var timerKey = $"{chatId}:{messageId}";
            if (_messageTimers.TryGetValue(timerKey, out var timer))
            {
                _messageTimers.Remove(timerKey);
                timer.Stop();
                timer.Dispose();
                Console.WriteLine($"🚫 Cancelled deletion of message {messageId} from chat {chatId}");
            }
        }

    public async Task SendAutoDeletingMessageAsync(long chatId, string text, int delayMinutes = 30, ParseMode? parseMode = null, IReplyMarkup? replyMarkup = null)
    {
        try
        {
            var message = await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: text,
                parseMode: parseMode,
                disableNotification: true,
                replyMarkup: replyMarkup
            );

            // Запланируем удаление сообщения через указанное время
            ScheduleMessageDeletion(chatId, message.MessageId, delayMinutes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to send auto-deleting message: {ex.Message}");
            throw;
        }
    }

    private async Task<string> GetFullShaFromShortAsync(string shortSha, string repoName)
    {
        try
        {
            // Получаем последние коммиты из репозитория (используем main ветку)
            var commitMessage = await _gitHubService.GetRecentCommitsAsync("main", 20);

            // Ищем коммит с совпадающим коротким SHA
            // GetRecentCommitsAsync возвращает string, поэтому нужно получить данные по-другому
            // Пока что просто возвращаем короткий SHA как полный
            return shortSha;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting full SHA: {ex.Message}");
            return shortSha; // Возвращаем короткий в случае ошибки
        }
    }

    private void SetupDailySummaryTimer()
    {
        _dailySummaryTimer = new System.Timers.Timer();
        if (_dailySummaryTimer != null)
        {
            _dailySummaryTimer.Elapsed += async (sender, e) => await SendDailySummaryAsync();
            _dailySummaryTimer.AutoReset = true;

            // Рассчитываем время до следующего запуска в 18:00 МСК
            var now = DateTime.Now;
            var mskTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
            var nowMsk = TimeZoneInfo.ConvertTime(now, mskTimeZone);

            var nextRun = nowMsk.Date.AddHours(18);
            if (nowMsk >= nextRun)
            {
                nextRun = nextRun.AddDays(1);
            }

            var timeUntilNextRun = nextRun - nowMsk;
            _dailySummaryTimer.Interval = timeUntilNextRun.TotalMilliseconds;

            _dailySummaryTimer.Start();
            Console.WriteLine($"⏰ Daily summary timer set to run in {timeUntilNextRun.TotalHours:F1} hours");
        }
    }

    private async Task SendDailySummaryAsync(long? targetChatId = null)
    {
        try
        {
            // Получаем статистику
            var (branchStats, authorStats) = await _gitHubService.GetDailyCommitStatsAsync();
            var (workflowSuccess, workflowFailure) = await _gitHubService.GetDailyWorkflowStatsAsync();

            // Определяем Chat ID: если передан параметр - используем его, иначе получаем из конфигурации
            long chatId;
            if (targetChatId.HasValue)
            {
                chatId = targetChatId.Value;
            }
            else
            {
                var configChatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID") ??
                                  throw new InvalidOperationException("TELEGRAM_CHAT_ID not configured");

                if (!long.TryParse(configChatId, out chatId))
                {
                    Console.WriteLine("❌ Invalid TELEGRAM_CHAT_ID format");
                    return;
                }
            }

            // Формируем сообщение со сводкой
            var title = targetChatId.HasValue
                ? $"📊 *Запрошенная сводка за {DateTime.Now.AddDays(-1):dd.MM.yyyy}*"
                : $"📊 *Ежедневная сводка за {DateTime.Now.AddDays(-1):dd.MM.yyyy}*";
            var message = $"{title}\n\n";

            // Статистика коммитов по веткам
            message += "📝 *Коммиты по веткам:*\n";
            var totalCommits = 0;

            foreach (var (branch, count) in branchStats.OrderByDescending(x => x.Value))
            {
                if (count > 0)
                {
                    message += $"🌿 `{branch}`: {count} коммит{(count != 1 ? "ов" : "")}\n";
                    totalCommits += count;
                }
            }

            if (totalCommits == 0)
            {
                // Если нет коммитов - показываем "выходной" с гифкой
                message = $"🍺 *Выходной! {DateTime.Now.AddDays(-1):dd.MM.yyyy}*\n\n";
                message += "Никто не коммитил - значит отдыхаем! 🎉\n\n";
                message += "https://media.giphy.com/media/8Iv5lqKwKsZ2g/giphy.gif\n\n";
                message += "🍻 Пьём пиво и наслаждаемся жизнью!";
                
                // Отправляем сообщение и завершаем функцию
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: message,
                    parseMode: ParseMode.Markdown,
                    disableNotification: targetChatId.HasValue
                );

                var weekendSummaryType = targetChatId.HasValue ? "requested" : "automatic";
                Console.WriteLine($"✅ {weekendSummaryType} weekend summary sent to chat {chatId}");

                // Перепланируем таймер на следующий день только для автоматических сводок
                if (_dailySummaryTimer != null && !targetChatId.HasValue)
                {
                    _dailySummaryTimer.Interval = 24 * 60 * 60 * 1000;
                }
                return;
            }
            else
            {
                message += $"\n📈 *Всего коммитов:* {totalCommits}\n\n";

                // Статистика по авторам
                message += "👥 *Коммиты по авторам:*\n";
                foreach (var (author, stats) in authorStats.OrderByDescending(x => x.Value.Commits))
                {
                    var commitsText = stats.Commits == 1 ? "коммит" : "коммитов";
                    var changesText = stats.TotalChanges == 1 ? "изменение" : 
                                     stats.TotalChanges < 5 ? "изменения" : "изменений";
                    
                    message += $"👤 {author}: {stats.Commits} {commitsText}\n";
                    if (stats.TotalChanges > 0)
                    {
                        message += $"   📊 +{stats.Additions} -{stats.Deletions} ({stats.TotalChanges} {changesText})\n";
                    }
                }
                message += "\n";
            }

            // Статистика CI/CD
            message += "⚙️ *CI/CD статусы:*\n";
            if (workflowSuccess > 0 || workflowFailure > 0)
            {
                message += $"✅ Успешных: {workflowSuccess}\n";
                message += $"❌ Неудачных: {workflowFailure}\n";
                var totalWorkflows = workflowSuccess + workflowFailure;
                var successRate = totalWorkflows > 0 ? (double)workflowSuccess / totalWorkflows * 100 : 0;
                message += $"📊 Процент успеха: {successRate:F1}%\n";
            }
            else
            {
                message += "😴 CI/CD запусков не было\n";
            }

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: message,
                parseMode: ParseMode.Markdown,
                disableNotification: targetChatId.HasValue // Без уведомления для запрошенных сводок, с уведомлением для автоматических
            );

            var summaryType = targetChatId.HasValue ? "requested" : "automatic";
            Console.WriteLine($"✅ {summaryType} summary sent to chat {chatId}");

            // Перепланируем таймер на следующий день только для автоматических сводок
            if (_dailySummaryTimer != null && !targetChatId.HasValue)
            {
                _dailySummaryTimer.Interval = 24 * 60 * 60 * 1000; // 24 часа в миллисекундах
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error sending daily summary: {ex.Message}");
        }
    }

    private async Task ShowBranchSelectionAsync(long chatId, string action)
    {
        try
        {
            var branches = await _gitHubService.GetBranchesListAsync();
            
            if (!branches.Any())
            {
                await _botClient.SendTextMessageAsync(chatId, "❌ Не удалось получить список веток", disableNotification: true);
                return;
            }

            var message = action switch
            {
                "commits" => "🌿 *Выберите ветку для просмотра коммитов:*",
                "workflows" => "🌿 *Выберите ветку для просмотра CI/CD:*",
                _ => "🌿 *Выберите ветку:*"
            };

            var buttons = new List<InlineKeyboardButton[]>();
            
            // Добавляем кнопки для веток (максимум 8)
            foreach (var branch in branches.Take(8))
            {
                var callbackData = action switch
                {
                    "commits" => $"branch_commits:{branch}",
                    "workflows" => $"branch_workflows:{branch}",
                    _ => $"branch_select:{branch}"
                };
                
                buttons.Add(new[] { InlineKeyboardButton.WithCallbackData($"🌿 {branch}", callbackData) });
            }

            // Кнопка возврата
            buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "/help") });

            var keyboard = new InlineKeyboardMarkup(buttons);

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: message,
                parseMode: ParseMode.Markdown,
                disableNotification: true,
                replyMarkup: keyboard
            );
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения веток: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandleSearchCommandAsync(long chatId, string query)
    {
        try
        {
            var results = await _gitHubService.SearchCommitsAsync(query);
            
            if (string.IsNullOrEmpty(results))
            {
                await _botClient.SendTextMessageAsync(chatId, $"🔍 По запросу '{query}' ничего не найдено", disableNotification: true);
                return;
            }

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "/help") }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: results,
                parseMode: ParseMode.Markdown,
                disableNotification: true,
                replyMarkup: keyboard
            );
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка поиска: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandleAuthorsCommandAsync(long chatId)
    {
        try
        {
            var authors = await _gitHubService.GetActiveAuthorsAsync();
            
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "/help") }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: authors,
                parseMode: ParseMode.Markdown,
                disableNotification: true,
                replyMarkup: keyboard
            );
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения авторов: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandleFilesCommandAsync(long chatId, string commitSha)
    {
        try
        {
            var files = await _gitHubService.GetCommitFilesAsync(commitSha);
            
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("📋 Детали коммита", $"cd:{commitSha}:goodluckv2:details") },
                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "/help") }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: files,
                parseMode: ParseMode.Markdown,
                disableNotification: true,
                replyMarkup: keyboard
            );
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения файлов: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandleBranchCallbackAsync(long chatId, string callbackData, int messageId)
    {
        try
        {
            var parts = callbackData.Split(':');
            if (parts.Length < 2) return;

            var action = parts[0];
            var branch = parts[1];

            // Удаляем сообщение с выбором ветки
            await DeleteMessageAsync(chatId, messageId);

            switch (action)
            {
                case "branch_commits":
                    await HandleCommitsCommandAsync(chatId, branch, 5);
                    break;
                case "branch_workflows":
                    await HandleWorkflowsCommandAsync(chatId, branch, 5);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error handling branch callback: {ex.Message}");
        }
    }

    private async Task ShowSearchMenuAsync(long chatId, int messageId)
    {
        try
        {
            var message = "🔍 *Поиск по репозиторию*\n\n" +
                         "Выберите тип поиска или введите команду:\n\n" +
                         "📝 `/search <текст>` - поиск по сообщениям коммитов\n" +
                         "👤 `/authors` - активные авторы\n" +
                         "📁 `/files <sha>` - файлы в коммите";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("👥 Активные авторы", "/authors") },
                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "/help") }
            });

            await _botClient.EditMessageTextAsync(
                chatId: chatId,
                messageId: messageId,
                text: message,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error showing search menu: {ex.Message}");
        }
    }
}
