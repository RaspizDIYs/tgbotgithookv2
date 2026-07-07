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



public class GifTextSettings

{

    public string TextColor { get; set; } = "white";

    public TextPosition Position { get; set; } = TextPosition.Bottom;

    

    public static GifTextSettings Default => new();

}



public class TelegramBotService

{

    private readonly ITelegramBotClient _botClient;

    private readonly GitHubService _gitHubService;

    private readonly AchievementService _achievementService;

    private readonly GeminiManager _geminiManager;

    private readonly MessageStatsService _messageStatsService;

    private readonly Dictionary<long, NotificationSettings> _chatSettings = new();

    private readonly HashSet<string> _processedCallbacks = new();

    private readonly HashSet<int> _processedUpdateIds = new();

    private readonly Queue<(int id, DateTime ts)> _processedUpdateTimestamps = new();

    private readonly Dictionary<string, System.Timers.Timer> _messageTimers = new();

    private static System.Timers.Timer? _dailySummaryTimer;

    private static readonly object _dailySummaryTimerLock = new object();

    private readonly Dictionary<long, int> _swearWordCounters = new();

    private readonly HashSet<string> _swearWords = new();

    private readonly Dictionary<long, bool> _geminiMode = new();

    // Буфер последних сообщений чата для /tldr (KAN-61)
    private readonly Dictionary<long, Queue<string>> _recentMessages = new();
    private const int MAX_RECENT_MESSAGES = 100;

    private readonly Dictionary<long, GameState> _gameStates = new();

    private readonly TenorService _tenorService;

    private readonly GifTextEditorService _gifTextEditorService;

    private readonly Dictionary<long, string> _pendingGifTexts = new(); // Для ожидания текста для GIF

    private readonly Dictionary<long, string> _pendingGifFiles = new(); // Для хранения GIF файлов

    private readonly Dictionary<long, GifTextSettings> _gifTextSettings = new(); // Настройки текста для GIF

    private readonly ChatActivityTracker _chatActivityTracker;

    private static readonly Random _random = new Random();

    private readonly Dictionary<long, string> _lastMenu = new(); // Для отслеживания предыдущего меню

    private readonly Dictionary<long, Stack<string>> _navigationStack = new(); // Стек навигации для каждого чата



    public TelegramBotService(ITelegramBotClient botClient, GitHubService gitHubService, AchievementService achievementService, GeminiManager geminiManager, MessageStatsService messageStatsService, TenorService tenorService, GifTextEditorService gifTextEditorService)

    {

        _botClient = botClient;

        _gitHubService = gitHubService ?? throw new ArgumentNullException(nameof(gitHubService));

        _achievementService = achievementService ?? throw new ArgumentNullException(nameof(achievementService));

        _geminiManager = geminiManager ?? throw new ArgumentNullException(nameof(geminiManager));

        _messageStatsService = messageStatsService ?? throw new ArgumentNullException(nameof(messageStatsService));

        _tenorService = tenorService ?? throw new ArgumentNullException(nameof(tenorService));

        _gifTextEditorService = gifTextEditorService ?? throw new ArgumentNullException(nameof(gifTextEditorService));

        

        // Создаем ChatActivityTracker с доступом к _geminiMode

        _chatActivityTracker = new ChatActivityTracker(

            tenorService, 

            geminiManager, 

            botClient,

            chatId => _geminiMode.ContainsKey(chatId) && _geminiMode[chatId],

            (chatId, isActive) => _geminiMode[chatId] = isActive);



        InitializeSwearWords();

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

        var fromId = message.From?.Id ?? 0;

        var fromUsername = message.From?.Username ?? (message.From?.FirstName ?? "");

        var text = message.Text.Trim();



        // Инкремент счетчиков до обработки команд

        var (totalChat, totalUser, chatMilestoneHit, userMilestoneHit, chatMilestone, userMilestone) = _messageStatsService.RegisterMessage(chatId, fromId);



        // Поздравления по чату: 20000, 30000, 40000, ...

        if (chatMilestoneHit && chatMilestone >= 20000)

        {

            var chatMsg = GetChatMilestoneMessage(chatMilestone);

            try { await _botClient.SendTextMessageAsync(chatId, chatMsg, parseMode: ParseMode.Markdown, disableWebPagePreview: true, disableNotification: true); } catch {}

        }



        // Пользовательское поздравление на 10000 для конкретного пользователя

        if (userMilestoneHit && userMilestone == 10000 && fromId != 0)

        {

            var mention = !string.IsNullOrWhiteSpace(fromUsername) ? $"@{fromUsername}" : $"id:{fromId}";

            var userMsg = $"{mention} мастер общения! Это его {userMilestone} сообщение в этом чате!";

            try { await _botClient.SendTextMessageAsync(chatId, userMsg, parseMode: ParseMode.Markdown, disableWebPagePreview: true, disableNotification: true); } catch {}

        }



        // Детектор мата (простые русские шаблоны) - убрано постоянное сообщение

        // if (ContainsProfanity(text))

        // {

        //     var mention = !string.IsNullOrWhiteSpace(fromUsername) ? $"@{fromUsername}" : $"id:{fromId}";

        //     var warn = $"Вы открываете Скверну! {mention} СКВЕРНОСЛОВ!";

        //     try { await _botClient.SendTextMessageAsync(chatId, warn, parseMode: ParseMode.Markdown, disableWebPagePreview: true, disableNotification: true); } catch {}

        // }



        // Проверяем матные слова во всех сообщениях

        if (message.From != null)

        {

            await CheckSwearWordsAsync(chatId, message.From.Id, text);

        }



        // Буфер последних сообщений для /tldr (KAN-61) — только обычные сообщения, не команды

        if (!string.IsNullOrWhiteSpace(text) && !text.StartsWith("/"))

        {

            var author = !string.IsNullOrWhiteSpace(fromUsername) ? fromUsername : "user";

            AddToRecent(chatId, $"{author}: {text}");

        }



        // Команды Gemini

        if (text.StartsWith("/"))

        {

            var cleanCommand = text.Split('@')[0];

            

            if (cleanCommand == "/glaistart")

            {

                _geminiMode[chatId] = true;

                

                var aiMessage = "🤖 **Режим Gemini активирован!**\n\nТеперь я буду отвечать через AI модель.";

                var inlineKeyboard = new InlineKeyboardMarkup(new[]

                {

                    new[] { InlineKeyboardButton.WithCallbackData("⏹️ Остановить AI", "/glaistop") }

                });

                

                await _botClient.SendTextMessageAsync(chatId, aiMessage, parseMode: ParseMode.Markdown, replyMarkup: inlineKeyboard, disableNotification: true);

                return;

            }

            else if (cleanCommand == "/glaistop")

            {

                // Если активна игра, сначала останавливаем её

                if (_gameStates.ContainsKey(chatId) && _gameStates[chatId].IsActive)

                {

                    await StopGameAsync(chatId);

                }

                

                _geminiMode[chatId] = false;

                

                var stopMessage = "🛑 **Режим Gemini деактивирован.**\n\nВозвращаюсь к обычным командам.";

                var inlineKeyboard = new InlineKeyboardMarkup(new[]

                {

                    new[] { InlineKeyboardButton.WithCallbackData("▶️ Включить AI", "/glaistart") }

                });

                

                await _botClient.SendTextMessageAsync(chatId, stopMessage, parseMode: ParseMode.Markdown, replyMarkup: inlineKeyboard, disableNotification: true);

                return;

            }

            else if (cleanCommand == "/glaistats")

            {

                var stats = _geminiManager.GetAllAgentsStatus();

                await SendMessageWithBackButtonAsync(chatId, stats);

                return;

            }

            else if (cleanCommand == "/glaicurrent")

            {

                var stats = _geminiManager.GetCurrentAgentStatus();

                await SendMessageWithBackButtonAsync(chatId, stats);

                return;

            }

            else if (cleanCommand == "/glaiswitch")

            {

                _geminiManager.SwitchToNextAgent();

                var stats = _geminiManager.GetCurrentAgentStatus();

                await SendMessageWithBackButtonAsync(chatId, $"🔄 **Переключение агента**\n\n{stats}");

                return;

            }

            else if (cleanCommand == "/glaiclear")

            {

                _geminiManager.ClearContext(chatId);

                await SendMessageWithBackButtonAsync(chatId, "🧹 **Контекст разговора очищен!**");

                return;

            }

            else if (cleanCommand == "/webapp")

            {

                var webAppUrl = Environment.GetEnvironmentVariable("WEBAPP_URL") ?? 

                    "https://raspizdiys.github.io/tgbotgithookv2/webapp/";

                

                var webAppMessage = "🌐 **Веб-приложение бота**\n\n" +

                    "Откройте веб-интерфейс для управления ботом и просмотра статистики.";

                

                var inlineKeyboard = new InlineKeyboardMarkup(new[]

                {

                    new[] { InlineKeyboardButton.WithWebApp("🚀 Открыть веб-приложение", new Telegram.Bot.Types.WebAppInfo { Url = webAppUrl }) },

                    new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

                });

                

                await _botClient.SendTextMessageAsync(chatId, webAppMessage, parseMode: ParseMode.Markdown, 

                    replyMarkup: inlineKeyboard, disableNotification: true);

                return;

            }

            else if (cleanCommand == "/cursor")

            {

                var cursorUrl = "https://raspizdiys.github.io/tgbotgithookv2/";

                

                var cursorMessage = "💻 **Cursor IDE Диплинки**\n\n" +

                    "Откройте страницу с диплинками для быстрого доступа к файлам и промптам в Cursor IDE.";

                

                var inlineKeyboard = new InlineKeyboardMarkup(new[]

                {

                    new[] { InlineKeyboardButton.WithUrl("🔗 Открыть диплинки Cursor", cursorUrl) },

                    new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

                });

                

                await _botClient.SendTextMessageAsync(chatId, cursorMessage, parseMode: ParseMode.Markdown, 

                    replyMarkup: inlineKeyboard, disableNotification: true);

                return;

            }

            else if (cleanCommand == "/game")

            {

                await ShowGameMenuAsync(chatId);

                return;

            }

            else if (cleanCommand == "/gamememe")

            {

                await ShowDifficultyMenuAsync(chatId, "meme");

                return;

            }

            else if (cleanCommand == "/gamelol")

            {

                await ShowDifficultyMenuAsync(chatId, "lol");

                return;

            }

            else if (cleanCommand == "/gameprogramming")

            {

                await ShowDifficultyMenuAsync(chatId, "programming");

                return;

            }

            else if (cleanCommand == "/gamestop")

            {

                await StopGameAsync(chatId);

                return;

            }

            else if (cleanCommand == "/gametest")

            {

                await TestGamePromptAsync(chatId);

                return;

            }

            else if (cleanCommand == "/gifsearch")

            {

                await HandleGifSearchAsync(chatId, text);

                return;

            }

            else if (cleanCommand == "/gifrandom")

            {

                await HandleRandomGifAsync(chatId);

                return;

            }

            else if (cleanCommand == "/giftext")

            {

                await HandleGifTextCommandAsync(chatId);

                return;

            }

            else if (cleanCommand == "/gifsettings")

            {

                await ShowGifSettingsAsync(chatId);

                return;

            }

            else if (cleanCommand == "/gifcolor")

            {

                await ShowGifColorMenuAsync(chatId);

                return;

            }

            else if (cleanCommand == "/gifposition")

            {

                await ShowGifPositionMenuAsync(chatId);

                return;

            }

            else if (cleanCommand == "/chatactivity")

            {

                await ShowChatActivityAsync(chatId);

                return;

            }

            else if (cleanCommand == "/resetactivity")

            {

                await ResetChatActivityAsync(chatId);

                return;

            }

            else if (cleanCommand == "/testtenor")

            {

                await TestTenorApiAsync(chatId);

                return;

            }

        }



        // Если активна игра, обрабатываем ответ игрока

        if (_gameStates.ContainsKey(chatId) && _gameStates[chatId].IsActive)

        {

            await ProcessGameAnswerAsync(chatId, text);

            return;

        }



        // Если режим Gemini активен, отправляем все сообщения в AI

        if (_geminiMode.ContainsKey(chatId) && _geminiMode[chatId])

        {

            try

            {

                var aiResponse = await _geminiManager.GenerateResponseWithContextAsync(text, chatId);

                

                // Проверяем, нужно ли отправить GIF вместе с ответом

                await HandleAiResponseWithGifAsync(chatId, aiResponse, text);

            }

            catch (Exception ex)

            {

                var errorMessage = $"❌ **Ошибка AI:** {ex.Message}\n\n" + _geminiManager.GetCurrentAgentStatus();

                await _botClient.SendTextMessageAsync(chatId, errorMessage, parseMode: ParseMode.Markdown, disableNotification: true);

            }

            return;

        }



        // Обычная обработка команд



        // Обработка GIF сообщений для добавления текста

        if (message.Animation != null && _pendingGifTexts.ContainsKey(chatId) && _pendingGifTexts[chatId] == "waiting_for_gif")

        {

            Console.WriteLine($"🎬 GIF received for chat {chatId}, fileId: {message.Animation.FileId}");

            

            // Сохраняем GIF файл и ждем текст

            _pendingGifFiles[chatId] = message.Animation.FileId;

            _pendingGifTexts[chatId] = "waiting_for_text";

            

            var step2Message = @"✅ **GIF получен!**



**Шаг 2:** Напишите текст, который хотите добавить на GIF



Просто отправьте сообщение с текстом, который должен появиться на GIF.";

            

            await _botClient.SendTextMessageAsync(chatId, step2Message, parseMode: ParseMode.Markdown, disableNotification: true);

            return;

        }



        // Обработка текста для GIF

        if (!string.IsNullOrEmpty(text) && _pendingGifTexts.ContainsKey(chatId) && _pendingGifTexts[chatId] == "waiting_for_text")

        {

            if (_pendingGifFiles.ContainsKey(chatId))

            {

                // Сохраняем текст и переходим к выбору цвета

                _pendingGifTexts[chatId] = text; // Сохраняем текст

                await ShowGifColorSelectionAsync(chatId);

                return;

            }

        }



        // Обычные команды

        if (text.StartsWith("/"))

        {

            var cleanCommand = text.Split('@')[0];

            await HandleCommandAsync(chatId, cleanCommand, message.From?.Username);

            return;

        }



        // Отслеживаем активность диалога для автоматического AI

        if (message.From != null && !string.IsNullOrWhiteSpace(text))

        {

            await _chatActivityTracker.TrackMessageAsync(

                chatId, 

                message.From.Id, 

                message.From.Username ?? message.From.FirstName, 

                text, 

                DateTime.UtcNow);

        }



        // В обычном режиме просто игнорируем сообщения (только команды работают)

        // Для общения с AI используйте /glaistart

    }





