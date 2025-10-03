using System.Text.Json;

namespace TelegramGitHubBot.Services;

public class MessageStatsService
{
    private readonly object _lockObject = new();
    private readonly string _dataDir;
    private readonly string _statsFilePath;

    private readonly Dictionary<long, ChatMessageStats> _chatStats = new();

    public MessageStatsService()
    {
        _dataDir = Environment.GetEnvironmentVariable("DATA_DIR")?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_dataDir))
        {
            _dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        }
        try { Directory.CreateDirectory(_dataDir); } catch { }
        _statsFilePath = Path.Combine(_dataDir, "message_stats.json");
        Load();
    }

    public (long totalChatMessages, long totalUserMessages, bool isNewChatMilestone, bool isNewUserMilestone, long reachedChatMilestone, long reachedUserMilestone) RegisterMessage(long chatId, long userId)
    {
        lock (_lockObject)
        {
            if (!_chatStats.TryGetValue(chatId, out var chat))
            {
                chat = new ChatMessageStats { ChatId = chatId };
                _chatStats[chatId] = chat;
            }

            chat.TotalMessages++;

            if (!chat.UserMessageCounts.TryGetValue(userId, out var userCount))
            {
                userCount = 0;
            }
            userCount++;
            chat.UserMessageCounts[userId] = userCount;

            var (isChatMilestone, chatMilestone) = ComputeNextChatMilestone(chat.TotalMessages, chat.AnnouncedChatMilestones);
            var (isUserMilestone, userMilestone) = ComputeUserMilestone(userCount, chat.AnnouncedUserMilestones, userId);

            if (isChatMilestone)
            {
                chat.AnnouncedChatMilestones.Add(chatMilestone);
            }
            if (isUserMilestone)
            {
                if (!chat.AnnouncedUserMilestones.TryGetValue(userId, out var set))
                {
                    set = new HashSet<long>();
                    chat.AnnouncedUserMilestones[userId] = set;
                }
                set.Add(userMilestone);
            }

            Save();

            return (chat.TotalMessages, userCount, isChatMilestone, isUserMilestone, chatMilestone, userMilestone);
        }
    }

    public ChatMessageStats? GetChatStats(long chatId)
    {
        lock (_lockObject)
        {
            return _chatStats.TryGetValue(chatId, out var chat) ? chat : null;
        }
    }

    public long GetUserMessageCount(long chatId, long userId)
    {
        lock (_lockObject)
        {
            if (_chatStats.TryGetValue(chatId, out var chat) && chat.UserMessageCounts.TryGetValue(userId, out var c))
            {
                return c;
            }
            return 0;
        }
    }

    public List<(long userId, long count)> GetTopUsers(long chatId, int top = 5)
    {
        lock (_lockObject)
        {
            if (!_chatStats.TryGetValue(chatId, out var chat)) return new List<(long, long)>();
            return chat.UserMessageCounts
                .OrderByDescending(kv => kv.Value)
                .Take(top)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();
        }
    }

    private static (bool, long) ComputeNextChatMilestone(long total, HashSet<long> announced)
    {
        if (total < 20000) return (false, 0); // 10000 уже было, не анонсируем
        if (total % 10000 != 0) return (false, 0);
        var milestone = total;
        if (announced.Contains(milestone)) return (false, 0);
        return (true, milestone);
    }

    private static (bool, long) ComputeUserMilestone(long userCount, Dictionary<long, HashSet<long>> announcedUser, long userId)
    {
        // Пользовательские: только 10000 по требованию
        if (userCount != 10000) return (false, 0);
        if (announcedUser.TryGetValue(userId, out var set) && set.Contains(10000))
        {
            return (false, 0);
        }
        return (true, 10000);
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_statsFilePath))
            {
                var json = File.ReadAllText(_statsFilePath);
                var data = JsonSerializer.Deserialize<Dictionary<long, ChatMessageStats>>(json);
                if (data != null)
                {
                    foreach (var kv in data)
                    {
                        _chatStats[kv.Key] = kv.Value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Ошибка загрузки message stats: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_chatStats, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_statsFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Ошибка сохранения message stats: {ex.Message}");
        }
    }

    public int GetTotalMessages()
    {
        lock (_lockObject)
        {
            return (int)_chatStats.Values.Sum(c => c.TotalMessages);
        }
    }

    public int GetActiveUsersCount()
    {
        lock (_lockObject)
        {
            return _chatStats.Values.SelectMany(c => c.UserMessageCounts.Keys).Distinct().Count();
        }
    }
}

public class ChatMessageStats
{
    public long ChatId { get; set; }
    public long TotalMessages { get; set; }
    public Dictionary<long, long> UserMessageCounts { get; set; } = new();
    public HashSet<long> AnnouncedChatMilestones { get; set; } = new();
    public Dictionary<long, HashSet<long>> AnnouncedUserMilestones { get; set; } = new();
}


