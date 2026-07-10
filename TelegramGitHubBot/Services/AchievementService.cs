using TelegramGitHubBot.Models;
using System.Text.Json;
using System.Collections.Concurrent;
using Octokit;

namespace TelegramGitHubBot.Services;

// Класс для запланированной статистики
public class ScheduledStats
{
    public string Data { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdated { get; set; }
    public string Type { get; set; } = string.Empty; // "commits", "authors", "weekly", "rating", etc.
    public string Parameters { get; set; } = string.Empty; // Дополнительные параметры (ветка, количество и т.д.)
}

// Класс для кэшированной статистики
public class CachedStats
{
    public string Data { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessed { get; set; }
    public int AccessCount { get; set; }
    public string Type { get; set; } = string.Empty; // "authors", "commits", "status", etc.
}

public class AchievementService
{
    private readonly ConcurrentDictionary<long, UserStats> _userStats = new();
    private readonly ConcurrentDictionary<string, Achievement> _achievements = new();
    private readonly List<AchievementDefinition> _achievementDefinitions;
    private readonly string _dataDir;
    private readonly string _dataFilePath;
    private readonly string _achievementsFilePath;
    private readonly string _processedShasFilePath;
    private readonly string? _persistOwner;
    private readonly string? _persistRepo;
    private readonly string _persistPath = "tgbot_stats.json";
    private readonly string _persistBranch = "main";
    private GitHubClient? _ghClient;
    // ConcurrentDictionary как потокобезопасное множество: атомарный TryAdd для дедупа SHA.
    private readonly ConcurrentDictionary<string, byte> _processedShas = new();
    
    // Настройки кэша
    private readonly int _maxProcessedShas = 10000; // Максимум SHA в кэше
    private readonly int _maxInactiveUsers = 50; // Максимум неактивных пользователей
    private readonly int _inactiveDaysThreshold = 90; // Дней неактивности
    
    // Расписание автообновления (9:00, 18:00, 00:00 МСК)
    private readonly int[] _updateHours = { 9, 18, 0 };
    private DateTime _lastScheduledUpdate = DateTime.MinValue;
    private readonly Dictionary<string, ScheduledStats> _scheduledStatsCache = new();
    
    // Защита от потери данных
    private readonly Dictionary<string, ScheduledStats> _backupStatsCache = new();
    private readonly int _minApiCallsThreshold = 100; // Минимум API вызовов для безопасного обновления
    private readonly int _maxApiCallsPerUpdate = 50; // Максимум API вызовов за одно обновление
    private DateTime _lastApiResetCheck = DateTime.MinValue;
    private readonly object _lockObject = new object(); // Блокировка для конкурентного доступа
    
    // Кэш статистики
    private readonly ConcurrentDictionary<string, CachedStats> _statsCache = new();
    private readonly int _maxCachedStats = 100;
    private readonly int _statsCacheDays = 7;
    private DateTime _lastAutoRefresh = DateTime.MinValue;
    private readonly int _autoRefreshIntervalHours = 24;

    public AchievementService()
    {
        _achievementDefinitions = InitializeAchievementDefinitions();

        // Определяем директорию хранения (персистентный диск)
        _dataDir = Environment.GetEnvironmentVariable("DATA_DIR")?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(_dataDir))
        {
            _dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        }
        try
        {
            Directory.CreateDirectory(_dataDir);
        }
        catch { }

        _dataFilePath = Path.Combine(_dataDir, "user_stats.json");
        _achievementsFilePath = Path.Combine(_dataDir, "achievements.json");
        _processedShasFilePath = Path.Combine(_dataDir, "processed_shas.json");

        // Настройка GitHub персистенса (опционально)
        var persistOwner = Environment.GetEnvironmentVariable("GITHUB_PERSIST_OWNER");
        var persistRepo = Environment.GetEnvironmentVariable("GITHUB_PERSIST_REPO");
        var persistPath = Environment.GetEnvironmentVariable("GITHUB_PERSIST_PATH");
        var persistBranch = Environment.GetEnvironmentVariable("GITHUB_PERSIST_BRANCH");
        var pat = Environment.GetEnvironmentVariable("GITHUB_PAT");
        if (!string.IsNullOrWhiteSpace(persistOwner) && !string.IsNullOrWhiteSpace(persistRepo) && !string.IsNullOrWhiteSpace(pat))
        {
            _persistOwner = persistOwner!.Trim();
            _persistRepo = persistRepo!.Trim();
            if (!string.IsNullOrWhiteSpace(persistPath)) _persistPath = persistPath!.Trim();
            if (!string.IsNullOrWhiteSpace(persistBranch)) _persistBranch = persistBranch!.Trim();
            _ghClient = new GitHubClient(new ProductHeaderValue("TelegramGitHubBot"))
            {
                Credentials = new Credentials(pat.Trim())
            };
        }

        // Пытаемся загрузить сперва из GitHub (если настроено), иначе — из локального файла
        if (!(LoadUserStatsFromGitHub() || LoadUserStats()))
        {
            // ничего
        }
        LoadAchievements();
        LoadProcessedShas();
        LoadScheduledStatsFromFile();
    }