    private static bool ContainsProfanity(string text)

    {

        if (string.IsNullOrWhiteSpace(text)) return false;

        var lower = text.ToLowerInvariant();

        // Базовые формы, грубый фильтр (без регэкспов для скорости)

        string[] words = {

            "бля", "еба", "ебат", "ёба", "ёб", "сука", "хуй", "пизд", "сучар", "мраз", "гандон", "ебан", "уёб", "уеб"

        };

        foreach (var w in words)

        {

            if (lower.Contains(w)) return true;

        }

        return false;

    }



    private static string GetChatMilestoneMessage(long milestone)

    {

        // Немного разнообразия

        var pool = new[]

        {

            $"Вы очень активны! Было написано целых {milestone} сообщений!",

            $"Чат кипит! {milestone} сообщений достигнуты!",

            $"Ого! Мы пробили отметку в {milestone}!",

            $"{milestone}! Это шедевр общения!",

            $"{milestone} сообщений — жара! Продолжаем!"

        };

        var idx = (int)(milestone / 10000) % pool.Length;

        return pool[idx];

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



    public async Task HandleCommandAsync(long chatId, string command, string? username = null)

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



                case "/ask":

                    {

                        var question = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : "";

                        if (string.IsNullOrWhiteSpace(question))

                        {

                            await _botClient.SendTextMessageAsync(chatId, "❓ Использование: /ask <вопрос>", disableNotification: true);

                            break;

                        }

                        try

                        {

                            var askResponse = await _geminiManager.GenerateResponseAsync(question);

                            // /ask — чистый ответ без мемов: вырезаем теги [GIF:...]

                            var cleanAnswer = System.Text.RegularExpressions.Regex.Replace(askResponse, @"\[GIF:[^\]]*\]", "").Trim();

                            await _botClient.SendTextMessageAsync(chatId, cleanAnswer, disableNotification: true);

                        }

                        catch (Exception ex)

                        {

                            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка: {ex.Message}", disableNotification: true);

                        }

                    }

                    break;



                case "/tldr":

                case "/summary":

                    await HandleTldrCommandAsync(chatId);

                    break;



                case "/settings":

                case "/manage":

                    await SendSettingsMessageAsync(chatId);

                    break;



                case "/status":

                    await HandleStatusCommandAsync(chatId);

                    break;



                case "/stats":

                    await HandleStatsCommandAsync(chatId);

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



    public async Task<object> HandleCommandForWebAppAsync(long chatId, string command, string? username = null)

    {

        try

        {

            var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var cmd = parts[0].ToLower();



            switch (cmd)

            {

                case "/gifrandom":

                    return await HandleRandomGifForWebAppAsync(chatId);

                

                case "/gifsearch":

                    if (parts.Length > 1)

                    {

                        var query = string.Join(" ", parts.Skip(1));

                        return await HandleGifSearchForWebAppAsync(chatId, query);

                    }

                    return new { message = "Использование: /gifsearch <запрос>" };

                

                case "/giftext":

                    return await HandleGifTextForWebAppAsync(chatId);

                

                case "/gifcolor":

                    if (parts.Length > 1)

                    {

                        var color = parts[1];

                        return await HandleGifColorForWebAppAsync(chatId, color);

                    }

                    return new { message = "Использование: /gifcolor <цвет>" };

                

                case "/gifposition":

                    if (parts.Length > 1)

                    {

                        var position = parts[1];

                        return await HandleGifPositionForWebAppAsync(chatId, position);

                    }

                    return new { message = "Использование: /gifposition <позиция>" };

                

                case "/status":

                    return await HandleStatusCommandForWebAppAsync(chatId);

                

                case "/stats":

                    return await HandleStatsCommandForWebAppAsync(chatId);

                

                case "/commits":

                    if (parts.Length > 1)

                    {

                        var branch = parts[1];

                        var count = parts.Length > 2 && int.TryParse(parts[2], out var c) ? c : 5;

                        return await HandleCommitsCommandForWebAppAsync(chatId, branch, count);

                    }

                    return await ShowBranchSelectionForWebAppAsync(chatId, "commits");

                

                case "/branches":

                    return await HandleBranchesCommandForWebAppAsync(chatId);

                

                case "/prs":

                case "/pulls":

                    return await HandlePullRequestsCommandForWebAppAsync(chatId);

                

                case "/ci":

                case "/workflows":

                    return await HandleWorkflowsCommandForWebAppAsync(chatId);

                

                case "/authors":

                    return await HandleAuthorsCommandForWebAppAsync(chatId);

                

                case "/search":

                    if (parts.Length > 1)

                    {

                        var query = string.Join(" ", parts.Skip(1));

                        return await HandleSearchCommandForWebAppAsync(chatId, query);

                    }

                    return new { message = "Использование: /search <запрос>" };

                

                case "/achievements":

                    return await HandleAchievementsCommandForWebAppAsync(chatId);

                

                case "/leaderboard":

                    return await HandleLeaderboardCommandForWebAppAsync(chatId);

                

                case "/streaks":

                    return await HandleStreaksCommandForWebAppAsync(chatId);

                

                default:

                    return new { message = "Команда не найдена" };

            }

        }

        catch (Exception ex)

        {

            return new { message = $"Ошибка выполнения команды: {ex.Message}" };

        }

    }



    private void PushNavigation(long chatId, string command)

    {

        if (!_navigationStack.ContainsKey(chatId))

        {

            _navigationStack[chatId] = new Stack<string>();

        }

        _navigationStack[chatId].Push(command);

    }



    private string? PopNavigation(long chatId)

    {

        if (!_navigationStack.ContainsKey(chatId) || _navigationStack[chatId].Count == 0)

        {

            return null;

        }

        return _navigationStack[chatId].Pop();

    }



    private string? PeekNavigation(long chatId)

    {

        if (!_navigationStack.ContainsKey(chatId) || _navigationStack[chatId].Count == 0)

        {

            return "/help"; // По умолчанию возвращаемся в help

        }

        return _navigationStack[chatId].Peek();

    }



    private async Task SendMessageWithBackButtonAsync(long chatId, string message, string? backCommand = null, ParseMode parseMode = ParseMode.Markdown)

    {

        var actualBackCommand = backCommand ?? PeekNavigation(chatId) ?? "/help";

        var inlineKeyboard = new InlineKeyboardMarkup(new[]

        {

            new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", actualBackCommand) }

        });



        await _botClient.SendTextMessageAsync(chatId, message, parseMode: parseMode, replyMarkup: inlineKeyboard, disableNotification: true);

    }



    private async Task EditMessageWithBackButtonAsync(long chatId, int messageId, string message, string? backCommand = null, ParseMode parseMode = ParseMode.Markdown)

    {

        var actualBackCommand = backCommand ?? PeekNavigation(chatId) ?? "/help";

        var inlineKeyboard = new InlineKeyboardMarkup(new[]

        {

            new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", actualBackCommand) }

        });



        await _botClient.EditMessageTextAsync(chatId, messageId, message, parseMode: parseMode, replyMarkup: inlineKeyboard);

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

                InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад")

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

❓ /help - Эта справка



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

🛡️ /protection - Защита веток

💾 /backup - Резервное копирование



📊 *Stats - Статистика и достижения:*

📈 /laststats - Последняя статистика

📊 /weekstats - Статистика по неделям

🏆 /rating - Рейтинг разработчиков

📉 /trends - Тренды активности

🏅 /achievements - Список всех ачивок

🥇 /leaderboard - Таблица лидеров

🔥 /streaks - Топ стриков

🔄 /recalc - Пересчёт статистики



🤖 *Gemini AI - Искусственный интеллект:*

▶️ /glaistart - Включить режим AI

⏹️ /glaistop - Выключить режим AI

📊 /glaistats - Статус всех агентов

🔍 /glaicurrent - Текущий агент

🔄 /glaiswitch - Переключить агента

🧹 /glaiclear - Очистить контекст



🌐 *Веб-интерфейс:*

🚀 /webapp - Открыть веб-приложение

💻 /cursor - Диплинки Cursor IDE



🎮 *Игры:*

🎯 /game - Меню игр

😄 /gamememe - Игра с мемами

😂 /gamelol - Викторина League of Legends

💻 /gameprogramming - Программирование

⏹️ /gamestop - Остановить игру

🧪 /gametest - Тест игры



🎬 *GIF - Работа с анимацией:*

🔍 /gifsearch <запрос> - Поиск GIF

🎲 /gifrandom - Случайный GIF

😊 /gifemotion <эмоция> - GIF по эмоции

📝 /giftext - Добавить текст на GIF

⚙️ /gifsettings - Настройки GIF



🤖 *AI Диалоги:*

📊 /chatactivity - Статистика активности

🔄 /resetactivity - Сбросить активность

🧪 /testtenor - Тест Tenor API



🖱️ *Cursor - Интеграция:*

🔗 /deep <путь> - Диплинк для Cursor

  Примеры:

  • /deep src/App.tsx

  • /deep src/App.tsx:42

  • /deep src/App.tsx:42:10



⚙️ *Настройки:*

⚙️ /settings - Настройки уведомлений



💡 *Подсказки:*

• Используйте главное меню для удобной навигации

• Команды без параметров показывают интерактивное меню

• В режиме Gemini все сообщения отправляются в AI";



        var inlineKeyboard = new InlineKeyboardMarkup(new[]

        {

            new[]

            {

                InlineKeyboardButton.WithCallbackData("📦 Git", "menu:git"),

                InlineKeyboardButton.WithCallbackData("📊 Stats", "menu:stats"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🤖 Gemini AI", "menu:gemini"),

                InlineKeyboardButton.WithCallbackData("🎮 Игры", "menu:games"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🎬 GIF", "menu:gif"),

                InlineKeyboardButton.WithCallbackData("🖱️ Cursor", "menu:cursor"),

            },

            new[]

            {

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

            

            await SendMessageWithBackButtonAsync(chatId, status);

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

            

            await SendMessageWithBackButtonAsync(chatId, commits);

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

            await SendMessageWithBackButtonAsync(chatId, branches);

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

            await SendMessageWithBackButtonAsync(chatId, prs);

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

                

                // Добавляем команду в стек навигации

                PushNavigation(chatId, data);

                

                await HandleSubmenuAsync(chatId, messageId, data);

            }

            else if (data.StartsWith("difficulty:"))

            {

                Console.WriteLine($"🎯 Processing difficulty selection: {data}");

                await HandleDifficultySelectionAsync(chatId, messageId, data);

            }

            else if (data.StartsWith("gifcolor:"))

            {

                Console.WriteLine($"🎨 Processing GIF color selection: {data}");

                await HandleGifColorSelectionAsync(chatId, data);

            }

            else if (data.StartsWith("gifposition:"))

            {

                Console.WriteLine($"📍 Processing GIF position selection: {data}");

                await HandleGifPositionSelectionAsync(chatId, data);

            }

            else if (data.StartsWith("copy_deeplink:"))

            {

                Console.WriteLine("📋 Processing deeplink copy request");

                var deeplink = data.Substring("copy_deeplink:".Length);

                await HandleDeeplinkCopyAsync(chatId, deeplink);

            }

            else

            {

                Console.WriteLine("📝 Processing regular command");

                

                // Если это кнопка "Назад", возвращаемся к предыдущему меню

                if (data == "⬅️ Назад" || data.Contains("Назад"))

                {

                    var previousCommand = PopNavigation(chatId);

                    if (previousCommand != null)

                    {

                        await HandleCommandAsync(chatId, previousCommand, callbackQuery.From?.Username);

                    }

                    else

                    {

                        // Если нет предыдущего меню, возвращаемся в help

                        await SendHelpMessageAsync(chatId);

                    }

                }

                else

                {

                    // Добавляем команду в стек навигации

                    PushNavigation(chatId, data);

                    

                    // Обрабатываем команду из callback data

                    await HandleCommandAsync(chatId, data, callbackQuery.From?.Username);

                }

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

            new [] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

        });



        try

        {

            // Убираем удаление предыдущих сообщений для корректной работы



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

                    InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад")

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



    private async Task HandleStatsCommandAsync(long chatId)

    {

        var stats = _messageStatsService.GetChatStats(chatId);

        if (stats == null)

        {

            await SendMessageWithBackButtonAsync(chatId, "Статистика пока пуста.");

            return;

        }



        var top = _messageStatsService.GetTopUsers(chatId, 5);

        var lines = new List<string>();

        lines.Add($"Всего сообщений в чате: {stats.TotalMessages}");

        if (top.Count > 0)

        {

            lines.Add("");

            lines.Add("Топ активистов:");

            int rank = 1;

            foreach (var (userId, count) in top)

            {

                lines.Add($"{rank}. id:{userId} — {count}");

                rank++;

            }

        }



        var text = string.Join("\n", lines);

        await SendMessageWithBackButtonAsync(chatId, text);

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

        lock (_dailySummaryTimerLock)

        {

            if (_dailySummaryTimer != null)

            {

                Console.WriteLine("⏰ Daily summary timer already initialized (another instance), skipping");

                return;

            }

            _dailySummaryTimer = new System.Timers.Timer();

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



            // «Что сделали за день» — человеческим языком (тот же флаг SUMMARIZE_GITHUB_EVENTS).
            // Источник — тексты коммитов из GitHub, а не эфемерный буфер (устойчиво к рестартам Render).
            if (DigestEnabled)
            {
                try
                {
                    var commitMessages = await _gitHubService.GetDailyCommitMessagesAsync();
                    if (commitMessages.Count > 0)
                    {
                        var prompt =
                            "Ниже сообщения коммитов за день по репозиторию. Расскажи человеческим языком, " +
                            "2-4 предложения, что было сделано за день — по сути, без воды, без эмодзи, " +
                            "без списков и без тегов. Сообщения коммитов:\n" + string.Join("\n", commitMessages);
                        var prose = StripGifTags(await _geminiManager.GenerateResponseAsync(prompt));
                        if (!string.IsNullOrWhiteSpace(prose) && !prose.Contains("❌"))
                        {
                            message += $"\n🌇 *Что сделали за день:*\n{prose.Trim()}\n";
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Ошибка прозаического блока сводки: {ex.Message}");
                }
            }

            // Автоматическую сводку шлём в тему 8146 (PUSH_SUMMARY_*), запрошенную (/summary) — в чат запроса
            int? threadId = null;
            if (!targetChatId.HasValue)
            {
                var summaryChatId = Environment.GetEnvironmentVariable("PUSH_SUMMARY_CHAT_ID");
                if (!string.IsNullOrWhiteSpace(summaryChatId) && long.TryParse(summaryChatId, out var scid))
                {
                    chatId = scid;
                    threadId = int.TryParse(Environment.GetEnvironmentVariable("PUSH_SUMMARY_THREAD_ID"), out var tid) ? tid : null;
                }
            }

            await _botClient.SendTextMessageAsync(

                chatId: chatId,

                text: message,

                parseMode: ParseMode.Markdown,

                messageThreadId: threadId,

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

            buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") });



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

                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

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

                    new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

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

                    new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

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

                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

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

                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

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



            // Убираем удаление сообщений для корректной работы



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

                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

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



            buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") });



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

                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

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

                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

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

            

            // Убираем удаление сообщений для корректной работы



            var weekStats = await _gitHubService.GetWeeklyStatsAsync(weekOffset);



            var keyboard = new InlineKeyboardMarkup(new[]

            {

                new[] { InlineKeyboardButton.WithCallbackData("📊 Выбрать другую неделю", "/weekstats") },

                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

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



    private void InitializeSwearWords()

    {

        // Хуй и производные

        _swearWords.Add("хуй");

        _swearWords.Add("хуйня");

        _swearWords.Add("хуев");

        _swearWords.Add("хуевый");

        _swearWords.Add("хуярить");

        _swearWords.Add("хуячить");

        _swearWords.Add("хуяк");

        

        // Ебать и производные

        _swearWords.Add("ебать");

        _swearWords.Add("ебаться");

        _swearWords.Add("ебаное");

        _swearWords.Add("ебанутый");

        _swearWords.Add("ебашить");

        _swearWords.Add("ебанько");

        _swearWords.Add("ебучий");

        _swearWords.Add("ебанулся");

        _swearWords.Add("ебанушка");

        _swearWords.Add("ебанат");

        _swearWords.Add("ебало");

        _swearWords.Add("ебатель");

        

        // Блядь и производные

        _swearWords.Add("блядь");

        _swearWords.Add("блядский");

        _swearWords.Add("блядство");

        _swearWords.Add("блядовать");

        _swearWords.Add("блядюга");

        _swearWords.Add("блядня");

        _swearWords.Add("блядюшка");

        

        // Сука и производные

        _swearWords.Add("сука");

        _swearWords.Add("сукин");

        _swearWords.Add("сучий");

        _swearWords.Add("сучара");

        _swearWords.Add("сучатина");

        _swearWords.Add("сучиться");

        _swearWords.Add("сук");

        

        // Уебищ и производные

        _swearWords.Add("уебищ");

        _swearWords.Add("уебок");

        _swearWords.Add("уебан");

        _swearWords.Add("уебать");

        _swearWords.Add("уебаться");

        _swearWords.Add("уебище");

        _swearWords.Add("уебашить");

        

        // Ахуй и производные

        _swearWords.Add("ахуй");

        _swearWords.Add("ахуеть");

        _swearWords.Add("ахуенный");

        _swearWords.Add("ахуевший");

        _swearWords.Add("ахуевать");

        _swearWords.Add("ахуевший");

        _swearWords.Add("ахуительный");

        

        // Пизда и расширенные производные

        _swearWords.Add("пизда");

        _swearWords.Add("пиздец");

        _swearWords.Add("пиздатый");

        _swearWords.Add("пиздецкий");

        _swearWords.Add("пиздить");

        _swearWords.Add("пиздобол");

        _swearWords.Add("пиздюк");

        _swearWords.Add("пиздануть");

        _swearWords.Add("пизданутый");

        _swearWords.Add("припизднутый");

        _swearWords.Add("припиздячить");

        _swearWords.Add("пиздабол");

        _swearWords.Add("пиздюлина");

        _swearWords.Add("пиздюль");

        

        // Похуй и производные

        _swearWords.Add("похуй");

        _swearWords.Add("похуям");

        _swearWords.Add("поахуевали");

        _swearWords.Add("похуист");

        _swearWords.Add("похуистика");

        _swearWords.Add("похуйщина");

        _swearWords.Add("похуйствовать");

        

        // Хуйлан и производные

        _swearWords.Add("хуйлан");

        _swearWords.Add("хуила");

        _swearWords.Add("хуйлуша");

        _swearWords.Add("хуйло");

        _swearWords.Add("хуйня");

        _swearWords.Add("хуйман");

        _swearWords.Add("хуйма");

        

        // Блядота и производные

        _swearWords.Add("блядота");

        _swearWords.Add("блядина");

        _swearWords.Add("блядюк");

        _swearWords.Add("блядюшка");

        _swearWords.Add("блядство");

        _swearWords.Add("блядовать");

    }



    private async Task CheckSwearWordsAsync(long chatId, long userId, string text)

    {

        var lowerText = text.ToLower();

        var swearCount = 0;



        foreach (var swearWord in _swearWords)

        {

            if (lowerText.Contains(swearWord))

            {

                swearCount++;

            }

        }



        if (swearCount > 0)

        {

            if (!_swearWordCounters.ContainsKey(userId))

            {

                _swearWordCounters[userId] = 0;

            }



            _swearWordCounters[userId] += swearCount;



            if (_swearWordCounters[userId] >= 100)

            {

                var shameMessage = "Позор! Позор! Позор! Уже 100 оскорблений в чате от тебя!";

                var gifUrl = "https://media1.tenor.com/m/5t7dwIkeSioAAAAC/shame-bell.gif";

                

                await _botClient.SendAnimationAsync(chatId, InputFile.FromUri(gifUrl), caption: shameMessage);

                _swearWordCounters[userId] = 0;

            }

        }

    }



    private async Task ShowGameMenuAsync(long chatId)

    {

        var menu = "🎮 **Доступные игры:**\n\n" +

                  "1️⃣ `/gamememe` - Что? Где? Мем? (русскоязычные мемы)\n" +

                  "2️⃣ `/gamelol` - Что? Где? Лол? (League of Legends)\n" +

                  "3️⃣ `/gameprogramming` - If? Else? True? (программирование)\n\n" +

                  "🛑 `/gamestop` - остановить текущую игру\n\n" +

                  "Выберите игру для начала!";

        

        await _botClient.SendTextMessageAsync(chatId, menu, parseMode: ParseMode.Markdown, disableNotification: true);

    }



    private async Task StartGameAsync(long chatId, string gameType)

    {

        // Останавливаем текущую игру если есть

        if (_gameStates.ContainsKey(chatId) && _gameStates[chatId].IsActive)

        {

            await StopGameAsync(chatId);

        }



        // Очищаем контекст AI для новой игры

        _geminiManager.ClearContext(chatId);



        // Создаем новое состояние игры

        _gameStates[chatId] = new GameState

        {

            IsActive = true,

            GameType = gameType,

            Difficulty = "medium", // По умолчанию средняя сложность

            CurrentQuestion = 0,

            CorrectAnswers = 0,

            WrongAnswers = 0,

            HasUsedLifeline = false,

            StartTime = DateTime.UtcNow

        };



        var gameName = GamePrompts.GetGameName(gameType);

        var prompt = GamePrompts.GetPromptWithDifficulty(gameType, _gameStates[chatId].Difficulty);



        try

        {

            await _botClient.SendTextMessageAsync(chatId, $"🎮 **{gameName}**\n\nИгра начинается! AI придумывает вопросы...", disableNotification: true);

            

            var aiResponse = await _geminiManager.GenerateResponseWithContextAsync(prompt, chatId);

            await _botClient.SendTextMessageAsync(chatId, aiResponse, disableNotification: true);

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ **Ошибка запуска игры:** {ex.Message}", disableNotification: true);

            _gameStates[chatId].IsActive = false;

        }

    }



    private async Task StopGameAsync(long chatId)

    {

        if (_gameStates.ContainsKey(chatId) && _gameStates[chatId].IsActive)

        {

            var gameState = _gameStates[chatId];

            var gameName = GamePrompts.GetGameName(gameState.GameType);

            

            await _botClient.SendTextMessageAsync(chatId, $"🛑 **{gameName} остановлена!**\n\nСтатистика:\n✅ Правильных: {gameState.CorrectAnswers}\n❌ Неправильных: {gameState.WrongAnswers}", disableNotification: true);

            

            _gameStates[chatId].IsActive = false;

            

            // Автоматически отключаем AI режим после окончания игры

            if (_geminiMode.ContainsKey(chatId) && _geminiMode[chatId])

            {

                _geminiMode[chatId] = false;

                await _botClient.SendTextMessageAsync(chatId, "🤖 **AI режим автоматически отключен после окончания игры!**", disableNotification: true);

            }

        }

        else

        {

            await _botClient.SendTextMessageAsync(chatId, "❌ **Нет активной игры для остановки!**", disableNotification: true);

        }

    }



    private async Task ProcessGameAnswerAsync(long chatId, string answer)

    {

        if (!_gameStates.ContainsKey(chatId) || !_gameStates[chatId].IsActive)

            return;



        var gameState = _gameStates[chatId];

        var gameName = GamePrompts.GetGameName(gameState.GameType);



        try

        {

            // Нормализуем ответ игрока

            var normalizedAnswer = NormalizeAnswer(answer);

            

            // Отправляем ответ игрока в AI для обработки

            var prompt = $@"CURRENT GAME: {gameName} ({gameState.GameType})

Player answered: '{normalizedAnswer}'



IMPORTANT: Respond ONLY in Russian language!



Process the answer:

1. If correct - start response with ""✅ ПРАВИЛЬНО!"" and give next question

2. If wrong - start response with ""❌ НЕПРАВИЛЬНО!"" and say correct answer, then give next question  

3. If this was the last (10th) question - finish game with ""🎉 ИГРА ЗАВЕРШЕНА!""

4. Follow format: Вопрос: [text] A) [option] B) [option] C) [option] D) [option]



Remember: 

- Start with ✅ ПРАВИЛЬНО! or ❌ НЕПРАВИЛЬНО!

- ALL responses must be in Russian!

- Current question: {gameState.CurrentQuestion + 1}/10

- Wrong answers: {gameState.WrongAnswers}/1 (game ends after 1 wrong answer)

- Stay strictly within the topic of {gameState.GameType} game!";

            

            var aiResponse = await _geminiManager.GenerateResponseWithContextAsync(prompt, chatId);

            

            // Отправляем ответ AI

            await _botClient.SendTextMessageAsync(chatId, aiResponse, disableNotification: true);

            

            // Если это игра с мемами, добавляем GIF после каждого ответа и при старте

            if (gameState.GameType == "meme")

            {

                await AddGifToMemeGameAsync(chatId, aiResponse, gameState);

            }



            // Анализируем ответ AI и обновляем счетчики

            if (answer != "start") // Не считаем стартовый запрос как вопрос

            {

                gameState.CurrentQuestion++;

                

                // Определяем правильность ответа по началу ответа AI

                var isCorrect = aiResponse.StartsWith("✅ ПРАВИЛЬНО!") || aiResponse.Contains("✅ ПРАВИЛЬНО!");

                

                if (isCorrect)

                {

                    gameState.CorrectAnswers++;

                }

                else

                {

                    gameState.WrongAnswers++;

                }

            }



            // Проверяем, завершилась ли игра

            var shouldEndGame = false;

            

            // Проверяем количество вопросов (максимум 10)

            if (gameState.CurrentQuestion >= 10)

            {

                shouldEndGame = true;

            }

            

            // Проверяем количество неправильных ответов (максимум 1)

            if (gameState.WrongAnswers >= 1)

            {

                shouldEndGame = true;

            }

            

            // Проверяем ответ AI на завершение игры

            if (aiResponse.Contains("🎉 ИГРА ЗАВЕРШЕНА!") || aiResponse.Contains("ИГРА ЗАВЕРШЕНА"))

            {

                shouldEndGame = true;

            }

            

            if (shouldEndGame)

            {

                _gameStates[chatId].IsActive = false;

                

                // Отправляем финальную статистику

                var finalStats = $"🎮 **Игра завершена!**\n\n" +

                               $"📊 **Статистика:**\n" +

                               $"✅ Правильных ответов: {gameState.CorrectAnswers}\n" +

                               $"❌ Неправильных ответов: {gameState.WrongAnswers}\n" +

                               $"📝 Всего вопросов: {gameState.CurrentQuestion}\n" +

                               $"⏱️ Время игры: {DateTime.UtcNow - gameState.StartTime:mm\\:ss}";

                

                await _botClient.SendTextMessageAsync(chatId, finalStats, disableNotification: true);

                

                // Автоматически отключаем AI режим после окончания игры

                if (_geminiMode.ContainsKey(chatId) && _geminiMode[chatId])

                {

                    _geminiMode[chatId] = false;

                    await _botClient.SendTextMessageAsync(chatId, "🤖 **AI режим автоматически отключен после окончания игры!**", disableNotification: true);

                }

                

                return; // Выходим из метода, чтобы не обрабатывать ответ дальше

            }

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ **Ошибка обработки ответа:** {ex.Message}", disableNotification: true);

        }

    }



    private string NormalizeAnswer(string answer)

    {

        // Нормализуем ответ игрока для лучшего распознавания

        var normalized = answer.Trim().ToUpper();

        

        // Если ответ содержит только букву

        if (normalized.Length == 1 && "ABCD".Contains(normalized))

        {

            return normalized;

        }

        

        // Если ответ начинается с буквы

        if (normalized.Length > 1 && "ABCD".Contains(normalized[0]))

        {

            return normalized[0].ToString();

        }

        

        // Возвращаем оригинальный ответ

        return answer.Trim();

    }



    private async Task TestGamePromptAsync(long chatId)

    {

        try

        {

            await _botClient.SendTextMessageAsync(chatId, "🧪 **Тестирование промпта игры...**", disableNotification: true);

            

            var testPrompt = @"You are the host of a ""What? Where? When?"" quiz game about Russian internet memes.



IMPORTANT: All questions, answers, and responses must be in RUSSIAN language only!



RULES:

- 10 questions with 4 answer options each

- Difficulty progression: 3 easy → 3 medium → 3 hard → 1 very hard

- Player has 1 lifeline (can make 1 mistake)

- Only real popular Russian memes, no fictional ones



RESPONSE FORMAT (MUST be in Russian):

Вопрос: [question text]

A) [option 1]

B) [option 2] 

C) [option 3]

D) [option 4]



Start with the first easy question. Remember: everything must be in Russian!";



            var aiResponse = await _geminiManager.GenerateResponseWithContextAsync(testPrompt, chatId);

            await _botClient.SendTextMessageAsync(chatId, aiResponse, disableNotification: true);

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ **Ошибка тестирования:** {ex.Message}", disableNotification: true);

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

                case "main":

                    await SendHelpMessageAsync(chatId);

                    break;

                case "git":

                    await ShowGitMenuAsync(chatId, messageId);

                    break;

                case "stats":

                    await ShowStatsMenuAsync(chatId, messageId);

                    break;

                case "gemini":

                    await ShowGeminiMenuAsync(chatId, messageId);

                    break;

                case "games":

                    await ShowGamesMenuAsync(chatId, messageId);

                    break;

                case "gif":

                    await ShowGifMenuAsync(chatId, messageId);

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

                InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад")

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

                InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад")

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



    private async Task ShowCursorMenuAsync(long chatId, int messageId)

    {

        var message = "🖱️ *Cursor - Интеграция с редактором*\n\n" +

                     "Команды для работы с Cursor:\n\n" +

                     "📝 `/deep <путь или команда>` - Создать диплинк\n" +

                     "Примеры файлов:\n" +

                     "• `/deep src/App.tsx`\n" +

                     "• `/deep src/App.tsx:42`\n" +

                     "• `/deep src/App.tsx:42:10`\n\n" +

                     "Примеры команд:\n" +

                     "• `/deep Запусти в терминале билд`\n" +

                     "• `/deep Создай компонент Button`\n" +

                     "• `/deep Исправь ошибки в коде`\n\n" +

                     "ℹ️ Подробнее: /info";



        var keyboard = new InlineKeyboardMarkup(new[]

        {

            new[]

            {

                InlineKeyboardButton.WithCallbackData("ℹ️ Подробная инфо", "/info"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад")

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



    private async Task ShowGeminiMenuAsync(long chatId, int messageId)

    {

        var message = "🤖 *Gemini AI - Искусственный интеллект*\n\n" +

                     "Выберите действие:";



        var keyboard = new InlineKeyboardMarkup(new[]

        {

            new[]

            {

                InlineKeyboardButton.WithCallbackData("▶️ Включить AI", "/glaistart"),

                InlineKeyboardButton.WithCallbackData("⏹️ Выключить AI", "/glaistop"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("📊 Статус агентов", "/glaistats"),

                InlineKeyboardButton.WithCallbackData("🔍 Текущий агент", "/glaicurrent"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🔄 Переключить", "/glaiswitch"),

                InlineKeyboardButton.WithCallbackData("🧹 Очистить контекст", "/glaiclear"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад")

            }

        });



        await _botClient.EditMessageTextAsync(chatId, messageId, message, parseMode: ParseMode.Markdown, replyMarkup: keyboard);

    }



    private async Task ShowGamesMenuAsync(long chatId, int messageId)

    {

        var message = "🎮 *Игры*\n\n" +

                     "Выберите игру:";



        var keyboard = new InlineKeyboardMarkup(new[]

        {

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🎯 Меню игр", "/game"),

                InlineKeyboardButton.WithCallbackData("😄 Мемы", "/gamememe"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⚔️ League of Legends", "/gamelol"),

                InlineKeyboardButton.WithCallbackData("💻 Программирование", "/gameprogramming"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⏹️ Остановить", "/gamestop"),

                InlineKeyboardButton.WithCallbackData("🧪 Тест", "/gametest"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад")

            }

        });



        await _botClient.SendTextMessageAsync(

            chatId: chatId,

            text: message,

            parseMode: ParseMode.Markdown,

            replyMarkup: keyboard,

            disableNotification: true

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

Команда `/deep` создаёт диплинк для открытия файла или выполнения команды в Cursor.



*Примеры файлов:*

• `/deep src/components/Button.tsx`

  Откроет файл Button.tsx



• `/deep src/components/Button.tsx:150`

  Откроет файл на строке 150



• `/deep src/components/Button.tsx:150:10`

  Откроет файл на строке 150, колонке 10



*Примеры команд:*

• `/deep Запусти в терминале билд`

  Выполнит команду в Cursor



• `/deep Создай компонент Button`

  Попросит Cursor создать компонент



• `/deep Исправь ошибки в коде`

  Попросит Cursor исправить ошибки



*Форматы диплинков:*

• Файлы: `cursor://file/{workspace}/{path}?line={line}&column={column}`

• Команды: `cursor://anysphere.cursor-deeplink/prompt?text={command}`



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



    private async Task HandleDeeplinkCommandAsync(long chatId, string input)

    {

        try

        {

            string deeplink;

            string message;

            

            // Проверяем, является ли ввод текстовой командой (не содержит слеши и не похож на путь к файлу)

            if (!input.Contains('/') && !input.Contains('\\') && !input.Contains('.'))

            {

                // Это текстовая команда для Cursor

                var encodedText = Uri.EscapeDataString(input);

                deeplink = $"cursor://anysphere.cursor-deeplink/prompt?text={encodedText}";

                

                message = "🔗 *Диплинк для Cursor (Текстовая команда)*\n\n";

                message += $"💬 Команда: `{input}`\n";

                message += $"📦 Тип: Текстовая команда для Cursor\n";

                message += $"\n🔗 Ссылка:\n`{deeplink}`\n\n";

                message += "Нажми кнопку ниже, чтобы запустить в Cursor";

            }

            else

            {

                // Это путь к файлу

                var workspacePath = Environment.GetEnvironmentVariable("GOODLUCK_WORKSPACE_PATH") 

                                  ?? Environment.GetEnvironmentVariable("CURSOR_WORKSPACE_PATH")

                                  ?? "D:/Git/goodluckv2";

                

                string relativePath = input;

                int? line = null;

                int? column = null;



                if (input.Contains(':'))

                {

                    var parts = input.Split(':');

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

                

                deeplink = $"cursor://file/{workspacePath}/{relativePath}";

                

                if (line.HasValue)

                {

                    deeplink += $"?line={line.Value}";

                    if (column.HasValue)

                    {

                        deeplink += $"&column={column.Value}";

                    }

                }



                message = "🔗 *Диплинк для Cursor (Файл)*\n\n";

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

            }



            // Если Telegram не поддерживает cursor:// как URL, используем HTTPS-мост

            var bridgeBase = Environment.GetEnvironmentVariable("BRIDGE_BASE_URL");

            string buttonUrl;

            string buttonText;

            if (!string.IsNullOrWhiteSpace(bridgeBase))

            {

                // Готовим ссылку на мост

                if (!input.Contains('/') && !input.Contains('\\') && !input.Contains('.'))

                {

                    // prompt bridge

                    var enc = Uri.EscapeDataString(input);

                    buttonUrl = $"{bridgeBase.TrimEnd('/')}/?type=prompt&text={enc}";

                    buttonText = "🚀 Запустить в Cursor";

                }

                else

                {

                    // file bridge

                    var workspacePath = Environment.GetEnvironmentVariable("GOODLUCK_WORKSPACE_PATH") 

                                      ?? Environment.GetEnvironmentVariable("CURSOR_WORKSPACE_PATH")

                                      ?? "D:/Git/goodluckv2";

                    string relativePath = input;

                    string lineQs = string.Empty;

                    string columnQs = string.Empty;

                    if (input.Contains(':'))

                    {

                        var parts = input.Split(':');

                        relativePath = parts[0];

                        if (parts.Length > 1 && int.TryParse(parts[1], out var lineNum))

                        {

                            lineQs = $"&line={lineNum}";

                        }

                        if (parts.Length > 2 && int.TryParse(parts[2], out var colNum))

                        {

                            columnQs = $"&column={colNum}";

                        }

                    }

                    var encWs = Uri.EscapeDataString(workspacePath.Replace('\\','/'));

                    var encPath = Uri.EscapeDataString(relativePath.Replace('\\','/').TrimStart('/'));

                    buttonUrl = $"{bridgeBase.TrimEnd('/')}/?type=file&workspace={encWs}&path={encPath}{lineQs}{columnQs}";

                    buttonText = "📁 Открыть в Cursor";

                }

            }

            else

            {

                // fallback: показываем копирование через callback

                buttonUrl = string.Empty;

                buttonText = !input.Contains('/') && !input.Contains('\\') && !input.Contains('.') 

                    ? "🚀 Запустить в Cursor" 

                    : "📁 Открыть в Cursor";

            }



            InlineKeyboardMarkup keyboard;

            if (!string.IsNullOrEmpty(buttonUrl))

            {

                keyboard = new InlineKeyboardMarkup(new[]

                {

                    new[] { InlineKeyboardButton.WithUrl(buttonText, buttonUrl) },

                    new[] { InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "/start") }

                });

            }

            else

            {

                // Без BRIDGE_BASE_URL используем callback, который пришлет ссылку для копирования

                keyboard = new InlineKeyboardMarkup(new[]

                {

                    new[] { InlineKeyboardButton.WithCallbackData(buttonText, $"copy_deeplink:{input}") },

                    new[] { InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "/start") }

                });

            }



            await _botClient.SendTextMessageAsync(

                chatId: chatId,

                text: message,

                parseMode: ParseMode.Markdown,

                disableWebPagePreview: true,

                disableNotification: true,

                replyMarkup: keyboard

            );



            Console.WriteLine($"✅ Deeplink generated: {deeplink}");

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка генерации диплинка: {ex.Message}", disableNotification: true);

        }

    }



    private async Task HandleDeeplinkCopyAsync(long chatId, string input)

    {

        try

        {

            string deeplink;

            

            // Проверяем, является ли ввод текстовой командой (не содержит слеши и не похож на путь к файлу)

            if (!input.Contains('/') && !input.Contains('\\') && !input.Contains('.'))

            {

                // Это текстовая команда для Cursor

                var encodedText = Uri.EscapeDataString(input);

                deeplink = $"cursor://anysphere.cursor-deeplink/prompt?text={encodedText}";

            }

            else

            {

                // Это путь к файлу

                var workspacePath = Environment.GetEnvironmentVariable("GOODLUCK_WORKSPACE_PATH") 

                                  ?? Environment.GetEnvironmentVariable("CURSOR_WORKSPACE_PATH")

                                  ?? "D:/Git/goodluckv2";

                

                string relativePath = input;

                int? line = null;

                int? column = null;



                if (input.Contains(':'))

                {

                    var parts = input.Split(':');

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

                

                deeplink = $"cursor://file/{workspacePath}/{relativePath}";

                

                if (line.HasValue)

                {

                    deeplink += $"?line={line.Value}";

                    if (column.HasValue)

                    {

                        deeplink += $"&column={column.Value}";

                    }

                }

            }



            var message = "📋 *Ссылка скопирована в буфер обмена*\n\n";

            message += $"🔗 Диплинк для Cursor:\n`{deeplink}`\n\n";

            message += "💡 *Как использовать:*\n";

            message += "1. Скопируйте ссылку выше\n";

            message += "2. Откройте Cursor\n";

            message += "3. Вставьте ссылку в адресную строку браузера\n";

            message += "4. Cursor автоматически откроет файл\n\n";

            message += "⚠️ *Важно:* Убедитесь, что Cursor установлен и настроен для работы с протоколом `cursor://`";



            var keyboard = new InlineKeyboardMarkup(new[]

            {

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



            Console.WriteLine($"📋 Deeplink copy requested: {deeplink}");

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка обработки запроса: {ex.Message}", disableNotification: true);

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

    

    // Включена ли выдача дайджеста (тот же флаг, что раньше отвечал за резюме пушей)
    private static bool DigestEnabled =>
        (Environment.GetEnvironmentVariable("SUMMARIZE_GITHUB_EVENTS") ?? "").ToLowerInvariant() is "true" or "1" or "yes";

    private static string StripGifTags(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text ?? "", @"\[GIF:[^\]]*\]", "").Trim();

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



    private async Task ShowDifficultyMenuAsync(long chatId, string gameType)

    {

        var gameName = GamePrompts.GetGameName(gameType);

        var message = $"🎮 **{gameName}**\n\nВыберите сложность:";



        var keyboard = new InlineKeyboardMarkup(new[]

        {

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🟢 Легкая", $"difficulty:{gameType}:easy"),

                InlineKeyboardButton.WithCallbackData("🟡 Средняя", $"difficulty:{gameType}:medium"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🔴 Сложная", $"difficulty:{gameType}:hard"),

                InlineKeyboardButton.WithCallbackData("⚫ Эксперт", $"difficulty:{gameType}:expert"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⬅️ Назад к играм", "/game")

            }

        });



        await _botClient.SendTextMessageAsync(

            chatId: chatId,

            text: message,

            parseMode: ParseMode.Markdown,

            replyMarkup: keyboard,

            disableNotification: true

        );

    }



    private async Task HandleDifficultySelectionAsync(long chatId, int messageId, string data)

    {

        try

        {

            var parts = data.Split(':');

            if (parts.Length >= 3)

            {

                var gameType = parts[1];

                var difficulty = parts[2];

                

                // Убираем удаление сообщений для корректной работы

                

                // Запускаем игру с выбранной сложностью

                await StartGameWithDifficultyAsync(chatId, gameType, difficulty);

            }

        }

        catch (Exception ex)

        {

            Console.WriteLine($"❌ Error handling difficulty selection: {ex.Message}");

        }

    }



    #region GIF Methods



    private async Task ShowGifMenuAsync(long chatId, int messageId)

    {

        var message = @"🎬 *GIF меню*



Выберите действие:";



        var keyboard = new InlineKeyboardMarkup(new[]

        {

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🔍 Поиск GIF", "/gifsearch"),

                InlineKeyboardButton.WithCallbackData("🎲 Случайный", "/gifrandom"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("📝 Добавить текст", "/giftext"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⚙️ Настройки", "/gifsettings"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад"),

            }

        });



        await _botClient.EditMessageTextAsync(chatId, messageId, message, parseMode: ParseMode.Markdown, replyMarkup: keyboard);

    }



    private async Task HandleGifSearchAsync(long chatId, string text)

    {

        var query = text.Replace("/gifsearch", "").Trim();

        if (string.IsNullOrWhiteSpace(query))

        {

            await SendMessageWithBackButtonAsync(chatId, "🔍 **Поиск GIF**\n\nВведите запрос для поиска GIF:\n`/gifsearch котики`");

            return;

        }



        Console.WriteLine($"🔍 GIF Search request: {query}");

        await _botClient.SendTextMessageAsync(chatId, $"🔍 Ищу GIF по запросу: **{query}**...", parseMode: ParseMode.Markdown, disableNotification: true);



        try

        {

            var gifs = await _tenorService.SearchGifsAsync(query, 5);

            Console.WriteLine($"🎬 SearchGifsAsync returned {gifs.Count} GIFs");

            

            if (gifs.Count == 0)

            {

                await _botClient.SendTextMessageAsync(chatId, "❌ GIF не найдены по запросу: " + query, disableNotification: true);

                return;

            }



            foreach (var gif in gifs.Take(3))

            {

                try

                {

                    Console.WriteLine($"📤 Sending GIF: {gif.Url}");

                    await _botClient.SendAnimationAsync(chatId, InputFile.FromUri(gif.Url), caption: $"🎬 {gif.Title}", disableNotification: true);

                    await Task.Delay(500); // Небольшая задержка между GIF

                }

                catch (Exception ex)

                {

                    Console.WriteLine($"❌ Error sending GIF: {ex.Message}");

                }

            }

        }

        catch (Exception ex)

        {

            Console.WriteLine($"❌ Error in HandleGifSearchAsync: {ex.Message}");

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка поиска GIF: {ex.Message}", disableNotification: true);

        }

    }



    private async Task HandleRandomGifAsync(long chatId)

    {

        Console.WriteLine($"🎲 Random GIF request for chat {chatId}");

        await _botClient.SendTextMessageAsync(chatId, "🎲 Ищу случайный GIF...", disableNotification: true);



        try

        {

            var gif = await _tenorService.GetRandomGifAsync("memes");

            Console.WriteLine($"🎬 GetRandomGifAsync returned: {(gif != null ? "GIF found" : "null")}");

            

            if (gif == null)

            {

                await _botClient.SendTextMessageAsync(chatId, "❌ Не удалось найти случайный GIF", disableNotification: true);

                return;

            }



            Console.WriteLine($"📤 Sending random GIF: {gif.Url}");

            await _botClient.SendAnimationAsync(chatId, InputFile.FromUri(gif.Url), caption: $"🎲 Случайный GIF: {gif.Title}", disableNotification: true);

        }

        catch (Exception ex)

        {

            Console.WriteLine($"❌ Error in HandleRandomGifAsync: {ex.Message}");

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения случайного GIF: {ex.Message}", disableNotification: true);

        }

    }





    private async Task HandleGifTextCommandAsync(long chatId)

    {

        var message = @"📝 **Добавление текста на GIF**



**Шаг 1:** Отправьте GIF файл в этот чат



После отправки GIF я покажу следующие шаги для настройки текста.";



        await SendMessageWithBackButtonAsync(chatId, message);
        

        // Устанавливаем флаг ожидания GIF

        _pendingGifTexts[chatId] = "waiting_for_gif";

        Console.WriteLine($"📝 GIF text command started for chat {chatId}, waiting for GIF");
    }



    private async Task ShowGifColorSelectionAsync(long chatId)

    {

        // Получаем или создаем настройки для пользователя

        if (!_gifTextSettings.ContainsKey(chatId))

        {

            _gifTextSettings[chatId] = GifTextSettings.Default;

        }

        

        var settings = _gifTextSettings[chatId];

        var currentColor = GetColorName(settings.TextColor);

        

        var message = $@"🎨 **Шаг 3: Выберите цвет текста**



**Текущий цвет:** {currentColor}



Выберите цвет для текста на GIF:";



        var keyboard = new InlineKeyboardMarkup(new[]

        {

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⚪ Белый", "gifcolor:white"),

                InlineKeyboardButton.WithCallbackData("⚫ Черный", "gifcolor:black"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🔴 Красный", "gifcolor:red"),

                InlineKeyboardButton.WithCallbackData("🟢 Зеленый", "gifcolor:green"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🔵 Синий", "gifcolor:blue"),

                InlineKeyboardButton.WithCallbackData("🟡 Желтый", "gifcolor:yellow"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🟣 Фиолетовый", "gifcolor:purple"),

            }

        });



        await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, replyMarkup: keyboard, disableNotification: true);

    }



    private async Task HandleGifWithTextAsync(long chatId, string fileId, string textSettings)

    {

        try

        {

            await _botClient.SendTextMessageAsync(chatId, "📝 Обрабатываю GIF...", disableNotification: true);



            // Получаем файл GIF

            var file = await _botClient.GetFileAsync(fileId);

            var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ?? "";

            var fileUrl = $"https://api.telegram.org/file/bot{token}/{file.FilePath}";



            // Получаем настройки пользователя

            var settings = _gifTextSettings.ContainsKey(chatId) ? _gifTextSettings[chatId] : GifTextSettings.Default;

            

            // Добавляем текст на GIF

            var editedGifBytes = await _gifTextEditorService.AddTextToGifAsync(fileUrl, textSettings, settings.Position, settings.TextColor);

            

            if (editedGifBytes == null)

            {

                await _botClient.SendTextMessageAsync(chatId, "❌ Ошибка обработки GIF", disableNotification: true);

                return;

            }



            // Отправляем обработанный GIF

            using var stream = new MemoryStream(editedGifBytes);

            await _botClient.SendAnimationAsync(chatId, InputFile.FromStream(stream, "edited.gif"), caption: $"📝 GIF с текстом: {textSettings}", disableNotification: true);

            

            // Отправляем сообщение об успешном завершении

            await _botClient.SendTextMessageAsync(chatId, "✅ **Готово!** GIF с текстом создан успешно!", parseMode: ParseMode.Markdown, disableNotification: true);

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка обработки GIF: {ex.Message}", disableNotification: true);

        }

    }



    private string GetColorName(string color)

    {

        return color.ToLower() switch

        {

            "white" => "белый",

            "black" => "черный",

            "red" => "красный",

            "green" => "зеленый",

            "blue" => "синий",

            "yellow" => "желтый",

            "purple" => "фиолетовый",

            _ => "пользовательский"

        };

    }



    private async Task ShowGifColorMenuAsync(long chatId)

    {

        // Получаем или создаем настройки для пользователя

        if (!_gifTextSettings.ContainsKey(chatId))

        {

            _gifTextSettings[chatId] = GifTextSettings.Default;

        }

        

        var settings = _gifTextSettings[chatId];

        var currentColor = GetColorName(settings.TextColor);

        

        var message = $@"🎨 **Настройка цвета текста GIF**



**Текущий цвет:** {currentColor}



Выберите новый цвет:";



        var keyboard = new InlineKeyboardMarkup(new[]

        {

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⚪ Белый", "gifcolor:white"),

                InlineKeyboardButton.WithCallbackData("⚫ Черный", "gifcolor:black"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🔴 Красный", "gifcolor:red"),

                InlineKeyboardButton.WithCallbackData("🟢 Зеленый", "gifcolor:green"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🔵 Синий", "gifcolor:blue"),

                InlineKeyboardButton.WithCallbackData("🟡 Желтый", "gifcolor:yellow"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🟠 Оранжевый", "gifcolor:orange"),

                InlineKeyboardButton.WithCallbackData("🟣 Фиолетовый", "gifcolor:purple"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🩷 Розовый", "gifcolor:pink"),

                InlineKeyboardButton.WithCallbackData("🔵 Голубой", "gifcolor:cyan"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⬅️ Назад к GIF", "menu:gif"),

            }

        });



        await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, replyMarkup: keyboard, disableNotification: true);

    }



    private async Task ShowGifPositionMenuAsync(long chatId)

    {

        // Получаем или создаем настройки для пользователя

        if (!_gifTextSettings.ContainsKey(chatId))

        {

            _gifTextSettings[chatId] = GifTextSettings.Default;

        }

        

        var settings = _gifTextSettings[chatId];

        var currentPosition = settings.Position == TextPosition.Top ? "сверху" : "снизу";

        

        var message = $@"📍 **Шаг 4: Выберите расположение текста**



**Текущая позиция:** {currentPosition}



Выберите где расположить текст на GIF:";



        var keyboard = new InlineKeyboardMarkup(new[]

        {

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⬆️ Сверху", "gifposition:top"),

                InlineKeyboardButton.WithCallbackData("⬇️ Снизу", "gifposition:bottom"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⬅️ Назад к GIF", "menu:gif"),

            }

        });



        await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, replyMarkup: keyboard, disableNotification: true);

    }



    private async Task ShowGifSettingsAsync(long chatId)

    {

        // Получаем или создаем настройки для пользователя

        if (!_gifTextSettings.ContainsKey(chatId))

        {

            _gifTextSettings[chatId] = GifTextSettings.Default;

        }

        

        var settings = _gifTextSettings[chatId];

        var colorName = GetColorName(settings.TextColor);

        var positionName = settings.Position == TextPosition.Top ? "сверху" : "снизу";

        

        var message = $@"⚙️ **Настройки GIF**



**Текущие настройки:**

• Цвет текста: {colorName}

• Позиция текста: {positionName}

• Качество: высокое



📝 **Команды:**

• `/giftext` - Добавить текст на GIF

• `/gifsearch <запрос>` - Поиск GIF

• `/gifrandom` - Случайный GIF";



        var keyboard = new InlineKeyboardMarkup(new[]

        {

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🎨 Цвет текста", "/gifcolor"),

                InlineKeyboardButton.WithCallbackData("📍 Позиция текста", "/gifposition"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("📝 Добавить текст", "/giftext"),

                InlineKeyboardButton.WithCallbackData("🔍 Поиск", "/gifsearch"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⬅️ Назад", "menu:gif"),

            }

        });



        await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, replyMarkup: keyboard, disableNotification: true);

    }



    private async Task ShowGifPositionSelectionAsync(long chatId)

    {

        // Получаем или создаем настройки для пользователя

        if (!_gifTextSettings.ContainsKey(chatId))

        {

            _gifTextSettings[chatId] = GifTextSettings.Default;

        }

        

        var settings = _gifTextSettings[chatId];

        var currentPosition = settings.Position == TextPosition.Top ? "сверху" : "снизу";

        

        var message = $@"📍 **Шаг 4: Выберите расположение текста**



**Текущая позиция:** {currentPosition}



Выберите где расположить текст на GIF:";



        var keyboard = new InlineKeyboardMarkup(new[]

        {

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⬆️ Сверху", "gifposition:top"),

                InlineKeyboardButton.WithCallbackData("⬇️ Снизу", "gifposition:bottom"),

            }

        });



        await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, replyMarkup: keyboard, disableNotification: true);

    }



    private async Task HandleGifColorSelectionAsync(long chatId, string data)

    {

        try

        {

            var colorName = data.Split(':')[1];

            

            // Получаем или создаем настройки для пользователя

            if (!_gifTextSettings.ContainsKey(chatId))

            {

                _gifTextSettings[chatId] = GifTextSettings.Default;

            }

            

            var settings = _gifTextSettings[chatId];

            

            // Устанавливаем новый цвет

            settings.TextColor = colorName switch

            {

                "white" => "white",

                "black" => "black",

                "red" => "red",

                "green" => "green",

                "blue" => "blue",

                "yellow" => "yellow",

                "purple" => "purple",

                _ => "white"

            };

            

            var colorDisplayName = GetColorName(settings.TextColor);

            await _botClient.SendTextMessageAsync(chatId, $"✅ Цвет текста изменен на: **{colorDisplayName}**", parseMode: ParseMode.Markdown, disableNotification: true);

            

            // Переходим к выбору позиции

            await ShowGifPositionSelectionAsync(chatId);

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка изменения цвета: {ex.Message}", disableNotification: true);

        }

    }



    private async Task HandleGifPositionSelectionAsync(long chatId, string data)

    {

        try

        {

            var positionName = data.Split(':')[1];

            

            // Получаем или создаем настройки для пользователя

            if (!_gifTextSettings.ContainsKey(chatId))

            {

                _gifTextSettings[chatId] = GifTextSettings.Default;

            }

            

            var settings = _gifTextSettings[chatId];

            

            // Устанавливаем новую позицию

            settings.Position = positionName switch

            {

                "top" => TextPosition.Top,

                "bottom" => TextPosition.Bottom,

                _ => TextPosition.Bottom

            };

            

            var positionDisplayName = settings.Position == TextPosition.Top ? "сверху" : "снизу";

            await _botClient.SendTextMessageAsync(chatId, $"✅ Позиция текста изменена на: **{positionDisplayName}**", parseMode: ParseMode.Markdown, disableNotification: true);

            

            // Обрабатываем GIF с выбранными настройками

            if (_pendingGifFiles.ContainsKey(chatId) && _pendingGifTexts.ContainsKey(chatId))

            {

                var textToAdd = _pendingGifTexts[chatId];

                await HandleGifWithTextAsync(chatId, _pendingGifFiles[chatId], textToAdd);

                

                // Очищаем временные данные

                _pendingGifTexts.Remove(chatId);

                _pendingGifFiles.Remove(chatId);

            }

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка изменения позиции: {ex.Message}", disableNotification: true);

        }

    }



    // KAN-61: буфер последних сообщений чата
    private void AddToRecent(long chatId, string line)
    {
        if (!_recentMessages.ContainsKey(chatId))
            _recentMessages[chatId] = new Queue<string>();
        var q = _recentMessages[chatId];
        q.Enqueue(line);
        while (q.Count > MAX_RECENT_MESSAGES)
            q.Dequeue();
    }

    // KAN-61: /tldr — пересказ последних сообщений чата через LLM
    private async Task HandleTldrCommandAsync(long chatId)
    {
        if (!_recentMessages.TryGetValue(chatId, out var q) || q.Count < 3)
        {
            await _botClient.SendTextMessageAsync(chatId, "Пока нечего пересказывать — маловато сообщений.", disableNotification: true);
            return;
        }
        try
        {
            var thread = string.Join("\n", q);
            var prompt = "Кратко перескажи простым языком, о чём шёл разговор в чате. Буллетами, без воды, не выдумывай. Сообщения:\n" + thread;
            var response = await _geminiManager.GenerateResponseAsync(prompt);
            var clean = System.Text.RegularExpressions.Regex.Replace(response, @"\[GIF:[^\]]*\]", "").Trim();
            await _botClient.SendTextMessageAsync(chatId, $"📝 Пересказ последних {q.Count} сообщений:\n\n{clean}", disableNotification: true);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка: {ex.Message}", disableNotification: true);
        }
    }

    private async Task HandleAiResponseWithGifAsync(long chatId, string aiResponse, string userMessage)

    {

        try

        {

            // Если ответ содержит информацию о лимитах, отправляем с Markdown

            if (aiResponse.Contains("**Статус**") || aiResponse.Contains("**Лимиты исчерпаны**") || aiResponse.Contains("**Превышен лимит**"))

            {

                await _botClient.SendTextMessageAsync(chatId, aiResponse, parseMode: ParseMode.Markdown, disableNotification: true);

                return;

            }



            // Проверяем, содержит ли ответ инструкцию для отправки GIF

            var gifInstruction = ExtractGifInstruction(aiResponse);

            var cleanResponse = RemoveGifInstruction(aiResponse);



            // Отправляем текстовый ответ без кнопки "Назад" в режиме AI

            await _botClient.SendTextMessageAsync(chatId, cleanResponse, disableNotification: true);



            // Если AI указал, что нужно отправить GIF, отправляем его

            if (!string.IsNullOrEmpty(gifInstruction))

            {

                await AddEmotionalGifToResponseAsync(chatId, gifInstruction, userMessage);

            }

        }

        catch (Exception ex)

        {

            Console.WriteLine($"❌ Error handling AI response with GIF: {ex.Message}");

            // Отправляем хотя бы текстовый ответ без кнопки "Назад" в режиме AI

            await _botClient.SendTextMessageAsync(chatId, aiResponse, disableNotification: true);

        }

    }



    private string ExtractGifInstruction(string aiResponse)

    {

        // Ищем специальные теги для GIF в ответе AI

        var gifPatterns = new[]

        {

            "[GIF:",

            "[GIF:",

            "GIF:",

            "gif:",

            "🎬:",

            "📱:"

        };



        foreach (var pattern in gifPatterns)

        {

            var startIndex = aiResponse.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);

            if (startIndex >= 0)

            {

                var endIndex = aiResponse.IndexOf("]", startIndex);

                if (endIndex > startIndex)

                {

                    var gifInstruction = aiResponse.Substring(startIndex + pattern.Length, endIndex - startIndex - pattern.Length).Trim();

                    return gifInstruction;

                }

            }

        }



        return string.Empty;

    }



    private string RemoveGifInstruction(string aiResponse)

    {

        // Удаляем инструкции GIF из ответа

        var gifPatterns = new[]

        {

            "[GIF:",

            "[GIF:",

            "GIF:",

            "gif:",

            "🎬:",

            "📱:"

        };



        var cleanResponse = aiResponse;

        foreach (var pattern in gifPatterns)

        {

            var startIndex = cleanResponse.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);

            if (startIndex >= 0)

            {

                var endIndex = cleanResponse.IndexOf("]", startIndex);

                if (endIndex > startIndex)

                {

                    cleanResponse = cleanResponse.Remove(startIndex, endIndex - startIndex + 1);

                }

            }

        }



        return cleanResponse.Trim();

    }



    private bool ShouldAddGifToResponse(string aiResponse, string userMessage)

    {

        // Добавляем GIF если:

        // 1. Ответ эмоциональный

        // 2. Пользователь просил мем или GIF

        // 3. Ответ содержит эмоциональные слова

        

        var emotionalWords = new[] { "смешно", "весело", "грустно", "зло", "радость", "удивительно", "страшно", "любовь", "мем", "gif", "анимация" };

        var userWords = userMessage.ToLower();

        var responseWords = aiResponse.ToLower();



        return emotionalWords.Any(word => userWords.Contains(word) || responseWords.Contains(word)) ||

               userWords.Contains("мем") || userWords.Contains("gif") || userWords.Contains("анимация") ||

               responseWords.Contains("😄") || responseWords.Contains("😂") || responseWords.Contains("😢") ||

               responseWords.Contains("😡") || responseWords.Contains("😍") || responseWords.Contains("🤔");

    }



    private async Task AddEmotionalGifToResponseAsync(long chatId, string aiResponse, string userMessage)

    {

        try

        {

            // Определяем эмоцию по ответу

            var emotion = DetermineEmotionFromResponse(aiResponse);

            

            // Получаем подходящий GIF

            var gifs = await _tenorService.GetGifsByEmotionAsync(emotion);

            if (gifs.Count > 0)

            {

                var randomGif = gifs[_random.Next(gifs.Count)];

                

                // В режиме AI не добавляем кнопку "Назад" к GIF

                await _botClient.SendAnimationAsync(chatId, InputFile.FromUri(randomGif.Url), 

                    caption: $"🎬 {emotion}: {randomGif.Title}", disableNotification: true);

            }

        }

        catch (Exception ex)

        {

            Console.WriteLine($"❌ Error adding emotional GIF: {ex.Message}");

        }

    }



    private string DetermineEmotionFromResponse(string response)

    {

        var lowerResponse = response.ToLower();

        

        if (lowerResponse.Contains("смешно") || lowerResponse.Contains("весело") || lowerResponse.Contains("😂") || lowerResponse.Contains("😄") || lowerResponse.Contains("laugh"))

            return "смех";

        if (lowerResponse.Contains("грустно") || lowerResponse.Contains("печально") || lowerResponse.Contains("😢") || lowerResponse.Contains("sad"))

            return "грусть";

        if (lowerResponse.Contains("зло") || lowerResponse.Contains("злой") || lowerResponse.Contains("😡") || lowerResponse.Contains("angry"))

            return "злость";

        if (lowerResponse.Contains("радость") || lowerResponse.Contains("счастье") || lowerResponse.Contains("😍") || lowerResponse.Contains("happy"))

            return "счастье";

        if (lowerResponse.Contains("удивительно") || lowerResponse.Contains("🤔") || lowerResponse.Contains("?") || lowerResponse.Contains("surprised"))

            return "удивление";

        if (lowerResponse.Contains("страшно") || lowerResponse.Contains("боюсь") || lowerResponse.Contains("scared"))

            return "страх";

        if (lowerResponse.Contains("любовь") || lowerResponse.Contains("❤️") || lowerResponse.Contains("love"))

            return "любовь";

        if (lowerResponse.Contains("привет") || lowerResponse.Contains("hello") || lowerResponse.Contains("hi"))

            return "счастье";

        if (lowerResponse.Contains("спасибо") || lowerResponse.Contains("thanks"))

            return "счастье";

        if (lowerResponse.Contains("шутка") || lowerResponse.Contains("анекдот") || lowerResponse.Contains("юмор") || lowerResponse.Contains("joke"))

            return "шутка";

        if (lowerResponse.Contains("работа") || lowerResponse.Contains("труд") || lowerResponse.Contains("работаю") || lowerResponse.Contains("work"))

            return "работа";

        if (lowerResponse.Contains("оффтоп") || lowerResponse.Contains("развлечение") || lowerResponse.Contains("отвлечение"))

            return "оффтоп";

        if (lowerResponse.Contains("фол") || lowerResponse.Contains("поддержка") || lowerResponse.Contains("помощь") || lowerResponse.Contains("support"))

            return "фол";

        if (lowerResponse.Contains("программирование") || lowerResponse.Contains("код") || lowerResponse.Contains("python") || lowerResponse.Contains("javascript") || lowerResponse.Contains("programming"))

            return "программирование";

            

        return "мемы"; // По умолчанию

    }



    private async Task ShowChatActivityAsync(long chatId)

    {

        try

        {

            var activity = _chatActivityTracker.GetActivity(chatId);

            

            if (activity == null || activity.Messages.Count == 0)

            {

                await _botClient.SendTextMessageAsync(chatId, 

                    "📊 **Статистика активности диалога**\n\nАктивность не обнаружена. Начните диалог с другими пользователями!", 

                    parseMode: ParseMode.Markdown, 

                    disableNotification: true);

                return;

            }



            var uniqueUsers = activity.Messages.Select(m => m.UserId).Distinct().Count();

            var totalMessages = activity.Messages.Count;

            var oldestMessage = activity.Messages.Min(m => m.Timestamp);

            var newestMessage = activity.Messages.Max(m => m.Timestamp);

            var timeSpan = newestMessage - oldestMessage;

            

            var status = activity.AIActivated ? "✅ Активирован" : "⏳ Ожидание";

            var memeStatus = activity.MemeSent ? "✅ Отправлен" : "⏳ Не отправлен";



            var message = $@"📊 **Статистика активности диалога**



👥 **Участники:** {uniqueUsers}

💬 **Сообщений:** {totalMessages}

⏰ **Период:** {timeSpan.TotalMinutes:F1} минут

🤖 **AI статус:** {status}

🎬 **Мем статус:** {memeStatus}



**Условия активации AI:**

• Минимум 2 участника ✅

• Минимум 5 сообщений {(totalMessages >= 5 ? "✅" : "❌")}

• Временной интервал ≤ 5 минут {(timeSpan.TotalMinutes <= 5 ? "✅" : "❌")}



**Последние сообщения:**";

            

            var recentMessages = activity.Messages

                .OrderByDescending(m => m.Timestamp)

                .Take(5)

                .Select(m => $"• {m.Username}: {m.Text.Substring(0, Math.Min(50, m.Text.Length))}...")

                .ToArray();

            

            message += "\n" + string.Join("\n", recentMessages);



            await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, disableNotification: true);

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения статистики: {ex.Message}", disableNotification: true);

        }

    }



    private async Task ResetChatActivityAsync(long chatId)

    {

        try

        {

            _chatActivityTracker.ResetActivity(chatId);

            await _botClient.SendTextMessageAsync(chatId, 

                "🔄 **Активность диалога сброшена!**\n\nСистема отслеживания активности перезапущена.", 

                parseMode: ParseMode.Markdown, 

                disableNotification: true);

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка сброса активности: {ex.Message}", disableNotification: true);

        }

    }



    private async Task AddGifToMemeGameAsync(long chatId, string aiResponse, GameState gameState)

    {

        try

        {

            // Извлекаем тему вопроса из ответа AI для поиска релевантного GIF

            string gifCategory = ExtractMemeTopicFromQuestion(aiResponse);

            

            // Получаем подходящий GIF по теме вопроса

            var gifs = await _tenorService.SearchGifsAsync(gifCategory, 3);

            if (gifs.Count > 0)

            {

                var randomGif = gifs[_random.Next(gifs.Count)];

                await _botClient.SendAnimationAsync(chatId, InputFile.FromUri(randomGif.Url), 

                    caption: $"🎬 Вопрос про мем: {randomGif.Title}", disableNotification: true);

            }

        }

        catch (Exception ex)

        {

            Console.WriteLine($"❌ Error adding GIF to meme game: {ex.Message}");

        }

    }



    private string ExtractMemeTopicFromQuestion(string aiResponse)

    {

        // Извлекаем ключевые слова из вопроса для поиска релевантного GIF

        var response = aiResponse.ToLower();

        

        // Ищем упоминания популярных мемов

        if (response.Contains("ждун") || response.Contains("waiting"))

            return "waiting meme";

        if (response.Contains("дождь") || response.Contains("rain"))

            return "rain meme";

        if (response.Contains("кот") || response.Contains("cat"))

            return "cat meme";

        if (response.Contains("собака") || response.Contains("dog"))

            return "dog meme";

        if (response.Contains("человек") || response.Contains("person"))

            return "person meme";

        if (response.Contains("реакция") || response.Contains("reaction"))

            return "reaction meme";

        if (response.Contains("грустный") || response.Contains("sad"))

            return "sad meme";

        if (response.Contains("счастливый") || response.Contains("happy"))

            return "happy meme";

        if (response.Contains("злой") || response.Contains("angry"))

            return "angry meme";

        if (response.Contains("удивленный") || response.Contains("surprised"))

            return "surprised meme";

            

        // По умолчанию ищем общие мемы

        return "meme";

    }



    private async Task TestTenorApiAsync(long chatId)

    {

        try

        {

            await _botClient.SendTextMessageAsync(chatId, "🧪 **Тестирование Tenor API**\n\nПроверяю подключение...", parseMode: ParseMode.Markdown, disableNotification: true);

            

            Console.WriteLine("🧪 Testing Tenor API...");

            

            // Тест 1: Поиск GIF

            var searchGifs = await _tenorService.SearchGifsAsync("test", 3);

            Console.WriteLine($"🔍 Search test: {searchGifs.Count} GIFs found");

            

            // Тест 2: Трендовые GIF

            var trendingGifs = await _tenorService.GetTrendingGifsAsync(3);

            Console.WriteLine($"📈 Trending test: {trendingGifs.Count} GIFs found");

            

            // Тест 3: Случайный GIF

            var randomGif = await _tenorService.GetRandomGifAsync("memes");

            Console.WriteLine($"🎲 Random test: {(randomGif != null ? "GIF found" : "null")}");

            

            var result = $@"🧪 **Результаты тестирования Tenor API**



🔍 **Поиск:** {searchGifs.Count} GIF найдено

📈 **Тренды:** {trendingGifs.Count} GIF найдено  

🎲 **Случайный:** {(randomGif != null ? "Найден" : "Не найден")}



**Статус:** {(searchGifs.Count > 0 || trendingGifs.Count > 0 || randomGif != null ? "✅ API работает" : "❌ API не работает")}";



            await _botClient.SendTextMessageAsync(chatId, result, parseMode: ParseMode.Markdown, disableNotification: true);

            

            // Отправляем первый найденный GIF если есть

            if (searchGifs.Count > 0)

            {

                await _botClient.SendAnimationAsync(chatId, InputFile.FromUri(searchGifs[0].Url), caption: "🧪 Тестовый GIF", disableNotification: true);

            }

        }

        catch (Exception ex)

        {

            Console.WriteLine($"❌ Tenor API test error: {ex.Message}");

            await _botClient.SendTextMessageAsync(chatId, $"❌ **Ошибка тестирования Tenor API**\n\n{ex.Message}", parseMode: ParseMode.Markdown, disableNotification: true);

        }

    }



    #endregion



    private async Task StartGameWithDifficultyAsync(long chatId, string gameType, string difficulty)

    {

        // Останавливаем текущую игру если есть

        if (_gameStates.ContainsKey(chatId) && _gameStates[chatId].IsActive)

        {

            await StopGameAsync(chatId);

        }



        // Очищаем контекст AI для новой игры

        _geminiManager.ClearContext(chatId);



        // Создаем новое состояние игры с уровнем сложности

        _gameStates[chatId] = new GameState

        {

            IsActive = true,

            GameType = gameType,

            Difficulty = difficulty,

            CurrentQuestion = 0,

            CorrectAnswers = 0,

            WrongAnswers = 0,

            StartTime = DateTime.UtcNow

        };



        var gameName = GamePrompts.GetGameName(gameType);

        var difficultyName = GetDifficultyName(difficulty);

        

        await _botClient.SendTextMessageAsync(chatId, $"🎮 **{gameName}** ({difficultyName})\n\nИгра началась! Отвечайте на вопросы.", disableNotification: true);



        // Запускаем первую задачу

        await ProcessGameAnswerAsync(chatId, "start");

    }



    private string GetDifficultyName(string difficulty)

    {

        return difficulty switch

        {

            "easy" => "🟢 Легкая",

            "medium" => "🟡 Средняя", 

            "hard" => "🔴 Сложная",

            "expert" => "⚫ Эксперт",

            _ => "🟡 Средняя"

        };

    }



    public BotStats GetBotStats()

    {

        return new BotStats

        {

            TotalCommits = _achievementService.GetTotalCommits(),

            TotalMessages = _messageStatsService.GetTotalMessages(),

            ActiveUsers = _messageStatsService.GetActiveUsersCount(),

            AiRequests = _geminiManager.GetTotalRequests()

        };

    }



    #region Web App Methods



    private async Task<object> HandleRandomGifForWebAppAsync(long chatId)

    {

        try

        {

            var gif = await _tenorService.GetRandomGifAsync("memes");

            if (gif == null)

            {

                return new { message = "❌ Не удалось найти случайный GIF" };

            }

            return new { gifUrl = gif.Url, message = $"🎲 Случайный GIF: {gif.Title}" };

        }

        catch (Exception ex)

        {

            return new { message = $"❌ Ошибка получения случайного GIF: {ex.Message}" };

        }

    }



    private async Task<object> HandleGifSearchForWebAppAsync(long chatId, string query)

    {

        try

        {

            var gifs = await _tenorService.SearchGifsAsync(query, 1);

            if (gifs == null || gifs.Count == 0)

            {

                return new { message = $"❌ Не найдено GIF по запросу: {query}" };

            }

            var gif = gifs[0];

            return new { gifUrl = gif.Url, message = $"🔍 Найден GIF: {gif.Title}" };

        }

        catch (Exception ex)

        {

            return new { message = $"❌ Ошибка поиска GIF: {ex.Message}" };

        }

    }



    private Task<object> HandleGifTextForWebAppAsync(long chatId)

    {

        return Task.FromResult<object>(new { message = "📝 Для добавления текста на GIF отправьте GIF файл в чат" });
    }



    private Task<object> HandleStatusCommandForWebAppAsync(long chatId)

    {

        try

        {

            var stats = GetBotStats();

            var message = $"📊 **Статистика бота**\n\n" +

                         $"📝 Всего сообщений: {stats.TotalMessages}\n" +

                         $"👥 Активных пользователей: {stats.ActiveUsers}\n" +

                         $"🤖 AI запросов: {stats.AiRequests}\n" +

                         $"📦 Всего коммитов: {stats.TotalCommits}";

            return Task.FromResult<object>(new { message });

        }

        catch (Exception ex)

        {

            return Task.FromResult<object>(new { message = $"❌ Ошибка получения статистики: {ex.Message}" });

        }

    }



    private Task<object> HandleStatsCommandForWebAppAsync(long chatId)

    {

        try

        {

            var chatStats = _messageStatsService.GetChatStats(chatId);
            var userMessageCount = _messageStatsService.GetUserMessageCount(chatId, chatId);
            var message = $"📊 **Ваша статистика**\n\n" +
                         $"📝 Сообщений: {userMessageCount}\n" +
                         $"📦 Всего сообщений в чате: {chatStats?.TotalMessages ?? 0}\n" +
                         $"🔥 Активных пользователей: {_messageStatsService.GetActiveUsersCount()}";

            return Task.FromResult<object>(new { message });

        }

        catch (Exception ex)

        {

            return Task.FromResult<object>(new { message = $"❌ Ошибка получения статистики: {ex.Message}" });

        }

    }



    private async Task<object> HandleCommitsCommandForWebAppAsync(long chatId, string branch, int count)

    {

        try

        {

            var commits = await _gitHubService.GetRecentCommitsWithStatsAsync(branch, count);

            var message = $"📦 **Последние {count} коммитов в ветке {branch}**\n\n";

            foreach (var commit in commits.Take(count))

            {

                message += $"• {commit.Message}\n";

            }

            return Task.FromResult<object>(new { message });

        }

        catch (Exception ex)

        {

            return Task.FromResult<object>(new { message = $"❌ Ошибка получения коммитов: {ex.Message}" });

        }

    }



    private async Task<object> ShowBranchSelectionForWebAppAsync(long chatId, string action)

    {

        try

        {

            var branchesList = await _gitHubService.GetBranchesListAsync();

            var message = $"🌿 **Выберите ветку для {action}**\n\n";

            foreach (var branch in branchesList.Take(10))

            {

                message += $"• {branch}\n";

            }

            return Task.FromResult<object>(new { message });

        }

        catch (Exception ex)

        {

            return Task.FromResult<object>(new { message = $"❌ Ошибка получения веток: {ex.Message}" });

        }

    }



    private async Task<object> HandleBranchesCommandForWebAppAsync(long chatId)

    {

        try

        {

            var branchesList = await _gitHubService.GetBranchesListAsync();

            var message = "🌿 **Ветки репозитория**\n\n";

            foreach (var branch in branchesList.Take(20))

            {

                message += $"• {branch}\n";

            }

            return Task.FromResult<object>(new { message });

        }

        catch (Exception ex)

        {

            return Task.FromResult<object>(new { message = $"❌ Ошибка получения веток: {ex.Message}" });

        }

    }



    private async Task<object> HandlePullRequestsCommandForWebAppAsync(long chatId)

    {

        try

        {

            var prs = await _gitHubService.GetPullRequestsAsync();

            var message = "🔀 **Pull Requests**\n\n";

            message += prs;

            return Task.FromResult<object>(new { message });

        }

        catch (Exception ex)

        {

            return Task.FromResult<object>(new { message = $"❌ Ошибка получения PR: {ex.Message}" });

        }

    }



    private Task<object> HandleWorkflowsCommandForWebAppAsync(long chatId)

    {

        try

        {

            var message = "⚙️ **Workflows**\n\n" +
                         "• GitHub Actions workflows доступны через основной бот";

            return Task.FromResult<object>(new { message });

        }

        catch (Exception ex)

        {

            return Task.FromResult<object>(new { message = $"❌ Ошибка получения workflows: {ex.Message}" });

        }

    }



    private Task<object> HandleAuthorsCommandForWebAppAsync(long chatId)

    {

        try

        {

            var message = "👥 **Авторы коммитов**\n\n" +
                         "• Информация об авторах доступна через основной бот";

            return Task.FromResult<object>(new { message });

        }

        catch (Exception ex)

        {

            return Task.FromResult<object>(new { message = $"❌ Ошибка получения авторов: {ex.Message}" });

        }

    }



    private async Task<object> HandleSearchCommandForWebAppAsync(long chatId, string query)

    {

        try

        {

            var results = await _gitHubService.SearchCommitsAsync(query);

            var message = $"🔍 **Результаты поиска: {query}**\n\n";

            if (string.IsNullOrEmpty(results))

            {

                message += "• Результаты не найдены";

            }

            else

            {

                message += results;

            }

            return Task.FromResult<object>(new { message });

        }

        catch (Exception ex)

        {

            return Task.FromResult<object>(new { message = $"❌ Ошибка поиска: {ex.Message}" });

        }

    }



    private Task<object> HandleAchievementsCommandForWebAppAsync(long chatId)

    {

        try

        {

            var achievements = _achievementService.GetAllAchievementsMerged();

            var message = "🏆 **Все достижения**\n\n";

            foreach (var achievement in achievements.Take(10))

            {

                var status = achievement.IsUnlocked ? "✅" : "❌";

                message += $"{status} {achievement.Name}: {achievement.Description}\n";

            }

            return Task.FromResult<object>(new { message });

        }

        catch (Exception ex)

        {

            return Task.FromResult<object>(new { message = $"❌ Ошибка получения достижений: {ex.Message}" });

        }

    }



    private Task<object> HandleLeaderboardCommandForWebAppAsync(long chatId)

    {

        try

        {

            var leaderboard = _achievementService.GetLeaderboardUsers();

            var message = "🏆 **Таблица лидеров**\n\n";

            for (int i = 0; i < Math.Min(10, leaderboard.Count); i++)

            {

                var user = leaderboard[i];

                message += $"{i + 1}. {user.DisplayName}: {user.TotalCommits} коммитов\n";

            }

            return Task.FromResult<object>(new { message });

        }

        catch (Exception ex)

        {

            return Task.FromResult<object>(new { message = $"❌ Ошибка получения таблицы лидеров: {ex.Message}" });

        }

    }



    private Task<object> HandleStreaksCommandForWebAppAsync(long chatId)

    {

        try

        {

            var chatStats = _messageStatsService.GetChatStats(chatId);
            var userMessageCount = _messageStatsService.GetUserMessageCount(chatId, chatId);
            var message = $"🔥 **Статистика серий**\n\n" +
                         $"📝 Ваших сообщений: {userMessageCount}\n" +
                         $"📦 Всего сообщений в чате: {chatStats?.TotalMessages ?? 0}\n" +
                         $"🔥 Активных пользователей: {_messageStatsService.GetActiveUsersCount()}";

            return Task.FromResult<object>(new { message });

        }

        catch (Exception ex)

        {

            return Task.FromResult<object>(new { message = $"❌ Ошибка получения серий: {ex.Message}" });

        }

    }



    private Task<object> HandleGifColorForWebAppAsync(long chatId, string color)

    {

        try

        {

            // Здесь можно сохранить выбранный цвет в настройках пользователя

            var message = $"🎨 Цвет текста установлен: {color}";

            return Task.FromResult<object>(new { message });

        }

        catch (Exception ex)

        {

            return Task.FromResult<object>(new { message = $"❌ Ошибка установки цвета: {ex.Message}" });

        }

    }



    private Task<object> HandleGifPositionForWebAppAsync(long chatId, string position)

    {

        try

        {

            // Здесь можно сохранить выбранную позицию в настройках пользователя

            var message = $"📍 Позиция текста установлена: {position}";

            return Task.FromResult<object>(new { message });

        }

        catch (Exception ex)

        {

            return Task.FromResult<object>(new { message = $"❌ Ошибка установки позиции: {ex.Message}" });

        }

    }



    #endregion

}



public class BotStats

{

    public int TotalCommits { get; set; }

    public int TotalMessages { get; set; }

    public int ActiveUsers { get; set; }

    public int AiRequests { get; set; }

}

