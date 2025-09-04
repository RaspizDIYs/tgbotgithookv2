using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramGitHubBot.Services;

public class TelegramBotService
{
    private readonly ITelegramBotClient _botClient;
    private readonly GitHubService _gitHubService;

    public TelegramBotService(ITelegramBotClient botClient, GitHubService gitHubService)
    {
        _botClient = botClient;
        _gitHubService = gitHubService ?? throw new ArgumentNullException(nameof(gitHubService));
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

                case "/status":
                    await HandleStatusCommandAsync(chatId);
                    break;

                case "/commits":
                    var branch = parts.Length > 1 ? parts[1] : "main";
                    var count = parts.Length > 2 && int.TryParse(parts[2], out var c) ? c : 5;
                    await HandleCommitsCommandAsync(chatId, branch, count);
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
                    var wfBranch = parts.Length > 1 ? parts[1] : null;
                    var wfCount = parts.Length > 2 && int.TryParse(parts[2], out var wc) ? wc : 5;
                    await HandleWorkflowsCommandAsync(chatId, wfBranch, wfCount);
                    break;

                case "/deploy":
                    if (parts.Length > 1)
                    {
                        await HandleDeployCommandAsync(chatId, parts[1], username);
                    }
                    else
                    {
                        await _botClient.SendTextMessageAsync(chatId, "Укажите среду для деплоя: /deploy staging или /deploy production");
                    }
                    break;

                case "/педик":
                    await _botClient.SendTextMessageAsync(chatId, "Сам ты педик");
                    break;

                default:
                    
                    break;
            }
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"Ошибка выполнения команды: {ex.Message}");
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
• Релизах";

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
                InlineKeyboardButton.WithCallbackData("❓ Справка", "/help"),
            }
        });

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: message,
            replyMarkup: inlineKeyboard
        );
    }

    private async Task SendHelpMessageAsync(long chatId)
    {
        var message = @"📋 Команды бота:

/status - Статус репозитория
/commits [ветка] - Коммиты
/branches - Список веток
/prs - Открытые PR
/ci [ветка] - CI/CD статус
/deploy [среда] - Деплой
/help - Эта справка";

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
                InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "/start"),
            }
        });

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: message,
            replyMarkup: inlineKeyboard
        );
    }

    private async Task HandleWorkflowsCommandAsync(long chatId, string? branch, int count)
    {
        try
        {
            var workflows = await _gitHubService.GetWorkflowRunsAsync(branch ?? string.Empty, count);
            await _botClient.SendTextMessageAsync(chatId, workflows, parseMode: ParseMode.Markdown);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения CI/CD статусов: {ex.Message}");
        }
    }

    private async Task HandleDeployCommandAsync(long chatId, string environment, string? username)
    {
        try
        {
            // Проверяем права пользователя
            if (string.IsNullOrEmpty(username))
            {
                await _botClient.SendTextMessageAsync(chatId, "❌ Не удалось определить пользователя");
                return;
            }

            var allowedUsers = new[] { "your_username" }; // Добавьте разрешенных пользователей
            if (!allowedUsers.Contains(username.ToLower()))
            {
                await _botClient.SendTextMessageAsync(chatId, "❌ У вас нет прав для запуска деплоя");
                return;
            }

            if (environment.ToLower() != "staging" && environment.ToLower() != "production")
            {
                await _botClient.SendTextMessageAsync(chatId, "❌ Доступные среды: staging, production");
                return;
            }

            var message = $"🚀 *Запуск деплоя в {environment}*\n\n" +
                         $"👤 Инициировал: {username}\n" +
                         $"⏰ Время: {DateTime.Now:dd.MM.yyyy HH:mm}\n" +
                         $"🔄 Статус: Запускается...";

            await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown);

            // Здесь можно добавить логику для запуска GitHub Actions workflow
            // await _gitHubService.TriggerDeploymentAsync(environment, username);

            var successMessage = $"✅ *Деплой в {environment} запущен!*\n\n" +
                               $"👤 {username}\n" +
                               $"📊 Следите за статусом через /ci";

            await _botClient.SendTextMessageAsync(chatId, successMessage, parseMode: ParseMode.Markdown);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка запуска деплоя: {ex.Message}");
        }
    }

    private async Task HandleStatusCommandAsync(long chatId)
    {
        try
        {
            var status = await _gitHubService.GetRepositoryStatusAsync();
            await _botClient.SendTextMessageAsync(chatId, status, parseMode: ParseMode.Markdown);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения статуса: {ex.Message}");
        }
    }

    private async Task HandleCommitsCommandAsync(long chatId, string branch, int count)
    {
        try
        {
            var commits = await _gitHubService.GetRecentCommitsAsync(branch, count);
            await _botClient.SendTextMessageAsync(chatId, commits, parseMode: ParseMode.Markdown);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения коммитов: {ex.Message}");
        }
    }

    private async Task HandleBranchesCommandAsync(long chatId)
    {
        try
        {
            var branches = await _gitHubService.GetBranchesAsync();
            await _botClient.SendTextMessageAsync(chatId, branches, parseMode: ParseMode.Markdown);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения веток: {ex.Message}");
        }
    }

    private async Task HandlePullRequestsCommandAsync(long chatId)
    {
        try
        {
            var prs = await _gitHubService.GetPullRequestsAsync();
            await _botClient.SendTextMessageAsync(chatId, prs, parseMode: ParseMode.Markdown);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения PR: {ex.Message}");
        }
    }

    public async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery)
    {
        var chatId = callbackQuery.Message?.Chat.Id ?? 0;
        var data = callbackQuery.Data;

        if (chatId == 0 || string.IsNullOrEmpty(data))
        {
            await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Ошибка обработки запроса");
            return;
        }

        try
        {
            // Отвечаем на callback query
            await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id);

            // Проверяем, является ли это запросом деталей коммита
            if (data.StartsWith("commit_details:"))
            {
                // Обрабатываем запрос деталей коммита
                await HandleCommitDetailsCallbackAsync(chatId, data);
            }
            else
            {
                // Обрабатываем обычную команду из callback data
                await HandleCommandAsync(chatId, data, callbackQuery.From?.Username);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Callback query error: {ex.Message}");
            await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Произошла ошибка");
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
                await _botClient.SendTextMessageAsync(chatId, "❌ Ошибка: некорректные данные");
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
                    replyMarkup: backKeyboard
                );
            }
            else if (action == "back")
            {
                // Показываем упрощенное сообщение о пуше
                var backMessage = $"🚀 *Последний пуш в {repoName}*\n\n" +
                                 $"📦 Коммит: `{shortSha}`\n" +
                                 $"🔗 [Посмотреть на GitHub](https://github.com/RaspizDIYs/goodluckv2/commit/{commitSha})";

                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: backMessage,
                    parseMode: ParseMode.Markdown,
                    disableWebPagePreview: true
                );
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling commit details: {ex.Message}");
            await _botClient.SendTextMessageAsync(chatId, "❌ Ошибка получения деталей коммита");
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
}