    private List<AchievementDefinition> InitializeAchievementDefinitions()
    {
        return new List<AchievementDefinition>
        {
            new()
            {
                Id = "commits_emperor",
                Name = "Commits Emperor",
                Description = "Император коммитов - больше всех коммитов в проекте",
                GifUrl = "https://media1.tenor.com/m/4WZiORkx-EgAAAAd/joaquin-phoenix-commodus.gif",
                Emoji = "👑",
                Type = AchievementType.TotalCommits,
                IsRecordBased = true
            },
            new()
            {
                Id = "speedster",
                Name = "Speedster",
                Description = "Скоростной кодер - максимальное количество изменений строк в одном коммите",
                GifUrl = "https://media1.tenor.com/m/ITc1hNBSH_wAAAAd/coding-typing.gif",
                Emoji = "⚡",
                Type = AchievementType.MaxLinesChanged,
                IsRecordBased = true
            },
            new()
            {
                Id = "motivating_leader",
                Name = "Motivating Leader",
                Description = "Мотивирующий лидер - самое долгое время без коммитов",
                GifUrl = "https://media1.tenor.com/m/LoXuYGcyMxgAAAAd/just-do-it-shia-la-beouf.gif",
                Emoji = "🔥",
                Type = AchievementType.LongestBreak,
                IsRecordBased = true
            },
            new()
            {
                Id = "weekend_warrior",
                Name = "Weekend Warrior",
                Description = "Выходной воин - коммиты в выходные",
                GifUrl = "https://media1.tenor.com/m/gv1_d-p0AmwAAAAC/megaman-battle-network-megaman-nt-warrior.gif",
                Emoji = "🏆",
                Type = AchievementType.WeekendWarrior,
                IsRecordBased = false,
                RequiredValue = 1
            },
            new()
            {
                Id = "night_owl",
                Name = "Night Owl",
                Description = "Ночная сова - коммиты после 22:00",
                GifUrl = "https://media1.tenor.com/m/G9wtd4WhwXIAAAAC/lain-computer.gif",
                Emoji = "🦉",
                Type = AchievementType.NightOwl,
                IsRecordBased = false,
                RequiredValue = 1
            },
            new()
            {
                Id = "early_bird",
                Name = "Early Bird",
                Description = "Ранняя пташка - коммиты до 8:00",
                GifUrl = "https://media1.tenor.com/m/8qBCQb3FJvgAAAAC/good-morning.gif",
                Emoji = "🐦",
                Type = AchievementType.EarlyBird,
                IsRecordBased = false,
                RequiredValue = 1
            },
            new()
            {
                Id = "bug_hunter",
                Name = "Bug Hunter",
                Description = "Охотник на баги - коммиты с 'fix', 'bug', 'error'",
                GifUrl = "https://media1.tenor.com/m/FZSQrGEIhnsAAAAC/democracy-helldivers.gif",
                Emoji = "🐛",
                Type = AchievementType.BugHunter,
                IsRecordBased = false,
                RequiredValue = 5
            },
            new()
            {
                Id = "feature_master",
                Name = "Feature Master",
                Description = "Мастер фич - коммиты с 'feat', 'feature', 'add'",
                GifUrl = "https://media1.tenor.com/m/IDV0S6JuDx0AAAAd/bry-brysupersaurus.gif",
                Emoji = "✨",
                Type = AchievementType.FeatureMaster,
                IsRecordBased = false,
                RequiredValue = 10
            },
            new()
            {
                Id = "refactor_king",
                Name = "Refactor King",
                Description = "Король рефакторинга - коммиты с 'refactor', 'clean', 'optimize'",
                GifUrl = "https://media1.tenor.com/m/58wVviY_niEAAAAC/odoo-refactoring.gif",
                Emoji = "♻️",
                Type = AchievementType.RefactorKing,
                IsRecordBased = false,
                RequiredValue = 3
            },
            new()
            {
                Id = "streak_master",
                Name = "Streak Master",
                Description = "Мастер стриков - коммиты подряд без перерыва",
                GifUrl = "https://media1.tenor.com/m/pyVuBW9e6zsAAAAC/touch-grass.gif",
                Emoji = "🔥",
                Type = AchievementType.StreakMaster,
                IsRecordBased = true
            },
            new()
            {
                Id = "test_champion",
                Name = "Test Champion",
                Description = "Чемпион тестов - коммиты с тестами",
                GifUrl = "https://media1.tenor.com/m/pXe5Lu_fQgUAAAAd/shaquille-oneal-taste-test.gif",
                Emoji = "🧪",
                Type = AchievementType.TestChampion,
                IsRecordBased = false,
                RequiredValue = 5
            },
            new()
            {
                Id = "release_genius",
                Name = "Release Genius",
                Description = "Гений релизов - коммиты с версиями",
                GifUrl = "https://media1.tenor.com/m/xgZ9cq7vVggAAAAC/jimmy-neutron-cool-photos.gif",
                Emoji = "🚀",
                Type = AchievementType.ReleaseGenius,
                IsRecordBased = false,
                RequiredValue = 3
            }
            ,
            new()
            {
                Id = "druid_branch_master",
                Name = "Druid",
                Description = "Друид - больше всех создал веток",
                GifUrl = "https://media1.tenor.com/m/vOZ-PWzaUvkAAAAd/druid-of-the-talon-druid.gif",
                Emoji = "🌿",
                Type = AchievementType.BranchCreator,
                IsRecordBased = true
            }
        };
    }

    public void ProcessCommit(string author, string email, string commitMessage, DateTime commitDate, int linesAdded, int linesDeleted)
    {
        ProcessCommitInternal(author, email, commitMessage, commitDate, linesAdded, linesDeleted);
        SaveUserStats();
        SaveAchievements();
    }

