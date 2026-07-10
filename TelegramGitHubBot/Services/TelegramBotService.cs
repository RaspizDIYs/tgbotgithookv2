using System.Collections.Generic;
using System.Collections.Concurrent;

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




public partial class TelegramBotService

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

    private readonly ConcurrentDictionary<string, System.Timers.Timer> _messageTimers = new();

    private static System.Timers.Timer? _dailySummaryTimer;

    private static readonly object _dailySummaryTimerLock = new object();

    private readonly Dictionary<long, int> _swearWordCounters = new();

    private readonly HashSet<string> _swearWords = new();

    private readonly Dictionary<long, bool> _geminiMode = new();

    // Буфер последних сообщений чата для /tldr (KAN-61)
    private readonly Dictionary<long, Queue<string>> _recentMessages = new();
    private const int MAX_RECENT_MESSAGES = 100;

    private readonly TenorService _tenorService;
    private readonly JiraService _jiraService;
    private readonly GlitchTipService _glitchTipService;

    private readonly ChatActivityTracker _chatActivityTracker;

    private static readonly Random _random = new Random();


    private readonly Dictionary<long, Stack<string>> _navigationStack = new(); // Стек навигации для каждого чата

    // «Живой» навигационный экран на чат: один экран, который редактируется на месте
    // вместо спама новыми сообщениями. _pendingEditMessageId выставляется на время
    // обработки callback, чтобы рендер редактировал именно нажатое сообщение.
    private readonly ConcurrentDictionary<long, int> _navMessageId = new();
    private readonly ConcurrentDictionary<long, int> _pendingEditMessageId = new();



    public TelegramBotService(ITelegramBotClient botClient, GitHubService gitHubService, AchievementService achievementService, GeminiManager geminiManager, MessageStatsService messageStatsService, TenorService tenorService, JiraService jiraService, GlitchTipService glitchTipService)

    {

        _botClient = botClient;

        _gitHubService = gitHubService ?? throw new ArgumentNullException(nameof(gitHubService));

        _achievementService = achievementService ?? throw new ArgumentNullException(nameof(achievementService));

        _geminiManager = geminiManager ?? throw new ArgumentNullException(nameof(geminiManager));

        _messageStatsService = messageStatsService ?? throw new ArgumentNullException(nameof(messageStatsService));

        _tenorService = tenorService ?? throw new ArgumentNullException(nameof(tenorService));

        _jiraService = jiraService ?? throw new ArgumentNullException(nameof(jiraService));

        _glitchTipService = glitchTipService ?? throw new ArgumentNullException(nameof(glitchTipService));

        

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

        }






        // Если режим Gemini активен, отправляем все сообщения в AI

        if (_geminiMode.ContainsKey(chatId) && _geminiMode[chatId])

        {

            try

            {

                var aiResponse = await _geminiManager.GenerateResponseWithContextAsync(text, chatId);
                await _botClient.SendTextMessageAsync(chatId, aiResponse, parseMode: ParseMode.Markdown, disableNotification: true);

            }

            catch (Exception ex)

            {

                var errorMessage = $"❌ **Ошибка AI:** {ex.Message}\n\n" + _geminiManager.GetCurrentAgentStatus();

                await _botClient.SendTextMessageAsync(chatId, errorMessage, parseMode: ParseMode.Markdown, disableNotification: true);

            }

            return;

        }



        // Обычная обработка команд






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

                            // Агентный режим: LLM может дергать собственные команды бота
                            // (коммиты, PR, CI, статистика) как инструменты и суммаризовать.
                            await RunAgenticAskAsync(chatId, question);

                        }

                        catch (Exception ex)

                        {

                            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка: {ex.Message}", disableNotification: true);

                        }

                    }

                    break;



                case "/tldr":


                    await HandleTldrCommandAsync(chatId);

                    break;



                case "/settings":


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


                    await HandlePullRequestsCommandAsync(chatId);

                    break;



                case "/ci":


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







                case "/achievements":



                    await ShowAchievementPageAsync(chatId, 0, null);

                    break;



                case "/leaderboard":


                    await HandleLeaderboardCommandAsync(chatId);

                    break;



                case "/streaks":


                    await HandleStreaksCommandAsync(chatId);

                    break;

                case "/recalc":

                    await HandleRecalcCommandAsync(chatId);

                    break;





                case "/info":

                    await SendInfoMessageAsync(chatId);

                    break;



                case "/ratelimit":


                    await HandleRateLimitCommandAsync(chatId);

                    break;



                case "/cache":


                    await HandleCacheInfoCommandAsync(chatId);

                    break;



                case "/protection":

                case "/backup":

                    await HandleDataProtectionCommandAsync(chatId);

                    break;

                case "/jira":

                case "/kan":

                    await HandleJiraDigestAsync(chatId);

                    break;

                case "/digest":

                case "/plan":

                    await HandleDigestCommandAsync(chatId);

                    break;

                case "/stale":

                    await HandleStaleCommandAsync(chatId);

                    break;

                case "/monthly":

                    await HandleMonthlyCommandAsync(chatId);

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
        await ShowNavScreenAsync(chatId, message, inlineKeyboard, parseMode);
    }

    private async Task EditMessageWithBackButtonAsync(long chatId, int messageId, string message, string? backCommand = null, ParseMode parseMode = ParseMode.Markdown)
    {
        var actualBackCommand = backCommand ?? PeekNavigation(chatId) ?? "/help";
        var inlineKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", actualBackCommand) }
        });
        await _botClient.EditMessageTextAsync(chatId, messageId, message, parseMode: parseMode, replyMarkup: inlineKeyboard);
        _navMessageId[chatId] = messageId;
    }

    /// <summary>
    /// Рендерит «живой» навигационный экран чата. Если известен id текущего экрана
    /// (нажатая кнопка или предыдущий экран) — редактирует его на месте, иначе шлёт
    /// новое сообщение. Так один экран обновляется вместо спама новыми сообщениями.
    /// </summary>
    private async Task ShowNavScreenAsync(long chatId, string text, InlineKeyboardMarkup? keyboard, ParseMode parseMode = ParseMode.Markdown)
    {
        int? target = null;
        if (_pendingEditMessageId.TryGetValue(chatId, out var pe)) target = pe;
        else if (_navMessageId.TryGetValue(chatId, out var nv)) target = nv;

        if (target is int mid)
        {
            try
            {
                await _botClient.EditMessageTextAsync(chatId, mid, text, parseMode: parseMode, replyMarkup: keyboard);
                _navMessageId[chatId] = mid;
                return;
            }
            catch (Telegram.Bot.Exceptions.ApiRequestException ex) when (ex.Message.Contains("message is not modified"))
            {
                _navMessageId[chatId] = mid; // контент не изменился — экран и так актуален
                return;
            }
            catch
            {
                // Не смогли отредактировать (сообщение удалено/это медиа/слишком старое) —
                // удалим старое и отправим новое.
                try { await _botClient.DeleteMessageAsync(chatId, mid); } catch { }
            }
        }

        var sent = await _botClient.SendTextMessageAsync(chatId, text, parseMode: parseMode, replyMarkup: keyboard, disableNotification: true);
        _navMessageId[chatId] = sent.MessageId;
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

            // Рендер во время обработки callback редактирует именно нажатое сообщение
            // (единый «живой» экран вместо новых сообщений).
            _pendingEditMessageId[chatId] = messageId;

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

                await HandleNotificationToggleAsync(chatId, data, messageId, callbackQuery.Id);

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

        finally
        {
            _pendingEditMessageId.TryRemove(chatId, out _);
        }

    }



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
}
