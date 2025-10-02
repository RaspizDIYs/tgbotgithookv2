using System.Collections.Generic;
using System.Timers;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
// using Telegram.Bot.Types.InputFiles; // deprecated in Telegram.Bot 19

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
    private readonly AchievementService _achievementService;
    private readonly Dictionary<long, NotificationSettings> _chatSettings = new();
    private readonly HashSet<string> _processedCallbacks = new();
    private readonly HashSet<int> _processedUpdateIds = new();
    private readonly Queue<(int id, DateTime ts)> _processedUpdateTimestamps = new();
    private readonly Dictionary<string, System.Timers.Timer> _messageTimers = new();
    private System.Timers.Timer? _dailySummaryTimer;

    public TelegramBotService(ITelegramBotClient botClient, GitHubService gitHubService, AchievementService achievementService)
    {
        _botClient = botClient;
        _gitHubService = gitHubService ?? throw new ArgumentNullException(nameof(gitHubService));
        _achievementService = achievementService ?? throw new ArgumentNullException(nameof(achievementService));

        // Настраиваем ежедневную сводку в 18:00 МСК
        SetupDailySummaryTimer();
        
        // Запускаем систему запланированных обновлений
        _ = StartScheduledUpdatesTimer();
    }

    public async Task HandleUpdateAsync(HttpContext context)
    {
        try
        {
            var update = await context.Request.ReadFromJsonAsync<Update>();
            if (update == null) return;

            // Идемпотентность: отбрасываем уже обработанные update.Id (на случай повторной доставки вебхука)
            CleanupProcessedUpdates();
            if (_processedUpdateIds.Contains(update.Id))
            {
                Console.WriteLine($"♻️ Duplicate update ignored: {update.Id}");
                return;
            }
            _processedUpdateIds.Add(update.Id);
            _processedUpdateTimestamps.Enqueue((update.Id, DateTime.UtcNow));

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

    private void CleanupProcessedUpdates()
    {
        // держим максимум 1000 id и TTL 10 минут
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        while (_processedUpdateTimestamps.Count > 0)
        {
            var (id, ts) = _processedUpdateTimestamps.Peek();
            if (_processedUpdateIds.Count > 1000 || ts < cutoff)
            {
                _processedUpdateTimestamps.Dequeue();
                _processedUpdateIds.Remove(id);
            }
            else
            {
                break;
            }
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

                case "/weekstats":
                    await ShowWeekSelectionAsync(chatId);
                    break;

                case "/rating":
                    await HandleRatingCommandAsync(chatId);
                    break;

                case "/trends":
                    await HandleTrendsCommandAsync(chatId);
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

                case "/achivelist":
                case "/achivementlist":
                case "/achievementlist":
                case "/achievlist":
                case "/achievements":
                case "/achievement":
                case "/achivement":
                    await ShowAchievementPageAsync(chatId, 0, null);
                    break;

                case "/leaderboard":
                case "/top":
                    await HandleLeaderboardCommandAsync(chatId);
                    break;

                case "/streaks":
                case "/streak":
                    await HandleStreaksCommandAsync(chatId);
                    break;
                case "/recalc":
                    await HandleRecalcCommandAsync(chatId);
                    break;

                case "/deep":
                    if (parts.Length > 1)
                    {
                        var deeplinkPath = string.Join(" ", parts.Skip(1)).Trim('"', '\'');
                        await HandleDeeplinkCommandAsync(chatId, deeplinkPath);
                    }
                    else
                    {
                        await _botClient.SendTextMessageAsync(chatId, "Использование: /deep <путь к файлу>\nПример: /deep src/components/Button.tsx\nИли: /deep src/components/Button.tsx:150:10", disableNotification: true);
                    }
                    break;

                case "/info":
                    await SendInfoMessageAsync(chatId);
                    break;

                case "/ratelimit":
                case "/limits":
                    await HandleRateLimitCommandAsync(chatId);
                    break;

                case "/cache":
                case "/cacheinfo":
                    await HandleCacheInfoCommandAsync(chatId);
                    break;

                case "/protection":
                case "/backup":
                    await HandleDataProtectionCommandAsync(chatId);
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
        var message = @"🤖 *GitHub Monitor Bot*
Мониторинг репозитория goodluckv2

📢 Уведомления о:
• Коммитах
• PR/MR
• CI/CD
• Релизах

💡 Выберите раздел из меню ниже:";

        var inlineKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📦 Git", "menu:git"),
                InlineKeyboardButton.WithCallbackData("📊 Stats", "menu:stats"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🖱️ Cursor", "menu:cursor"),
                InlineKeyboardButton.WithCallbackData("⚙️ Настройки", "/settings"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("ℹ️ Инфо", "/info"),
                InlineKeyboardButton.WithCallbackData("❓ Справка", "/help"),
            }
        });

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: message,
            parseMode: ParseMode.Markdown,
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
        var message = @"📋 *Справка по боту*

🏠 /start - Главное меню
ℹ️ /info - Подробная информация

📦 *Git - Работа с репозиторием:*
📊 /status - Статус репозитория
📝 /commits [ветка] [кол-во] - Коммиты
🌿 /branches - Список веток
🔄 /prs - Открытые PR
⚙️ /ci [ветка] - CI/CD статус
🚀 /deploy [среда] - Деплой
🔎 /search <запрос> - Поиск по коммитам
👥 /authors - Активные авторы
📁 /files <sha> - Файлы в коммите
📈 /ratelimit - GitHub API лимиты
💾 /cache - Информация о кэше

📊 *Stats - Статистика и достижения:*
📈 /laststats - Последняя статистика
📊 /weekstats - Статистика по неделям
🏆 /rating - Рейтинг разработчиков
📉 /trends - Тренды активности
🏅 /achievements - Список всех ачивок
🥇 /leaderboard - Таблица лидеров
🔥 /streaks - Топ стриков
🔄 /recalc - Пересчёт статистики

🖱️ *Cursor - Интеграция:*
🔗 /deep <путь> - Диплинк для Cursor
  Примеры:
  • /deep src/App.tsx
  • /deep src/App.tsx:42
  • /deep src/App.tsx:42:10

*Настройки*
⚙️ /settings - Настройки уведомлений

💡 *Подсказки:*
• Используйте главное меню для удобной навигации
• Команды без параметров показывают интерактивное меню";

        var inlineKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📦 Git", "menu:git"),
                InlineKeyboardButton.WithCallbackData("📊 Stats", "menu:stats"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🖱️ Cursor", "menu:cursor"),
                InlineKeyboardButton.WithCallbackData("⚙️ Настройки", "/settings"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("ℹ️ Инфо", "/info"),
                InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "/start"),
            }
        });

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: message,
            parseMode: ParseMode.Markdown,
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
            // Проверяем запланированную статистику
            var scheduledKey = "status_main";
            var scheduledStatus = _achievementService.GetScheduledStats(scheduledKey);
            
            if (scheduledStatus != null)
            {
                await _botClient.SendTextMessageAsync(chatId, scheduledStatus, parseMode: ParseMode.Markdown, disableNotification: true);
                return;
            }
            
            // Проверяем кэш статистики
            var cacheKey = $"status_{DateTime.UtcNow:yyyyMMddHH}";
            var cachedStatus = _achievementService.GetCachedStats(cacheKey);
            
            if (cachedStatus != null)
            {
                await _botClient.SendTextMessageAsync(chatId, cachedStatus, parseMode: ParseMode.Markdown, disableNotification: true);
                return;
            }
            
            // Получаем свежие данные
            var status = await _gitHubService.GetRepositoryStatusAsync();
            
            // Кэшируем результат
            _achievementService.CacheStats(cacheKey, status, "status");
            
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
            // Проверяем запланированную статистику
            var scheduledKey = $"commits_{branch}_{count}";
            var scheduledCommits = _achievementService.GetScheduledStats(scheduledKey);
            
            if (scheduledCommits != null)
            {
                await _botClient.SendTextMessageAsync(chatId, scheduledCommits, parseMode: ParseMode.Markdown, disableNotification: true);
                return;
            }
            
            // Проверяем кэш статистики
            var cacheKey = $"commits_{branch}_{count}_{DateTime.UtcNow:yyyyMMddHH}";
            var cachedCommits = _achievementService.GetCachedStats(cacheKey);
            
            if (cachedCommits != null)
            {
                await _botClient.SendTextMessageAsync(chatId, cachedCommits, parseMode: ParseMode.Markdown, disableNotification: true);
                return;
            }
            
            // Получаем свежие данные
            var commits = await _gitHubService.GetRecentCommitsAsync(branch, count);
            
            // Кэшируем результат
            _achievementService.CacheStats(cacheKey, commits, "commits");
            
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
            else if (data.StartsWith("week_stats:"))
            {
                Console.WriteLine("📊 Processing week stats selection");
                await HandleWeekStatsCallbackAsync(chatId, data, messageId);
            }
            else if (data == "search_menu")
            {
                Console.WriteLine("🔍 Processing search menu");
                await ShowSearchMenuAsync(chatId, messageId);
            }
            else if (data.StartsWith("achv:"))
            {
                Console.WriteLine("🏆 Processing achievement navigation");
                var parts = data.Split(':');
                if (parts.Length >= 3)
                {
                    var dir = parts[1];
                    if (int.TryParse(parts[2], out var idx))
                    {
                        var delta = dir == "next" ? 1 : dir == "prev" ? -1 : 0;
                        await ShowAchievementPageAsync(chatId, idx + delta, messageId);
                    }
                }
            }
            else if (data.StartsWith("menu:"))
            {
                Console.WriteLine($"📂 Processing submenu: {data}");
                await HandleSubmenuAsync(chatId, messageId, data);
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

    private async Task ShowAchievementPageAsync(long chatId, int index, int? messageIdToEdit)
    {
        var list = _achievementService.GetAllAchievements().OrderBy(a => a.Name).ToList();
        if (list.Count == 0)
        {
            await _botClient.SendTextMessageAsync(chatId, "🏆 Пока нет ачивок", disableNotification: true);
            return;
        }

        var count = list.Count;
        // нормализуем индекс
        var idx = ((index % count) + count) % count;
        var a = list[idx];

        var status = a.IsUnlocked ? "✅" : "❌";
        var holder = a.IsUnlocked && !string.IsNullOrEmpty(a.HolderName) ? $" (\u2014 {a.HolderName})" : "";
        var value = a.Value.HasValue ? $" [{a.Value}]" : "";
        var caption = $"{a.Emoji} *{a.Name}*\n{a.Description}{holder}{value}\n\n_{idx + 1}/{count}_";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new []
            {
                InlineKeyboardButton.WithCallbackData("⬅️", $"achv:prev:{idx}"),
                InlineKeyboardButton.WithCallbackData("➡️", $"achv:next:{idx}")
            },
            new [] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "/help") }
        });

        try
        {
            if (messageIdToEdit.HasValue && messageIdToEdit.Value != 0)
            {
                // Удаляем предыдущее сообщение, чтобы избежать ограничений редактирования media
                await DeleteMessageAsync(chatId, messageIdToEdit.Value);
            }

            var url = a.GifUrl?.Trim() ?? string.Empty;
            try
            {
                await _botClient.SendAnimationAsync(
                    chatId: chatId,
                    animation: InputFile.FromUri(url),
                    caption: caption,
                    parseMode: ParseMode.Markdown,
                    disableNotification: true,
                    replyMarkup: keyboard
                );
            }
            catch (Telegram.Bot.Exceptions.ApiRequestException apiEx)
            {
                // Авто-фолбэк: если это .gif с media.tenor.com, попробуем .mp4
                if (url.Contains("media.tenor.com") && url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                {
                    var mp4Url = url[..^4] + ".mp4";
                    Console.WriteLine($"⚠️ GIF failed, retrying MP4: {mp4Url}. Error: {apiEx.Message}");
                    await _botClient.SendAnimationAsync(
                        chatId: chatId,
                        animation: InputFile.FromUri(mp4Url),
                        caption: caption,
                        parseMode: ParseMode.Markdown,
                        disableNotification: true,
                        replyMarkup: keyboard
                    );
                }
                else
                {
                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to show achievement page: {ex.Message}");
            // Фоллбек: текст без гифки
            if (messageIdToEdit.HasValue && messageIdToEdit.Value != 0)
            {
                await _botClient.EditMessageTextAsync(chatId, messageIdToEdit.Value, caption, parseMode: ParseMode.Markdown, replyMarkup: keyboard);
            }
            else
            {
                await _botClient.SendTextMessageAsync(chatId, caption, parseMode: ParseMode.Markdown, disableNotification: true, replyMarkup: keyboard);
            }
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
            var owner = _gitHubService.OwnerName;
            var repo = _gitHubService.RepoName;
            var fallbackMessage = $"🚀 *Новый пуш в {owner}/{repoName}*\n\n" +
                                 $"📦 Коммит: `{commitSha[..8]}`\n" +
                                 $"🔗 [Посмотреть на GitHub](https://github.com/{owner}/{repo}/commit/{commitSha})";

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

    private string NormalizeTenorUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        var u = url.Trim();
        // Replace known host variants to media.tenor.com
        if (u.Contains("tenor.com") && !u.Contains("media.tenor.com"))
        {
            u = u.Replace("https://tenor.com/view/", "https://media.tenor.com/")
                 .Replace("https://tenor.com/ru/view/", "https://media.tenor.com/");
        }
        // If page URL slipped through, leave it, fallback in sender will try mp4
        return u;
    }

    private void SetupDailySummaryTimer()
    {
        _dailySummaryTimer = new System.Timers.Timer();
        if (_dailySummaryTimer != null)
        {
            _dailySummaryTimer.Elapsed += async (sender, e) => await SendDailySummaryAsync();
            _dailySummaryTimer.AutoReset = false; // Отключаем автоповтор

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

            // Формируем сообщение со сводкой с учетом МСК
            var mskTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
            var yesterdayMsk = TimeZoneInfo.ConvertTime(DateTime.UtcNow.AddDays(-1), mskTimeZone);
            
            var title = targetChatId.HasValue
                ? $"📊 *Запрошенная сводка за {yesterdayMsk:dd.MM.yyyy}*"
                : $"📊 *Ежедневная сводка за {yesterdayMsk:dd.MM.yyyy}*";
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
                message = $"🍺 *Выходной! {yesterdayMsk:dd.MM.yyyy}*\n\n";
                message += "Никто не коммитил — значит отдыхаем! 🎉\n\n";
                message += "🍻 Пьём пиво и наслаждаемся жизнью!";
                
                // Пробуем отправить анимацию с Tenor (URL из переменной окружения TENOR_WEEKEND_GIF)
                var weekendGif = Environment.GetEnvironmentVariable("TENOR_WEEKEND_GIF");
                if (!string.IsNullOrWhiteSpace(weekendGif))
                {
                    try
                    {
                        await _botClient.SendAnimationAsync(
                            chatId: chatId,
                            animation: InputFile.FromUri(NormalizeTenorUrl(weekendGif)),
                            caption: message,
                            parseMode: ParseMode.Markdown,
                            disableNotification: targetChatId.HasValue
                        );
                        var weekendSummaryType = targetChatId.HasValue ? "requested" : "automatic";
                        Console.WriteLine($"✅ {weekendSummaryType} weekend summary sent to chat {chatId} (Tenor GIF)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Failed to send Tenor GIF: {ex.Message}. Sending text fallback.");
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: message,
                    parseMode: ParseMode.Markdown,
                    disableNotification: targetChatId.HasValue
                );
                    }
                }
                else
                {
                    // Fallback только текст, без внешних хостов
                    await _botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: message,
                        parseMode: ParseMode.Markdown,
                        disableNotification: targetChatId.HasValue
                    );
                var weekendSummaryType = targetChatId.HasValue ? "requested" : "automatic";
                    Console.WriteLine($"✅ {weekendSummaryType} weekend summary sent to chat {chatId} (text only)");
                }

                // Перепланируем таймер на следующий день только для автоматических сводок
                if (_dailySummaryTimer != null && !targetChatId.HasValue)
                {
                    _dailySummaryTimer.Stop();
                    _dailySummaryTimer.Interval = 24 * 60 * 60 * 1000;
                    _dailySummaryTimer.Start();
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
                _dailySummaryTimer.Stop();
                _dailySummaryTimer.Interval = 24 * 60 * 60 * 1000; // 24 часа в миллисекундах
                _dailySummaryTimer.Start();
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
            // Проверяем запланированную статистику
            var scheduledKey = "authors_main";
            var scheduledAuthors = _achievementService.GetScheduledStats(scheduledKey);
            
            if (scheduledAuthors != null)
            {
            var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "/help") }
                });

                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: scheduledAuthors,
                    parseMode: ParseMode.Markdown,
                    disableNotification: true,
                    replyMarkup: keyboard);
                return;
            }
            
            // Проверяем кэш статистики
            var cacheKey = $"authors_{DateTime.UtcNow:yyyyMMddHH}";
            var cachedAuthors = _achievementService.GetCachedStats(cacheKey);
            
            if (cachedAuthors != null)
            {
                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "/help") }
                });

                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: cachedAuthors,
                    parseMode: ParseMode.Markdown,
                    disableNotification: true,
                    replyMarkup: keyboard);
                return;
            }
            
            // Получаем свежие данные
            var authors = await _gitHubService.GetActiveAuthorsAsync();
            
            // Кэшируем результат
            _achievementService.CacheStats(cacheKey, authors, "authors");
            
            var keyboard2 = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "/help") }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: authors,
                parseMode: ParseMode.Markdown,
                disableNotification: true,
                replyMarkup: keyboard2);
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

    private async Task ShowWeekSelectionAsync(long chatId)
    {
        try
        {
            var message = "📊 *Выберите неделю для статистики:*\n\n";
            
            var buttons = new List<InlineKeyboardButton[]>();
            
            // Добавляем кнопки для последних 4 недель
            for (int i = 0; i < 4; i++)
            {
                var weekStart = DateTime.Now.AddDays(-7 * i - (int)DateTime.Now.DayOfWeek + 1);
                var weekEnd = weekStart.AddDays(6);
                var weekText = $"{weekStart:dd.MM} - {weekEnd:dd.MM}";
                
                if (i == 0) weekText += " (текущая)";
                
                buttons.Add(new[] { InlineKeyboardButton.WithCallbackData($"📅 {weekText}", $"week_stats:{i}") });
            }

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
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка показа недель: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandleRatingCommandAsync(long chatId)
    {
        try
        {
            var rating = await _gitHubService.GetDeveloperRatingAsync();
            
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "/help") }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: rating,
                parseMode: ParseMode.Markdown,
                disableNotification: true,
                replyMarkup: keyboard
            );
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения рейтинга: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandleTrendsCommandAsync(long chatId)
    {
        try
        {
            var trends = await _gitHubService.GetActivityTrendsAsync();
            
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "/help") }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: trends,
                parseMode: ParseMode.Markdown,
                disableNotification: true,
                replyMarkup: keyboard
            );
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения трендов: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandleWeekStatsCallbackAsync(long chatId, string callbackData, int messageId)
    {
        try
        {
            var parts = callbackData.Split(':');
            if (parts.Length < 2) return;

            var weekOffset = int.Parse(parts[1]);
            
            // Удаляем сообщение с выбором недели
            await DeleteMessageAsync(chatId, messageId);

            var weekStats = await _gitHubService.GetWeeklyStatsAsync(weekOffset);

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("📊 Выбрать другую неделю", "/weekstats") },
                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "/help") }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: weekStats,
                parseMode: ParseMode.Markdown,
                disableNotification: true,
                replyMarkup: keyboard
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error handling week stats callback: {ex.Message}");
        }
    }

    private async Task HandleAchievementsCommandAsync(long chatId)
    {
        try
        {
            var achievements = _achievementService.GetAllAchievements();
            
            if (!achievements.Any())
            {
                await _botClient.SendTextMessageAsync(chatId, "🏆 Пока никто не получил ачивок!\n\nНачните коммитить, чтобы получить первые награды!", disableNotification: true);
                return;
            }

            var message = "🏆 *Список ачивок*\n\n";
            
            foreach (var achievement in achievements.OrderBy(a => a.Name))
            {
                var status = achievement.IsUnlocked ? "✅" : "❌";
                var holder = achievement.IsUnlocked && !string.IsNullOrEmpty(achievement.HolderName) 
                    ? $" ({achievement.HolderName})" 
                    : "";
                var value = achievement.Value.HasValue ? $" [{achievement.Value}]" : "";
                
                message += $"{status} {achievement.Emoji} *{achievement.Name}*\n";
                message += $"   {achievement.Description}{holder}{value}\n\n";
            }

            await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, disableNotification: true);

            // Отправляем гифки для каждой ачивки (Tenor URL поддерживается Telegram без API)
            foreach (var achievement in achievements.Where(a => a.IsUnlocked))
            {
                try
                {
                    await _botClient.SendAnimationAsync(
                        chatId: chatId,
                        animation: InputFile.FromUri(NormalizeTenorUrl(achievement.GifUrl)),
                        caption: $"{achievement.Emoji} *{achievement.Name}*\n{achievement.Description}",
                        parseMode: ParseMode.Markdown,
                        disableNotification: true
                    );
                    
                    // Небольшая задержка между гифками
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка отправки гифки для ачивки {achievement.Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения ачивок: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandleLeaderboardCommandAsync(long chatId)
    {
        try
        {
            var topUsers = _achievementService.GetTopUsers(10);
            var topStreakUsers = _achievementService.GetTopUsersByStreak(5);
            
            if (!topUsers.Any())
            {
                await _botClient.SendTextMessageAsync(chatId, "📊 Пока нет статистики!\n\nНачните коммитить, чтобы попасть в таблицу лидеров!", disableNotification: true);
                return;
            }

            var message = "🏆 *Таблица лидеров*\n\n";
            
            // Основная таблица по коммитам
            message += "📊 *По количеству коммитов:*\n";
            for (int i = 0; i < topUsers.Count; i++)
            {
                var user = topUsers[i];
                var medal = i switch
                {
                    0 => "🥇",
                    1 => "🥈", 
                    2 => "🥉",
                    _ => $"#{i + 1}"
                };
                
                var streakEmoji = _achievementService.GetStreakEmoji(user.LongestStreak);
                
                message += $"{medal} *{user.DisplayName}*\n";
                message += $"   📊 Коммитов: {user.TotalCommits}\n";
                message += $"   ⚡ Макс. строк: {user.MaxLinesChanged}\n";
                message += $"   {streakEmoji} Стрик: {user.LongestStreak} дн.\n";
                message += $"   🧪 Тесты: {user.TestCommits} | 🚀 Релизы: {user.ReleaseCommits}\n";
                message += $"   🐛 Баги: {user.BugFixCommits} | ✨ Фичи: {user.FeatureCommits}\n\n";
            }

            // Топ по стрикам
            if (topStreakUsers.Any())
            {
                message += "🔥 *Топ стриков:*\n";
                for (int i = 0; i < topStreakUsers.Count; i++)
                {
                    var user = topStreakUsers[i];
                    var streakEmoji = _achievementService.GetStreakEmoji(user.LongestStreak);
                    message += $"{streakEmoji} *{user.DisplayName}* - {user.LongestStreak} дн.\n";
                }
            }

            await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, disableNotification: true);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения таблицы лидеров: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandleStreaksCommandAsync(long chatId)
    {
        try
        {
            var topStreakUsers = _achievementService.GetTopUsersByStreak(10);
            
            if (!topStreakUsers.Any())
            {
                await _botClient.SendTextMessageAsync(chatId, "🔥 Пока нет стриков!\n\nНачните коммитить каждый день, чтобы создать стрик!", disableNotification: true);
                return;
            }

            var message = "🔥 *Топ стриков*\n\n";
            message += "Подсказка: чем больше стрик, тем больше 🔥\n\n";
            
            for (int i = 0; i < topStreakUsers.Count; i++)
            {
                var user = topStreakUsers[i];
                var medal = i switch
                {
                    0 => "🥇",
                    1 => "🥈", 
                    2 => "🥉",
                    _ => $"#{i + 1}"
                };
                
                var streakEmoji = _achievementService.GetStreakEmoji(user.LongestStreak);
                message += $"{medal} *{user.DisplayName}* — {user.LongestStreak} дн. {streakEmoji}\n";
            }

            await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, disableNotification: true);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения стриков: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandleRecalcCommandAsync(long chatId)
    {
        try
        {
            // Проверяем rate limit перед началом
            var (remaining, limit, resetTime) = await _gitHubService.GetRateLimitAsync();
            
            if (remaining < 500)
            {
                var timeUntilReset = resetTime - DateTime.UtcNow;
                var message = $"⚠️ *Предупреждение о лимитах GitHub API*\n\n" +
                             $"📊 Доступно запросов: {remaining}/{limit}\n" +
                             $"⏰ Сброс через: {timeUntilReset.Minutes} мин\n\n" +
                             $"⚡ Пересчёт может израсходовать до 2000+ запросов!\n\n" +
                             $"Рекомендации:\n" +
                             $"• Подождите до сброса лимита\n" +
                             $"• Или используйте /recalc light (только основная ветка)";
                
                await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, disableNotification: true);
                return;
            }

            await _botClient.SendTextMessageAsync(chatId, $"🔄 Запускаю пересчёт ачивок...\n\n📊 Доступно запросов: {remaining}/{limit}", disableNotification: true);

            // Сбрасываем все данные
            _achievementService.ResetAllData();

            // Получаем ветки; если пусто — пробуем основную ветку
            var branches = await _gitHubService.GetBranchesListAsync();
            if (!branches.Any())
            {
                var def = await _gitHubService.TryGetDefaultBranchAsync();
                if (!string.IsNullOrEmpty(def)) branches = new List<string> { def };
            }

            var totalProcessed = 0;
            var branchCount = 0;
            var startTime = DateTime.UtcNow;
            
            foreach (var branch in branches)
            {
                branchCount++;
                await _botClient.SendTextMessageAsync(chatId, $"📊 Обрабатываю ветку {branchCount}/{branches.Count}: `{branch}`...", parseMode: ParseMode.Markdown, disableNotification: true);
                
                var history = await _gitHubService.GetAllCommitsWithStatsForBranchAsync(branch, 2000);
                foreach (var c in history)
                {
                    _achievementService.ProcessCommitBatch(c.Author, c.Email, c.Message, c.Date, c.Additions, c.Deletions);
                }
                totalProcessed += history.Count;
                
                // Показываем промежуточный прогресс
                var (currentRemaining, _, _) = await _gitHubService.GetRateLimitAsync();
                var used = remaining - currentRemaining;
                Console.WriteLine($"📊 Branch {branch}: {history.Count} commits, API calls used: {used}");
            }

            // Сохраняем все изменения один раз в конце
            _achievementService.SaveAll();

            var duration = DateTime.UtcNow - startTime;
            var (finalRemaining, _, _) = await _gitHubService.GetRateLimitAsync();
            var totalUsed = remaining - finalRemaining;

            await _botClient.SendTextMessageAsync(chatId, 
                $"✅ *Пересчёт завершён!*\n\n" +
                $"📊 Обработано коммитов: {totalProcessed}\n" +
                $"🌿 Веток: {branchCount}\n" +
                $"⏱️ Время: {duration.TotalSeconds:F1} сек\n" +
                $"📈 API запросов: {totalUsed}\n" +
                $"💾 Осталось: {finalRemaining}/{limit}\n\n" +
                $"💾 Данные сохранены", 
                parseMode: ParseMode.Markdown, 
                disableNotification: true);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка пересчёта: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandleRateLimitCommandAsync(long chatId)
    {
        try
        {
            var (remaining, limit, resetTime) = await _gitHubService.GetRateLimitAsync();
            var timeUntilReset = resetTime - DateTime.UtcNow;
            var usedPercent = limit > 0 ? ((limit - remaining) * 100.0 / limit) : 0;

            string status;
            string emoji;
            
            if (remaining > 3000)
            {
                status = "Отлично";
                emoji = "✅";
            }
            else if (remaining > 1000)
            {
                status = "Хорошо";
                emoji = "🟢";
            }
            else if (remaining > 500)
            {
                status = "Умеренно";
                emoji = "🟡";
            }
            else if (remaining > 100)
            {
                status = "Низкий";
                emoji = "🟠";
            }
            else
            {
                status = "Критично";
                emoji = "🔴";
            }

            var message = $"{emoji} *GitHub API Rate Limit*\n\n" +
                         $"📊 *Статус:* {status}\n" +
                         $"📈 *Доступно:* {remaining}/{limit} ({usedPercent:F1}% использовано)\n" +
                         $"⏰ *Сброс через:* {(timeUntilReset.TotalMinutes > 0 ? $"{timeUntilReset.Minutes} мин {timeUntilReset.Seconds} сек" : "скоро")}\n" +
                         $"🕐 *Время сброса:* {resetTime.ToLocalTime():HH:mm:ss}\n\n" +
                         $"💡 *Рекомендации:*\n";

            if (remaining < 500)
            {
                message += "• ⚠️ Избегайте /recalc до сброса\n";
                message += "• Используйте простые команды\n";
            }
            else if (remaining < 1000)
            {
                message += "• ⚡ /recalc можно использовать осторожно\n";
                message += "• Следите за лимитом\n";
            }
            else
            {
                message += "• ✅ Все команды доступны\n";
                message += "• /recalc безопасно использовать\n";
            }

            message += $"\n📝 *Операции и их стоимость:*\n" +
                      $"• /status, /commits, /branches: 1-5 запросов\n" +
                      $"• /recalc: ~2000+ запросов (зависит от веток)\n" +
                      $"• Вебхуки GitHub: 1 запрос на коммит";

            await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, disableNotification: true);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения лимитов: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandleCacheInfoCommandAsync(long chatId)
    {
        try
        {
            var (userStatsCount, achievementsCount, processedShasCount, totalSizeBytes) = _achievementService.GetCacheInfo();
            
            var sizeKB = totalSizeBytes / 1024.0;
            var sizeMB = sizeKB / 1024.0;
            
            string sizeText;
            if (sizeMB >= 1)
                sizeText = $"{sizeMB:F2} MB";
            else
                sizeText = $"{sizeKB:F1} KB";

            var message = $"💾 *Информация о кэше*\n\n" +
                         $"📊 *Статистика пользователей:* {userStatsCount}\n" +
                         $"🏆 *Достижения:* {achievementsCount}\n" +
                         $"📝 *Обработанные SHA:* {processedShasCount}\n" +
                         $"💿 *Общий размер:* {sizeText}\n\n" +
                         $"⚙️ *Настройки автоочистки:*\n" +
                         $"• Максимум SHA: 10,000\n" +
                         $"• Неактивные пользователи: >90 дней\n" +
                         $"• Максимум неактивных: 50\n\n" +
                         $"🧹 *Автоочистка происходит:*\n" +
                         $"• При сохранении данных\n" +
                         $"• При пересчёте (/recalc)\n" +
                         $"• Ручная очистка: /cleancache\n\n" +
                         $"💡 *Рекомендации:*\n" +
                         $"• Регулярно используйте /cleancache\n" +
                         $"• Мониторьте размер кэша\n" +
                         $"• Старые данные удаляются автоматически";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🧹 Очистить кэш", "/cleancache"),
                    InlineKeyboardButton.WithCallbackData("📈 API лимиты", "/ratelimit"),
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "/start")
                }
            });

            await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, disableNotification: true, replyMarkup: keyboard);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения информации о кэше: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandleCleanCacheCommandAsync(long chatId)
    {
        try
        {
            await _botClient.SendTextMessageAsync(chatId, "🧹 Запускаю очистку кэша...", disableNotification: true);
            
            var beforeInfo = _achievementService.GetCacheInfo();
            _achievementService.ForceCleanup();
            var afterInfo = _achievementService.GetCacheInfo();
            
            var usersRemoved = beforeInfo.userStatsCount - afterInfo.userStatsCount;
            var shasRemoved = beforeInfo.processedShasCount - afterInfo.processedShasCount;
            var sizeSaved = beforeInfo.totalSizeBytes - afterInfo.totalSizeBytes;
            
            var sizeKB = sizeSaved / 1024.0;
            var sizeMB = sizeKB / 1024.0;
            
            string sizeText;
            if (sizeMB >= 1)
                sizeText = $"{sizeMB:F2} MB";
            else
                sizeText = $"{sizeKB:F1} KB";

            var message = $"✅ *Очистка кэша завершена!*\n\n" +
                         $"📊 *Результаты:*\n" +
                         $"• Удалено пользователей: {usersRemoved}\n" +
                         $"• Удалено SHA: {shasRemoved}\n" +
                         $"• Освобождено места: {sizeText}\n\n" +
                         $"📈 *Текущее состояние:*\n" +
                         $"• Пользователи: {afterInfo.userStatsCount}\n" +
                         $"• Достижения: {afterInfo.achievementsCount}\n" +
                         $"• SHA: {afterInfo.processedShasCount}\n\n" +
                         $"💾 Данные сохранены";

            await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, disableNotification: true);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка очистки кэша: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandleSubmenuAsync(long chatId, int messageId, string menuData)
    {
        try
        {
            var menuType = menuData.Split(':')[1];

            switch (menuType)
            {
                case "git":
                    await ShowGitMenuAsync(chatId, messageId);
                    break;
                case "stats":
                    await ShowStatsMenuAsync(chatId, messageId);
                    break;
                case "cursor":
                    await ShowCursorMenuAsync(chatId, messageId);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error handling submenu: {ex.Message}");
        }
    }

    private async Task ShowGitMenuAsync(long chatId, int messageId)
    {
        var message = "📦 *Git - Работа с репозиторием*\n\n" +
                     "Выберите действие:";

        var keyboard = new InlineKeyboardMarkup(new[]
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
                InlineKeyboardButton.WithCallbackData("🔍 Поиск", "search_menu"),
                InlineKeyboardButton.WithCallbackData("👥 Авторы", "/authors"),
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
            replyMarkup: keyboard
        );
    }

    private async Task ShowStatsMenuAsync(long chatId, int messageId)
    {
        var message = "📊 *Stats - Статистика и достижения*\n\n" +
                     "Выберите раздел:";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📈 Последняя статистика", "/laststats"),
                InlineKeyboardButton.WithCallbackData("📊 По неделям", "/weekstats"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🏆 Рейтинг", "/rating"),
                InlineKeyboardButton.WithCallbackData("📉 Тренды", "/trends"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🏅 Ачивки", "/achivelist"),
                InlineKeyboardButton.WithCallbackData("🥇 Лидеры", "/leaderboard"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🔥 Стрики", "/streaks"),
                InlineKeyboardButton.WithCallbackData("📈 API лимиты", "/ratelimit"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("💾 Кэш", "/cache"),
                InlineKeyboardButton.WithCallbackData("🔄 Пересчёт", "/recalc"),
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
            replyMarkup: keyboard
        );
    }

    private async Task ShowCursorMenuAsync(long chatId, int messageId)
    {
        var message = "🖱️ *Cursor - Интеграция с редактором*\n\n" +
                     "Команды для работы с Cursor:\n\n" +
                     "📝 `/deep <путь>` - Создать диплинк\n" +
                     "Примеры:\n" +
                     "• `/deep src/App.tsx`\n" +
                     "• `/deep src/App.tsx:42`\n" +
                     "• `/deep src/App.tsx:42:10`\n\n" +
                     "ℹ️ Подробнее: /info";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("ℹ️ Подробная инфо", "/info"),
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
            replyMarkup: keyboard
        );
    }

    private async Task SendInfoMessageAsync(long chatId)
    {
        var message = @"ℹ️ *Информация о боте*

🤖 *GitHub Monitor Bot*
Бот для мониторинга репозитория RaspizDIYs/goodluckv2

📦 *Git функционал:*
• Отслеживание коммитов, PR, CI/CD
• Статистика по веткам и авторам
• Поиск по истории коммитов

📊 *Статистика и достижения:*
• Система ачивок и рейтингов
• Стрики коммитов
• Детальная аналитика активности
• Мониторинг лимитов GitHub API

🖱️ *Интеграция с Cursor:*
Команда `/deep` создаёт диплинк для открытия файла в Cursor.

*Примеры использования:*
• `/deep src/components/Button.tsx`
  Откроет файл Button.tsx

• `/deep src/components/Button.tsx:150`
  Откроет файл на строке 150

• `/deep src/components/Button.tsx:150:10`
  Откроет файл на строке 150, колонке 10

*Формат диплинка:*
`cursor://file/{workspace}/{path}?line={line}&column={column}`

*Workspace репозитория:*
goodluckv2 (настраивается через GOODLUCK_WORKSPACE_PATH)

📈 *Мониторинг API:*
Команда `/ratelimit` показывает текущие лимиты GitHub API.

⚠️ *Важно:*
• GitHub API: 5000 запросов/час
• `/recalc` использует ~2000+ запросов
• Проверяйте лимиты перед пересчётом
• Данные кешируются в JSON файлах

*Хранение данных*
Бот использует JSON файлы как память
- user_stats.json - статистика пользователей
- achievements.json - полученные достижения
- processed_shas.json - обработанные коммиты

*Умная очистка кэша*
- Автоматическая очистка старых данных
- Максимум 10,000 SHA в кэше
- Удаление неактивных пользователей (более 90 дней)
- Команды cache, cleancache

*Настройки*
Используйте settings для настройки уведомлений

*Справка*
help - полный список команд";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "/start"),
                InlineKeyboardButton.WithCallbackData("❓ Справка", "/help"),
            }
        });

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: message,
            parseMode: ParseMode.Markdown,
            disableWebPagePreview: true,
            disableNotification: true,
            replyMarkup: keyboard
        );
    }

    private async Task HandleDeeplinkCommandAsync(long chatId, string pathInput)
    {
        try
        {
            var workspacePath = Environment.GetEnvironmentVariable("GOODLUCK_WORKSPACE_PATH") 
                              ?? Environment.GetEnvironmentVariable("CURSOR_WORKSPACE_PATH")
                              ?? "D:/Git/goodluckv2";
            
            string relativePath = pathInput;
            int? line = null;
            int? column = null;

            if (pathInput.Contains(':'))
            {
                var parts = pathInput.Split(':');
                relativePath = parts[0];
                
                if (parts.Length > 1 && int.TryParse(parts[1], out var lineNum))
                {
                    line = lineNum;
                }
                
                if (parts.Length > 2 && int.TryParse(parts[2], out var colNum))
                {
                    column = colNum;
                }
            }

            relativePath = relativePath.Replace('\\', '/').TrimStart('/');
            workspacePath = workspacePath.Replace('\\', '/');
            
            var deeplink = $"cursor://file/{workspacePath}/{relativePath}";
            
            if (line.HasValue)
            {
                deeplink += $"?line={line.Value}";
                if (column.HasValue)
                {
                    deeplink += $"&column={column.Value}";
                }
            }

            var message = "🔗 *Диплинк для Cursor*\n\n";
            message += $"📁 Файл: `{relativePath}`\n";
            if (line.HasValue)
            {
                message += $"📍 Строка: {line.Value}";
                if (column.HasValue)
                {
                    message += $", Колонка: {column.Value}";
                }
                message += "\n";
            }
            message += $"📦 Репозиторий: goodluckv2\n";
            message += $"\n🔗 Ссылка:\n`{deeplink}`\n\n";
            message += "Нажми кнопку ниже, чтобы открыть в Cursor";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithUrl("🖱️ Открыть в Cursor", deeplink)
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "/start")
                }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: message,
                parseMode: ParseMode.Markdown,
                disableWebPagePreview: true,
                disableNotification: true,
                replyMarkup: keyboard
            );

            Console.WriteLine($"✅ Deeplink generated for goodluckv2: {deeplink}");
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка генерации диплинка: {ex.Message}", disableNotification: true);
        }
    }
    
    private Task StartScheduledUpdatesTimer()
    {
        // Таймер для проверки расписания каждые 30 минут
        var scheduledTimer = new System.Timers.Timer(TimeSpan.FromMinutes(30).TotalMilliseconds);
        scheduledTimer.Elapsed += async (sender, e) => 
        {
            try
            {
                await CheckScheduledUpdates();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Критическая ошибка в таймере обновлений: {ex.Message}");
            }
        };
        scheduledTimer.AutoReset = true;
        scheduledTimer.Start();
        
        Console.WriteLine("⏰ Система запланированных обновлений запущена (проверка каждые 30 минут)");
        return Task.CompletedTask;
    }
    
    private async Task CheckScheduledUpdates()
    {
        try
        {
            if (!_achievementService.ShouldUpdateScheduledStats())
            {
                return;
            }
            
            Console.WriteLine("🔄 Начинаю запланированное обновление статистики...");
            
            // Проверяем API лимиты
            var (remaining, limit, resetTime) = await _gitHubService.GetRateLimitAsync();
            
            if (remaining < _achievementService.GetMinApiCallsThreshold())
            {
                Console.WriteLine($"⚠️ Пропуск запланированного обновления - мало API вызовов: {remaining}/{limit}");
                Console.WriteLine($"⏰ Следующая проверка через час или при сбросе лимитов");
                return;
            }
            
            // Создаем резервную копию перед обновлением
            _achievementService.CreateBackup();
            
            // Обновляем все статистические данные поочередно
            var success = await UpdateAllScheduledStatsSequentially();
            
            if (success)
            {
                // Отмечаем время обновления
                _achievementService.MarkScheduledUpdate();
                
                // Очищаем старые данные только после успешного обновления
                _achievementService.ClearOldScheduledStats();
                
                // Очищаем резервную копию
                _achievementService.ClearBackup();
                
                Console.WriteLine($"✅ Запланированное обновление завершено успешно");
            }
            else
            {
                // Восстанавливаем из резервной копии при сбое
                Console.WriteLine("🔄 Восстанавливаю данные из резервной копии...");
                _achievementService.RestoreFromBackup();
            }
            
            // Проверяем финальные лимиты
            var (finalRemaining, _, _) = await _gitHubService.GetRateLimitAsync();
            Console.WriteLine($"📊 API вызовов осталось: {finalRemaining}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Ошибка запланированного обновления: {ex.Message}");
            
            // Восстанавливаем из резервной копии при критической ошибке
            if (_achievementService.IsBackupValid())
            {
                Console.WriteLine("🔄 Восстанавливаю данные из резервной копии после ошибки...");
                _achievementService.RestoreFromBackup();
            }
        }
    }
    
    private async Task<bool> UpdateAllScheduledStatsSequentially()
    {
        try
        {
            var updateTasks = new List<(string key, string type, string parameters, Func<Task<string>> getData)>();
            
            var task1 = ("status_main", "status", "", (Func<Task<string>>)(() => _gitHubService.GetRepositoryStatusAsync()));
            var task2 = ("authors_main", "authors", "", (Func<Task<string>>)(() => _gitHubService.GetActiveAuthorsAsync()));
            var task3 = ("weekly_0", "weekly", "", (Func<Task<string>>)(() => _gitHubService.GetWeeklyStatsAsync()));
            var task4 = ("achievements_main", "achievements", "", (Func<Task<string>>)(() => Task.FromResult(_achievementService.GetAchievementStats())));
            var task5 = ("streaks_main", "streaks", "", (Func<Task<string>>)(() => Task.FromResult(_achievementService.GetStreaks())));
            var task6 = ("rating_main", "rating", "", (Func<Task<string>>)(() => Task.FromResult(_achievementService.GetRating())));
            var task7 = ("leaderboard_main", "leaderboard", "", (Func<Task<string>>)(() => Task.FromResult(_achievementService.GetLeaderboard())));
            
            updateTasks.Add(task1);
            updateTasks.Add(task2);
            updateTasks.Add(task3);
            updateTasks.Add(task4);
            updateTasks.Add(task5);
            updateTasks.Add(task6);
            updateTasks.Add(task7);
            
            var successCount = 0;
            var totalTasks = updateTasks.Count;
            
            foreach (var task in updateTasks)
            {
                try
                {
                    // Проверяем API лимиты перед каждым обновлением
                    var (remaining, limit, resetTime) = await _gitHubService.GetRateLimitAsync();
                    
                    if (remaining < _achievementService.GetMinApiCallsThreshold())
                    {
                        Console.WriteLine($"⚠️ Прерывание обновления - мало API вызовов: {remaining}/{limit}");
                        Console.WriteLine($"⏰ Сброс лимитов в: {resetTime:HH:mm dd.MM.yyyy}");
                        break;
                    }
                    
                    Console.WriteLine($"🔄 Обновляю {task.type}...");
                    
                    // Получаем данные
                    var data = await task.getData();
                    
                    // Проверяем, что данные не пустые
                    if (string.IsNullOrWhiteSpace(data))
                    {
                        Console.WriteLine($"⚠️ Получены пустые данные для {task.type}, пропускаю");
                        continue;
                    }
                    
                    // Безопасно сохраняем данные
                    var saved = _achievementService.SafeSaveScheduledStats(task.key, data, task.type, task.parameters);
                    
                    if (saved)
                    {
                        successCount++;
                        Console.WriteLine($"✅ {task.type} обновлен успешно");
                    }
                    else
                    {
                        Console.WriteLine($"❌ Ошибка сохранения {task.type}");
                    }
                    
                    // Небольшая пауза между обновлениями
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Ошибка обновления {task.type}: {ex.Message}");
                }
            }
            
            // Обновляем коммиты для основных веток (если остались API вызовы)
            try
            {
                var (remaining, _, _) = await _gitHubService.GetRateLimitAsync();
                if (remaining >= _achievementService.GetMinApiCallsThreshold())
                {
                    var branches = await _gitHubService.GetBranchesListAsync();
                    foreach (var branch in branches.Take(3))
                    {
                        var commits = await _gitHubService.GetRecentCommitsAsync(branch, 10);
                        if (!string.IsNullOrWhiteSpace(commits))
                        {
                            var saved = _achievementService.SafeSaveScheduledStats($"commits_{branch}_10", commits, "commits", branch);
                            if (saved) successCount++;
                        }
                        
                        // Проверяем лимиты после каждого обновления коммитов
                        var (currentRemaining, _, _) = await _gitHubService.GetRateLimitAsync();
                        if (currentRemaining < _achievementService.GetMinApiCallsThreshold())
                        {
                            Console.WriteLine($"⚠️ Прерывание обновления коммитов - мало API вызовов: {currentRemaining}");
                            break;
                        }
                        
                        await Task.Delay(1000);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка обновления коммитов: {ex.Message}");
            }
            
            var success = successCount >= totalTasks * 0.7; // Считаем успешным если обновлено 70% задач
            
            Console.WriteLine($"📊 Результат обновления: {successCount}/{totalTasks} задач выполнено успешно");
            
            // Проверяем целостность данных
            var isValid = _achievementService.ValidateDataIntegrity();
            if (!isValid)
            {
                Console.WriteLine("⚠️ Обнаружены проблемы с целостностью данных");
                return false;
            }
            
            return success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Критическая ошибка поочередного обновления: {ex.Message}");
            return false;
        }
    }
    
    private async Task HandleScheduledInfoCommandAsync(long chatId)
    {
        try
        {
            var (count, sizeBytes, byType) = _achievementService.GetScheduledStatsInfo();
            var mskTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
            var nowMsk = TimeZoneInfo.ConvertTime(DateTime.UtcNow, mskTimeZone);
            
            var message = $"⏰ *Информация о запланированных обновлениях*\n\n" +
                         $"🕐 *Расписание обновлений:*\n" +
                         $"• 09:00 МСК\n" +
                         $"• 18:00 МСК\n" +
                         $"• 00:00 МСК\n\n" +
                         $"📊 *Текущее состояние:*\n" +
                         $"• Записей в кэше: {count}\n" +
                         $"• Размер данных: {FormatBytes(sizeBytes)}\n" +
                         $"• По типам:\n";
            
            foreach (var type in byType)
            {
                message += $"  - {type.Key}: {type.Value}\n";
            }
            
            message += $"\n🔄 *Автоматически обновляются:*\n" +
                      $"• Статус репозитория\n" +
                      $"• Авторы и коммиты\n" +
                      $"• Недельная статистика\n" +
                      $"• Ачивки и стрики\n" +
                      $"• Рейтинг и лидерборд\n\n" +
                      $"💾 *Управление данными:*\n" +
                      $"• Данные сохраняются в JSON\n" +
                      $"• Старые данные очищаются через 3 дня\n" +
                      $"• Проверка расписания каждые 30 минут\n" +
                      $"• Текущее время МСК: {nowMsk:HH:mm dd.MM.yyyy}";
            
            await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, disableNotification: true);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения информации о расписании: {ex.Message}", disableNotification: true);
        }
    }
    
    private async Task HandleDataProtectionCommandAsync(long chatId)
    {
        try
        {
            var (remaining, limit, resetTime) = await _gitHubService.GetRateLimitAsync();
            var isValid = _achievementService.ValidateDataIntegrity();
            var hasBackup = _achievementService.IsBackupValid();
            var (count, sizeBytes, byType) = _achievementService.GetScheduledStatsInfo();
            
            var message = $"🛡️ *Информация о защите данных*\n\n" +
                         $"🔒 *Состояние защиты:*\n" +
                         $"• Целостность данных: {(isValid ? "✅ В порядке" : "❌ Нарушена")}\n" +
                         $"• Резервная копия: {(hasBackup ? "✅ Доступна" : "❌ Отсутствует")}\n" +
                         $"• Записей в кэше: {count}\n" +
                         $"• Размер данных: {FormatBytes(sizeBytes)}\n\n" +
                         $"📊 *API лимиты:*\n" +
                         $"• Доступно: {remaining}/{limit}\n" +
                         $"• Минимум для обновления: {_achievementService.GetMinApiCallsThreshold()}\n" +
                         $"• Сброс лимитов: {resetTime:HH:mm dd.MM.yyyy}\n\n" +
                         $"🔄 *Механизмы защиты:*\n" +
                         $"• Резервное копирование перед обновлением\n" +
                         $"• Поочередное обновление с проверкой лимитов\n" +
                         $"• Восстановление при сбоях\n" +
                         $"• Проверка целостности данных\n" +
                         $"• Очистка только после успешного обновления\n\n" +
                         $"💡 *Автоматические функции:*\n" +
                         $"• Обновление прерывается при низких лимитах\n" +
                         $"• Данные восстанавливаются при ошибках\n" +
                         $"• Проверка каждые 30 минут\n" +
                         $"• Безопасное сохранение с валидацией";
            
            await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, disableNotification: true);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения информации о защите: {ex.Message}", disableNotification: true);
        }
    }
    
    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number = number / 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}
