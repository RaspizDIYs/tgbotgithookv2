using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace TelegramGitHubBot.Services;

public class ChatActivityTracker
{
    private readonly ConcurrentDictionary<long, ChatActivity> _chatActivities = new();
    private readonly TenorService _tenorService;
    private readonly GeminiManager _geminiManager;
    private readonly ITelegramBotClient _botClient;
    private readonly Func<long, bool> _isGeminiModeActive;
    private readonly Action<long, bool> _setGeminiMode;

    public ChatActivityTracker(TenorService tenorService, GeminiManager geminiManager, ITelegramBotClient botClient, Func<long, bool> isGeminiModeActive, Action<long, bool> setGeminiMode)
    {
        _tenorService = tenorService;
        _geminiManager = geminiManager;
        _botClient = botClient;
        _isGeminiModeActive = isGeminiModeActive;
        _setGeminiMode = setGeminiMode;
    }

    public async Task TrackMessageAsync(long chatId, long userId, string username, string messageText, DateTime timestamp)
    {
        var activity = _chatActivities.GetOrAdd(chatId, _ => new ChatActivity());
        var shouldSendMeme = false;

        lock (activity)
        {
            // Добавляем сообщение
            activity.Messages.Add(new ChatActivityMessage
            {
                UserId = userId,
                Username = username,
                Text = messageText,
                Timestamp = timestamp
            });

            // Очищаем старые сообщения (старше 5 минут)
            var cutoffTime = timestamp.AddMinutes(-5);
            activity.Messages.RemoveAll(m => m.Timestamp < cutoffTime);

            // Проверяем условия для активации AI
            if (ShouldActivateAI(activity))
            {
                activity.AIActivated = true;
                activity.AIActivationTime = timestamp;
            }

            // Атомарно «застолбляем» отправку мема, чтобы два параллельных
            // сообщения не отправили его дважды.
            if (activity.AIActivated && !activity.MemeSent)
            {
                activity.MemeSent = true;
                shouldSendMeme = true;
            }
        }

        if (shouldSendMeme)
        {
            await AnalyzeAndSendMemeAsync(chatId, activity);
        }
    }

    private bool ShouldActivateAI(ChatActivity activity)
    {
        if (activity.AIActivated) return false;

        // Проверяем количество уникальных пользователей (минимум 2)
        var uniqueUsers = activity.Messages.Select(m => m.UserId).Distinct().Count();
        if (uniqueUsers < 2) return false;

        // Проверяем общее количество сообщений (минимум 5)
        if (activity.Messages.Count < 5) return false;

        // Проверяем интервал времени (все сообщения в пределах 5 минут)
        var oldestMessage = activity.Messages.Min(m => m.Timestamp);
        var newestMessage = activity.Messages.Max(m => m.Timestamp);
        var timeSpan = newestMessage - oldestMessage;
        
        return timeSpan.TotalMinutes <= 5;
    }

    private async Task AnalyzeAndSendMemeAsync(long chatId, ChatActivity activity)
    {
        try
        {
            // Анализируем контекст сообщений
            var context = AnalyzeMessageContext(activity);
            
            // Получаем подходящий мем
            var meme = await GetContextualMemeAsync(context);
            
            if (meme != null)
            {
                await _botClient.SendAnimationAsync(chatId, 
                    InputFile.FromUri(meme.Url), 
                    caption: $"🎬 {meme.Title}", 
                    disableNotification: true);

                // MemeSent уже выставлен атомарно в TrackMessageAsync под локом.

                // Отправляем сообщение о том, что AI присоединился к диалогу
                await _botClient.SendTextMessageAsync(chatId, 
                    "🤖 *AI присоединился к диалогу!*\n\nОбнаружил оживленную беседу и решил добавить мем 😄\n\nТеперь AI активен и будет отвечать на сообщения!", 
                    parseMode: ParseMode.Markdown, 
                    disableNotification: true);
                
                // Активируем AI режим для этого чата
                await ActivateAIModeAsync(chatId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error analyzing and sending meme: {ex.Message}");
        }
    }

    private string AnalyzeMessageContext(ChatActivity activity)
    {
        var allText = string.Join(" ", activity.Messages.Select(m => m.Text));
        var lowerText = allText.ToLower();

        // Определяем тему диалога
        if (lowerText.Contains("программирование") || lowerText.Contains("код") || lowerText.Contains("разработка"))
            return "programming coding development";
        
        if (lowerText.Contains("игра") || lowerText.Contains("гейм") || lowerText.Contains("игровой"))
            return "gaming video games";
        
        if (lowerText.Contains("мем") || lowerText.Contains("прикол") || lowerText.Contains("смешно"))
            return "memes funny humor";
        
        if (lowerText.Contains("работа") || lowerText.Contains("офис") || lowerText.Contains("проект"))
            return "work office business";
        
        if (lowerText.Contains("еда") || lowerText.Contains("готовить") || lowerText.Contains("рецепт"))
            return "food cooking recipe";
        
        if (lowerText.Contains("путешествие") || lowerText.Contains("отпуск") || lowerText.Contains("поездка"))
            return "travel vacation trip";
        
        if (lowerText.Contains("спорт") || lowerText.Contains("тренировка") || lowerText.Contains("фитнес"))
            return "sports fitness workout";
        
        if (lowerText.Contains("музыка") || lowerText.Contains("песня") || lowerText.Contains("концерт"))
            return "music concert song";
        
        if (lowerText.Contains("фильм") || lowerText.Contains("кино") || lowerText.Contains("сериал"))
            return "movie film series";
        
        if (lowerText.Contains("технологии") || lowerText.Contains("гаджет") || lowerText.Contains("устройство"))
            return "technology gadgets devices";
        
        // По умолчанию - общие мемы
        return "memes funny general";
    }

    private async Task<TenorGif?> GetContextualMemeAsync(string context)
    {
        try
        {
            var gifs = await _tenorService.SearchGifsAsync(context, 10);
            if (gifs.Count > 0)
            {
                var random = new Random();
                return gifs[random.Next(gifs.Count)];
            }
            
            // Fallback на общие мемы
            var fallbackGifs = await _tenorService.SearchGifsAsync("memes funny", 5);
            if (fallbackGifs.Count > 0)
            {
                var random = new Random();
                return fallbackGifs[random.Next(fallbackGifs.Count)];
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error getting contextual meme: {ex.Message}");
            return null;
        }
    }

    private Task ActivateAIModeAsync(long chatId)
    {
        try
        {
            // Проверяем, не активен ли уже AI режим
            if (_isGeminiModeActive(chatId))
            {
                return Task.CompletedTask;
            }
            
            // Активируем AI режим
            _setGeminiMode(chatId, true);
            
            // Очищаем контекст для нового диалога
            _geminiManager.ClearContext(chatId);
            
            Console.WriteLine($"🤖 AI mode activated for chat {chatId} due to active dialogue");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error activating AI mode: {ex.Message}");
            return Task.CompletedTask;
        }
    }

    public void ResetActivity(long chatId)
    {
        _chatActivities.TryRemove(chatId, out _);
    }

    public ChatActivity? GetActivity(long chatId)
    {
        _chatActivities.TryGetValue(chatId, out var activity);
        return activity;
    }
}

public class ChatActivity
{
    public List<ChatActivityMessage> Messages { get; set; } = new();
    public bool AIActivated { get; set; } = false;
    public DateTime AIActivationTime { get; set; }
    public bool MemeSent { get; set; } = false;
}

public class ChatActivityMessage
{
    public long UserId { get; set; }
    public string Username { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime Timestamp { get; set; }
}