    public void ProcessCommitBatch(string author, string email, string commitMessage, DateTime commitDate, int linesAdded, int linesDeleted)
    {
        ProcessCommitInternal(author, email, commitMessage, commitDate, linesAdded, linesDeleted);
    }

    public void SaveAll()
    {
        SaveUserStats();
        SaveAchievements();
    }

    private void ProcessCommitInternal(string author, string email, string commitMessage, DateTime commitDate, int linesAdded, int linesDeleted)
    {
        var userId = GetOrCreateUserId(author, email);
        var stats = GetOrCreateUserStats(userId, author);
        
        // Обновляем основную статистику
        stats.TotalCommits++;
        stats.MaxLinesChanged = Math.Max(stats.MaxLinesChanged, linesAdded + linesDeleted);
        
        if (stats.FirstCommitDate == null)
            stats.FirstCommitDate = commitDate;
        
        // Обновляем стрик
        UpdateStreak(stats, commitDate);
        
        stats.LastCommitDate = commitDate;
        stats.LastUpdated = DateTime.UtcNow;
        
        // Анализируем тип коммита
        AnalyzeCommitType(stats, commitMessage, commitDate);
        
        // Пересчитываем самый долгий перерыв
        RecalculateLongestBreak(stats);
        
        // Проверяем ачивки
        CheckAchievements(stats);
    }

    public void RegisterBranchCreated(string author, string email, DateTime createdAt)
    {
        var userId = GetOrCreateUserId(author, email);
        var stats = GetOrCreateUserStats(userId, author);

        stats.BranchesCreated++;
        stats.LastUpdated = DateTime.UtcNow;

        CheckAchievements(stats);
        SaveUserStats();
        SaveAchievements();
    }

    public void ResetAllData()
    {
        _userStats.Clear();
        _achievements.Clear();
        _processedShas.Clear();
        SaveUserStats();
        SaveAchievements();
        SaveProcessedShas();
    }

    public void ProcessCommitIfNew(string sha, string author, string email, string commitMessage, DateTime commitDate, int linesAdded, int linesDeleted)
    {
        if (string.IsNullOrWhiteSpace(sha))
        {
            ProcessCommit(author, email, commitMessage, commitDate, linesAdded, linesDeleted);
            return;
        }
        if (_processedShas.ContainsKey(sha)) return;
        ProcessCommit(author, email, commitMessage, commitDate, linesAdded, linesDeleted);
        _processedShas.TryAdd(sha, 0);
        SaveProcessedShas();
    }

    private long GetOrCreateUserId(string author, string email)
    {
        // ВАЖНО: string.GetHashCode() рандомизируется на каждый запуск процесса,
        // поэтому ID менялся после каждого рестарта и статистика фрагментировалась.
        // Используем детерминированный FNV-1a по нормализованному ключу.
        var key = !string.IsNullOrWhiteSpace(email) ? email : author;
        return StableHash(key.Trim().ToLowerInvariant());
    }

    /// <summary>Детерминированный 63-битный хеш (FNV-1a), стабильный между запусками.</summary>
    private static long StableHash(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= prime;
        }
        return (long)(hash & 0x7FFFFFFFFFFFFFFFUL); // всегда неотрицательный
    }

    private UserStats GetOrCreateUserStats(long userId, string author)
    {
        return _userStats.GetOrAdd(userId, _ => new UserStats
        {
            TelegramUserId = userId,
            Username = author,
            DisplayName = author
        });
    }

    private void UpdateStreak(UserStats stats, DateTime commitDate)
    {
        if (stats.LastCommitDate == null)
        {
            stats.CurrentStreak = 1;
            stats.LongestStreak = 1;
            return;
        }

        var daysDifference = (commitDate.Date - stats.LastCommitDate.Value.Date).Days;
        
        if (daysDifference == 1)
        {
            // Коммит на следующий день - увеличиваем стрик
            stats.CurrentStreak++;
        }
        else if (daysDifference == 0)
        {
            // Коммит в тот же день - стрик не меняется
        }
        else
        {
            // Перерыв больше дня - сбрасываем стрик
            stats.CurrentStreak = 1;
        }
        
        // Обновляем самый длинный стрик
        stats.LongestStreak = Math.Max(stats.LongestStreak, stats.CurrentStreak);
    }

    private void AnalyzeCommitType(UserStats stats, string commitMessage, DateTime commitDate)
    {
        var message = commitMessage.ToLower();
        var hour = commitDate.Hour;
        var dayOfWeek = commitDate.DayOfWeek;
        
        // Тесты
        if (message.Contains("test") || message.Contains("spec") || message.Contains("specs"))
        {
            stats.TestCommits++;
        }
        
        // Релизы
        if (message.Contains("release") || message.Contains("version") || message.Contains("v1.") || 
            message.Contains("v2.") || message.Contains("v3.") || message.Contains("v4.") || 
            message.Contains("v5.") || message.Contains("v6.") || message.Contains("v7.") || 
            message.Contains("v8.") || message.Contains("v9.") || message.Contains("v0."))
        {
            stats.ReleaseCommits++;
        }
        
        // Выходные
        if (dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday)
        {
            stats.WeekendCommits++;
        }
        
        // Ночные коммиты (22:00 - 06:00)
        if (hour >= 22 || hour < 6)
        {
            stats.NightCommits++;
        }
        
        // Ранние коммиты (06:00 - 08:00)
        if (hour >= 6 && hour < 8)
        {
            stats.EarlyCommits++;
        }
        
        // Багфиксы
        if (message.Contains("fix") || message.Contains("bug") || message.Contains("error") || 
            message.Contains("issue") || message.Contains("resolve"))
        {
            stats.BugFixCommits++;
        }
        
        // Фичи
        if (message.Contains("feat") || message.Contains("feature") || message.Contains("add") || 
            message.Contains("implement") || message.Contains("new"))
        {
            stats.FeatureCommits++;
        }
        
        // Рефакторинг
        if (message.Contains("refactor") || message.Contains("clean") || message.Contains("optimize") || 
            message.Contains("improve") || message.Contains("restructure"))
        {
            stats.RefactorCommits++;
        }
    }

    private void RecalculateLongestBreak(UserStats stats)
    {
        if (stats.FirstCommitDate == null || stats.LastCommitDate == null)
            return;

        // Простая логика: считаем дни между первым и последним коммитом
        // В реальности нужно анализировать все коммиты пользователя
        var totalDays = (stats.LastCommitDate.Value - stats.FirstCommitDate.Value).Days;
        stats.LongestBreakDays = Math.Max(stats.LongestBreakDays, totalDays / 10); // Упрощенная формула
    }

    private void CheckAchievements(UserStats stats)
    {
        foreach (var definition in _achievementDefinitions)
        {
            var achievementId = definition.Id;
            
            if (_achievements.ContainsKey(achievementId) && definition.IsRecordBased)
            {
                // Для рекордных ачивок проверяем, побит ли рекорд
                var currentAchievement = _achievements[achievementId];
                var newValue = GetValueForAchievement(stats, definition.Type);
                
                if (newValue > (currentAchievement.Value ?? 0))
                {
                    // Новый рекорд!
                    _achievements[achievementId] = new Achievement
                    {
                        Id = achievementId,
                        Name = definition.Name,
                        Description = definition.Description,
                        GifUrl = definition.GifUrl,
                        Emoji = definition.Emoji,
                        IsUnlocked = true,
                        UnlockedAt = DateTime.UtcNow,
                        HolderUserId = stats.TelegramUserId,
                        HolderName = stats.DisplayName,
                        Value = newValue
                    };
                }
            }
            else if (!_achievements.ContainsKey(achievementId))
            {
                // Первая проверка ачивки
                var value = GetValueForAchievement(stats, definition.Type);
                var shouldUnlock = definition.IsRecordBased ? 
                    value > 0 : 
                    value >= definition.RequiredValue;

                if (shouldUnlock)
                {
                    _achievements[achievementId] = new Achievement
                    {
                        Id = achievementId,
                        Name = definition.Name,
                        Description = definition.Description,
                        GifUrl = definition.GifUrl,
                        Emoji = definition.Emoji,
                        IsUnlocked = true,
                        UnlockedAt = DateTime.UtcNow,
                        HolderUserId = stats.TelegramUserId,
                        HolderName = stats.DisplayName,
                        Value = value
                    };
                }
            }
        }
    }

    private int GetValueForAchievement(UserStats stats, AchievementType type)
    {
        return type switch
        {
            AchievementType.TotalCommits => stats.TotalCommits,
            AchievementType.MaxLinesChanged => stats.MaxLinesChanged,
            AchievementType.LongestBreak => stats.LongestBreakDays,
            AchievementType.StreakMaster => stats.LongestStreak,
            AchievementType.WeekendWarrior => stats.WeekendCommits,
            AchievementType.NightOwl => stats.NightCommits,
            AchievementType.EarlyBird => stats.EarlyCommits,
            AchievementType.BugHunter => stats.BugFixCommits,
            AchievementType.FeatureMaster => stats.FeatureCommits,
            AchievementType.RefactorKing => stats.RefactorCommits,
            AchievementType.TestChampion => stats.TestCommits,
            AchievementType.ReleaseGenius => stats.ReleaseCommits,
            AchievementType.BranchCreator => stats.BranchesCreated,
            _ => 0
        };
    }

    public List<Achievement> GetAllAchievements()
    {
        return _achievements.Values.OrderBy(a => a.Name).ToList();
    }

    // Возвращает полный список из дефиниций, смерженный с текущими состояниями (показывает все ачивки)
    public List<Achievement> GetAllAchievementsMerged()
    {
        var result = new List<Achievement>();
        foreach (var def in _achievementDefinitions)
        {
            if (_achievements.TryGetValue(def.Id, out var ach))
            {
                // Обновляем поля из дефиниции (текст/эмодзи/гиф), состояние сохраняем
                result.Add(new Achievement
                {
                    Id = def.Id,
                    Name = def.Name,
                    Description = def.Description,
                    GifUrl = def.GifUrl,
                    Emoji = def.Emoji,
                    IsUnlocked = ach.IsUnlocked,
                    UnlockedAt = ach.UnlockedAt,
                    HolderUserId = ach.HolderUserId,
                    HolderName = ach.HolderName,
                    Value = ach.Value
                });
            }
            else
            {
                // Ещё не получена — показываем как заблокированную
                result.Add(new Achievement
                {
                    Id = def.Id,
                    Name = def.Name,
                    Description = def.Description,
                    GifUrl = def.GifUrl,
                    Emoji = def.Emoji,
                    IsUnlocked = false
                });
            }
        }
        return result.OrderBy(a => a.Name).ToList();
    }

    public List<UserStats> GetTopUsers(int count = 10)
    {
        return _userStats.Values
            .OrderByDescending(u => u.TotalCommits)
            .Take(count)
            .ToList();
    }

    public string GetStreakEmoji(int streak)
    {
        return streak switch
        {
            >= 30 => "🔥🔥🔥🔥", // Месяц и больше
            >= 21 => "🔥🔥🔥",   // 21+ дней
            >= 14 => "🔥🔥",     // 14+ дней
            >= 7 => "🔥",        // 7+ дней
            _ => ""
        };
    }

    public List<UserStats> GetTopUsersByStreak(int count = 10)
    {
        return _userStats.Values
            .OrderByDescending(u => u.LongestStreak)
            .Take(count)
            .ToList();
    }

    private bool LoadUserStats()
    {
        try
        {
            if (File.Exists(_dataFilePath))
            {
                var json = File.ReadAllText(_dataFilePath);
                var data = JsonSerializer.Deserialize<Dictionary<long, UserStats>>(json);
                if (data != null)
                {
                    foreach (var kvp in data)
                    {
                        _userStats[kvp.Key] = kvp.Value;
                    }
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка загрузки статистики: {ex.Message}");
        }
        return false;
    }

    private void SaveUserStats()
    {
        try
        {
            // Очищаем неактивных пользователей перед сохранением
            CleanupInactiveUsers();
            
            var json = JsonSerializer.Serialize(_userStats, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_dataFilePath, json);
            SaveUserStatsToGitHub(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка сохранения статистики: {ex.Message}");
        }
    }

    private void SaveAchievements()
    {
        try
        {
            var json = JsonSerializer.Serialize(_achievements, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_achievementsFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка сохранения достижений: {ex.Message}");
        }
    }

    private void LoadAchievements()
    {
        try
        {
            if (File.Exists(_achievementsFilePath))
            {
                var json = File.ReadAllText(_achievementsFilePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, Achievement>>(json);
                if (data != null)
                {
                    _achievements.Clear();
                    foreach (var kvp in data)
                    {
                        _achievements[kvp.Key] = kvp.Value;
                    }
                    Console.WriteLine($"✅ Загружено {_achievements.Count} достижений");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка загрузки достижений: {ex.Message}");
        }
    }

    private bool LoadUserStatsFromGitHub()
    {
        try
        {
            if (_ghClient == null || string.IsNullOrWhiteSpace(_persistOwner) || string.IsNullOrWhiteSpace(_persistRepo)) return false;
            var contents = _ghClient.Repository.Content.GetAllContentsByRef(_persistOwner!, _persistRepo!, _persistPath, _persistBranch).GetAwaiter().GetResult();
            if (contents != null && contents.Count > 0)
            {
                var json = contents[0].Content;
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var data = JsonSerializer.Deserialize<Dictionary<long, UserStats>>(json);
                    if (data != null)
                    {
                        _userStats.Clear();
                        foreach (var kvp in data) _userStats[kvp.Key] = kvp.Value;
                        // также сохраним локальную копию
                        File.WriteAllText(_dataFilePath, json);
                        Console.WriteLine("📥 Stats loaded from GitHub persist");
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Failed to load stats from GitHub: {ex.Message}");
        }
        return false;
    }

    private void SaveUserStatsToGitHub(string? jsonCache = null)
    {
        try
        {
            if (_ghClient == null || string.IsNullOrWhiteSpace(_persistOwner) || string.IsNullOrWhiteSpace(_persistRepo)) return;
            var json = jsonCache ?? JsonSerializer.Serialize(_userStats, new JsonSerializerOptions { WriteIndented = true });
            // Получаем SHA, чтобы определить Create/Update
            string? sha = null;
            try
            {
                var existing = _ghClient.Repository.Content.GetAllContentsByRef(_persistOwner!, _persistRepo!, _persistPath, _persistBranch).GetAwaiter().GetResult();
                if (existing != null && existing.Count > 0)
                {
                    sha = existing[0].Sha;
                }
            }
            catch { }

            var commitMessage = "chore(stats): update user_stats.json";
            if (string.IsNullOrEmpty(sha))
            {
                _ghClient.Repository.Content.CreateFile(_persistOwner!, _persistRepo!, _persistPath,
                    new CreateFileRequest(commitMessage, json, _persistBranch)).GetAwaiter().GetResult();
            }
            else
            {
                _ghClient.Repository.Content.UpdateFile(_persistOwner!, _persistRepo!, _persistPath,
                    new UpdateFileRequest(commitMessage, json, sha, _persistBranch)).GetAwaiter().GetResult();
            }
            Console.WriteLine("📤 Stats saved to GitHub persist");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Failed to save stats to GitHub: {ex.Message}");
        }
    }

    private void LoadProcessedShas()
    {
        try
        {
            if (File.Exists(_processedShasFilePath))
            {
                var json = File.ReadAllText(_processedShasFilePath);
                var data = JsonSerializer.Deserialize<HashSet<string>>(json);
                if (data != null)
                {
                    foreach (var sha in data) _processedShas.TryAdd(sha, 0);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка загрузки обработанных коммитов: {ex.Message}");
        }
    }

    private void SaveProcessedShas()
    {
        try
        {
            // Очищаем старые SHA если их слишком много
            CleanupProcessedShas();
            
            var json = JsonSerializer.Serialize(_processedShas.Keys.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_processedShasFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка сохранения обработанных коммитов: {ex.Message}");
        }
    }

    private void CleanupProcessedShas()
    {
        if (_processedShas.Count <= _maxProcessedShas) return;
        
        // Оставляем только последние SHA (новые коммиты важнее старых)
        var shasToKeep = _processedShas.Keys.TakeLast(_maxProcessedShas).ToHashSet();
        var removedCount = _processedShas.Count - shasToKeep.Count;
        
        _processedShas.Clear();
        foreach (var sha in shasToKeep)
        {
            _processedShas.TryAdd(sha, 0);
        }
        
        Console.WriteLine($"🧹 Очищено {removedCount} старых SHA из кэша (осталось {_processedShas.Count})");
    }

    private void CleanupInactiveUsers()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_inactiveDaysThreshold);
        var inactiveUsers = _userStats.Values
            .Where(u => u.LastCommitDate == null || u.LastCommitDate < cutoffDate)
            .OrderBy(u => u.LastCommitDate ?? DateTime.MinValue)
            .ToList();

        if (inactiveUsers.Count <= _maxInactiveUsers) return;

        var usersToRemove = inactiveUsers.Take(inactiveUsers.Count - _maxInactiveUsers).ToList();
        
        foreach (var user in usersToRemove)
        {
            _userStats.TryRemove(user.TelegramUserId, out _);
        }
        
        Console.WriteLine($"🧹 Удалено {usersToRemove.Count} неактивных пользователей (неактивны > {_inactiveDaysThreshold} дней)");
    }

    public (int userStatsCount, int achievementsCount, int processedShasCount, long totalSizeBytes) GetCacheInfo()
    {
        var userStatsSize = File.Exists(_dataFilePath) ? new FileInfo(_dataFilePath).Length : 0;
        var achievementsSize = File.Exists(_achievementsFilePath) ? new FileInfo(_achievementsFilePath).Length : 0;
        var processedShasSize = File.Exists(_processedShasFilePath) ? new FileInfo(_processedShasFilePath).Length : 0;
        
        return (_userStats.Count, _achievements.Count, _processedShas.Count, userStatsSize + achievementsSize + processedShasSize);
    }

    public void ForceCleanup()
    {
        CleanupProcessedShas();
        CleanupInactiveUsers();
        SaveAll();
    }
    
    // Автоматическое управление кэшем статистики
    public void CacheStats(string key, string data, string type)
    {
        _statsCache[key] = new CachedStats
        {
            Data = data,
            CreatedAt = DateTime.UtcNow,
            LastAccessed = DateTime.UtcNow,
            AccessCount = 1,
            Type = type
        };
        
        // Автоматическая очистка при превышении лимита
        if (_statsCache.Count > _maxCachedStats)
        {
            CleanupStatsCache();
        }
    }
    
    public string? GetCachedStats(string key)
    {
        if (_statsCache.TryGetValue(key, out var cached))
        {
            cached.LastAccessed = DateTime.UtcNow;
            cached.AccessCount++;
            return cached.Data;
        }
        return null;
    }
    
    public bool ShouldAutoRefresh()
    {
        return DateTime.UtcNow - _lastAutoRefresh > TimeSpan.FromHours(_autoRefreshIntervalHours);
    }
    
    public void MarkAutoRefresh()
    {
        _lastAutoRefresh = DateTime.UtcNow;
    }
    
    private void CleanupStatsCache()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_statsCacheDays);
        var toRemove = _statsCache
            .Where(kvp => kvp.Value.CreatedAt < cutoffDate)
            .OrderBy(kvp => kvp.Value.LastAccessed)
            .Take(_statsCache.Count - _maxCachedStats + 10) // Удаляем на 10 больше для буфера
            .Select(kvp => kvp.Key)
            .ToList();
            
        foreach (var key in toRemove)
        {
            _statsCache.TryRemove(key, out _);
        }
        
        Console.WriteLine($"🧹 Очищено {toRemove.Count} устаревших записей из кэша статистики");
    }
    
    // Умное сохранение данных - сохраняет важное, очищает старое
    public void SmartSave()
    {
        // Сохраняем пользователей с активностью за последние 30 дней
        var activeUsers = _userStats.Values
            .Where(u => u.LastCommitDate > DateTime.UtcNow.AddDays(-30))
            .ToList();
            
        // Сохраняем ачивки активных пользователей
        var activeAchievements = _achievements.Values
            .Where(a => activeUsers.Any(u => u.TelegramUserId == a.HolderUserId))
            .ToList();
            
        // Сохраняем последние обработанные SHA (важно для избежания дублирования)
        var recentShas = _processedShas.Keys.Take(_maxProcessedShas).ToList();
        
        // Временно сохраняем только активные данные
        var tempUserStats = _userStats.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        var tempAchievements = _achievements.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        var tempProcessedShas = _processedShas.Keys.ToHashSet();
        
        // Очищаем основные коллекции
        _userStats.Clear();
        _achievements.Clear();
        _processedShas.Clear();
        
        // Восстанавливаем только активные данные
        foreach (var user in activeUsers)
        {
            _userStats[user.TelegramUserId] = user;
        }
        
        foreach (var achievement in activeAchievements)
        {
            _achievements[achievement.Id] = achievement;
        }
        
        foreach (var sha in recentShas)
        {
            _processedShas.TryAdd(sha, 0);
        }
        
        // Сохраняем очищенные данные
        SaveUserStats();
        SaveAchievements();
        SaveProcessedShas();
        
        Console.WriteLine($"💾 Умное сохранение: {activeUsers.Count} активных пользователей, {activeAchievements.Count} ачивок, {recentShas.Count} SHA");
    }
    
    // Получение информации о кэше статистики
    public (int statsCount, int totalSize, Dictionary<string, int> byType) GetStatsCacheInfo()
    {
        var totalSize = _statsCache.Values.Sum(s => s.Data.Length);
        var byType = _statsCache.Values
            .GroupBy(s => s.Type)
            .ToDictionary(g => g.Key, g => g.Count());
            
        return (_statsCache.Count, totalSize, byType);
    }
    
    // Запланированное обновление статистики
    public bool ShouldUpdateScheduledStats()
    {
        var mskTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        var nowMsk = TimeZoneInfo.ConvertTime(DateTime.UtcNow, mskTimeZone);
        
        // Проверяем, прошло ли достаточно времени с последнего обновления
        if (_lastScheduledUpdate != DateTime.MinValue && 
            nowMsk - _lastScheduledUpdate < TimeSpan.FromHours(6))
        {
            return false;
        }
        
        // Проверяем, наступило ли время для обновления (точное время)
        var currentHour = nowMsk.Hour;
        var currentMinute = nowMsk.Minute;
        
        // Обновляем только в начале часа (0-5 минут)
        if (currentMinute > 5)
        {
            return false;
        }
        
        return _updateHours.Contains(currentHour);
    }
    
    public void MarkScheduledUpdate()
    {
        var mskTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        _lastScheduledUpdate = TimeZoneInfo.ConvertTime(DateTime.UtcNow, mskTimeZone);
    }

    public void SaveScheduledStats(string key, string data, string type, string parameters = "")
    {
        lock (_lockObject)
        {
            _scheduledStatsCache[key] = new ScheduledStats
            {
                Data = data,
                CreatedAt = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                Type = type,
                Parameters = parameters
            };
            
            // Сохраняем в JSON файл
            SaveScheduledStatsToFile();
        }
    }
    
    public string? GetScheduledStats(string key)
    {
        lock (_lockObject)
        {
            return _scheduledStatsCache.TryGetValue(key, out var stats) ? stats.Data : null;
        }
    }
    
    public void ClearOldScheduledStats()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-3); // Храним 3 дня
        var toRemove = _scheduledStatsCache
            .Where(kvp => kvp.Value.CreatedAt < cutoffDate)
            .Select(kvp => kvp.Key)
            .ToList();
            
        foreach (var key in toRemove)
        {
            _scheduledStatsCache.Remove(key);
        }
        
        if (toRemove.Count > 0)
        {
            Console.WriteLine($"🧹 Очищено {toRemove.Count} устаревших записей запланированной статистики");
            SaveScheduledStatsToFile();
        }
    }
    
    private void SaveScheduledStatsToFile()
    {
        try
        {
            var filePath = Path.Combine(_dataDir, "scheduled_stats.json");
            var json = JsonSerializer.Serialize(_scheduledStatsCache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Ошибка сохранения запланированной статистики: {ex.Message}");
        }
    }
    
    private void LoadScheduledStatsFromFile()
    {
        try
        {
            var filePath = Path.Combine(_dataDir, "scheduled_stats.json");
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, ScheduledStats>>(json);
                if (loaded != null)
                {
                    _scheduledStatsCache.Clear();
                    foreach (var kvp in loaded)
                    {
                        _scheduledStatsCache[kvp.Key] = kvp.Value;
                    }
                    Console.WriteLine($"📊 Загружено {_scheduledStatsCache.Count} записей запланированной статистики");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Ошибка загрузки запланированной статистики: {ex.Message}");
        }
    }
    
    // Защита от потери данных
    public void CreateBackup()
    {
        lock (_lockObject)
        {
            _backupStatsCache.Clear();
            foreach (var kvp in _scheduledStatsCache)
            {
                _backupStatsCache[kvp.Key] = new ScheduledStats
                {
                    Data = kvp.Value.Data,
                    CreatedAt = kvp.Value.CreatedAt,
                    LastUpdated = kvp.Value.LastUpdated,
                    Type = kvp.Value.Type,
                    Parameters = kvp.Value.Parameters
                };
            }
            Console.WriteLine($"💾 Создана резервная копия: {_backupStatsCache.Count} записей");
        }
    }
    
    public void RestoreFromBackup()
    {
        if (_backupStatsCache.Count == 0)
        {
            Console.WriteLine("⚠️ Резервная копия пуста, восстановление невозможно");
            return;
        }
        
        _scheduledStatsCache.Clear();
        foreach (var kvp in _backupStatsCache)
        {
            _scheduledStatsCache[kvp.Key] = new ScheduledStats
            {
                Data = kvp.Value.Data,
                CreatedAt = kvp.Value.CreatedAt,
                LastUpdated = kvp.Value.LastUpdated,
                Type = kvp.Value.Type,
                Parameters = kvp.Value.Parameters
            };
        }
        
        SaveScheduledStatsToFile();
        Console.WriteLine($"🔄 Восстановлено из резервной копии: {_scheduledStatsCache.Count} записей");
    }
    
    public bool IsBackupValid()
    {
        return _backupStatsCache.Count > 0 && 
               _backupStatsCache.Values.Any(v => !string.IsNullOrEmpty(v.Data));
    }
    
    public void ClearBackup()
    {
        var count = _backupStatsCache.Count;
        _backupStatsCache.Clear();
        Console.WriteLine($"🗑️ Очищена резервная копия: {count} записей");
    }
    
    // Безопасное сохранение с проверкой
    public bool SafeSaveScheduledStats(string key, string data, string type, string parameters = "")
    {
        try
        {
            // Проверяем, что данные не пустые
            if (string.IsNullOrWhiteSpace(data))
            {
                Console.WriteLine($"⚠️ Попытка сохранить пустые данные для {key}");
                return false;
            }
            
            lock (_lockObject)
            {
                // Сохраняем новые данные
                _scheduledStatsCache[key] = new ScheduledStats
                {
                    Data = data,
                    CreatedAt = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow,
                    Type = type,
                    Parameters = parameters
                };
                
                // Сохраняем в файл
                SaveScheduledStatsToFile();
            }
            
            Console.WriteLine($"✅ Безопасно сохранено: {key} ({type})");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Ошибка безопасного сохранения {key}: {ex.Message}");
            return false;
        }
    }
    
    // Проверка целостности данных
    public bool ValidateDataIntegrity()
    {
        try
        {
            var validCount = 0;
            var totalCount = _scheduledStatsCache.Count;
            
            foreach (var kvp in _scheduledStatsCache)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Value.Data) && 
                    !string.IsNullOrWhiteSpace(kvp.Value.Type))
                {
                    validCount++;
                }
            }
            
            var isValid = validCount == totalCount && totalCount > 0;
            Console.WriteLine($"🔍 Проверка целостности: {validCount}/{totalCount} записей валидны");
            
            return isValid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Ошибка проверки целостности: {ex.Message}");
            return false;
        }
    }
    
    public int GetMinApiCallsThreshold()
    {
        return _minApiCallsThreshold;
    }
    
    public int GetMaxApiCallsPerUpdate()
    {
        return _maxApiCallsPerUpdate;
    }
    
    public (int count, long sizeBytes, Dictionary<string, int> byType) GetScheduledStatsInfo()
    {
        var totalSize = _scheduledStatsCache.Values.Sum(s => s.Data.Length);
        var byType = _scheduledStatsCache.Values
            .GroupBy(s => s.Type)
            .ToDictionary(g => g.Key, g => g.Count());
            
        return (_scheduledStatsCache.Count, totalSize, byType);
    }
    
    public string GetAchievementStats()
    {
        var unlockedCount = _achievements.Values.Count(a => a.IsUnlocked);
        var totalCount = _achievementDefinitions.Count;
        var recentUnlocks = _achievements.Values
            .Where(a => a.IsUnlocked && a.UnlockedAt > DateTime.UtcNow.AddDays(-7))
            .Count();
            
        return $"🏆 Достижения: {unlockedCount}/{totalCount} разблокировано\n" +
               $"📈 За неделю: {recentUnlocks} новых";
    }
    
    public string GetStreaks()
    {
        var currentStreaks = _userStats.Values
            .Where(u => u.CurrentStreak > 0)
            .OrderByDescending(u => u.CurrentStreak)
            .Take(5)
            .ToList();
            
        if (!currentStreaks.Any())
            return "🔥 Нет активных стриков";
            
        var result = "🔥 Текущие стрики:\n";
        foreach (var user in currentStreaks)
        {
            result += $"• {user.DisplayName}: {user.CurrentStreak} дней\n";
        }
        
        return result.TrimEnd();
    }
    
    public string GetRating()
    {
        var topUsers = _userStats.Values
            .OrderByDescending(u => u.TotalCommits)
            .Take(5)
            .ToList();
            
        if (!topUsers.Any())
            return "📊 Нет данных для рейтинга";
            
        var result = "📊 Топ разработчиков:\n";
        for (int i = 0; i < topUsers.Count; i++)
        {
            var user = topUsers[i];
            result += $"{i + 1}. {user.DisplayName}: {user.TotalCommits} коммитов\n";
        }
        
        return result.TrimEnd();
    }
    
    public string GetLeaderboard()
    {
        var leaderboard = _userStats.Values
            .OrderByDescending(u => u.TotalCommits)
            .Take(10)
            .ToList();
            
        if (!leaderboard.Any())
            return "🏆 Нет данных для таблицы лидеров";
            
        var result = "🏆 Таблица лидеров:\n";
        for (int i = 0; i < leaderboard.Count; i++)
        {
            var user = leaderboard[i];
            var medal = i switch
            {
                0 => "🥇",
                1 => "🥈", 
                2 => "🥉",
                _ => $"{i + 1}."
            };
            result += $"{medal} {user.DisplayName}: {user.TotalCommits} коммитов\n";
        }
        
        return result.TrimEnd();
    }

    public int GetTotalCommits()
    {
        return _userStats.Values.Sum(u => u.TotalCommits);
    }

    public List<UserStats> GetLeaderboardUsers()
    {
        return _userStats.Values
            .OrderByDescending(u => u.TotalCommits)
            .Take(10)
            .ToList();
    }
}
