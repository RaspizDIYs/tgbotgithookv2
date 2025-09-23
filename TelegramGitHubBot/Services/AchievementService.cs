using TelegramGitHubBot.Models;
using System.Text.Json;

namespace TelegramGitHubBot.Services;

public class AchievementService
{
    private readonly Dictionary<long, UserStats> _userStats = new();
    private readonly Dictionary<string, Achievement> _achievements = new();
    private readonly List<AchievementDefinition> _achievementDefinitions;
    private readonly string _dataFilePath = "user_stats.json";

    public AchievementService()
    {
        _achievementDefinitions = InitializeAchievementDefinitions();
        LoadUserStats();
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
                GifUrl = "https://tenor.com/ru/view/joaquin-phoenix-commodus-gladiator-gif-12708896",
                Emoji = "👑",
                Type = AchievementType.TotalCommits,
                IsRecordBased = true
            },
            new()
            {
                Id = "speedster",
                Name = "Speedster",
                Description = "Скоростной кодер - максимальное количество изменений строк в одном коммите",
                GifUrl = "https://tenor.com/ru/view/coding-typing-pc-laptop-power-gif-21599707",
                Emoji = "⚡",
                Type = AchievementType.MaxLinesChanged,
                IsRecordBased = true
            },
            new()
            {
                Id = "motivating_leader",
                Name = "Motivating Leader",
                Description = "Мотивирующий лидер - самое долгое время без коммитов",
                GifUrl = "https://tenor.com/ru/view/just-do-it-shia-la-beouf-do-it-flame-fire-gif-5621394",
                Emoji = "🔥",
                Type = AchievementType.LongestBreak,
                IsRecordBased = true
            },
            new()
            {
                Id = "weekend_warrior",
                Name = "Weekend Warrior",
                Description = "Выходной воин - коммиты в выходные",
                GifUrl = "https://tenor.com/ru/view/coding-typing-pc-laptop-power-gif-21599707",
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
                GifUrl = "https://tenor.com/ru/view/coding-typing-pc-laptop-power-gif-21599707",
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
                GifUrl = "https://tenor.com/ru/view/coding-typing-pc-laptop-power-gif-21599707",
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
                GifUrl = "https://tenor.com/ru/view/joaquin-phoenix-commodus-gladiator-gif-12708896",
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
                GifUrl = "https://tenor.com/ru/view/coding-typing-pc-laptop-power-gif-21599707",
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
                GifUrl = "https://tenor.com/ru/view/just-do-it-shia-la-beouf-do-it-flame-fire-gif-5621394",
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
                GifUrl = "https://tenor.com/ru/view/just-do-it-shia-la-beouf-do-it-flame-fire-gif-5621394",
                Emoji = "🔥",
                Type = AchievementType.StreakMaster,
                IsRecordBased = true
            },
            new()
            {
                Id = "test_champion",
                Name = "Test Champion",
                Description = "Чемпион тестов - коммиты с тестами",
                GifUrl = "https://tenor.com/ru/view/coding-typing-pc-laptop-power-gif-21599707",
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
                GifUrl = "https://tenor.com/ru/view/joaquin-phoenix-commodus-gladiator-gif-12708896",
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
                GifUrl = "https://tenor.com/ru/view/druid-of-the-talon-druid-storm-crow-flight-form-night-elf-gif-13611705726058255097",
                Emoji = "🌿",
                Type = AchievementType.BranchCreator,
                IsRecordBased = true
            }
        };
    }

    public void ProcessCommit(string author, string email, string commitMessage, DateTime commitDate, int linesAdded, int linesDeleted)
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
        
        SaveUserStats();
    }

    public void RegisterBranchCreated(string author, string email, DateTime createdAt)
    {
        var userId = GetOrCreateUserId(author, email);
        var stats = GetOrCreateUserStats(userId, author);

        stats.BranchesCreated++;
        stats.LastUpdated = DateTime.UtcNow;

        CheckAchievements(stats);
        SaveUserStats();
    }

    public void ResetAllData()
    {
        _userStats.Clear();
        _achievements.Clear();
        SaveUserStats();
    }

    private long GetOrCreateUserId(string author, string email)
    {
        // Простая хеш-функция для создания ID из email
        return Math.Abs(email.GetHashCode());
    }

    private UserStats GetOrCreateUserStats(long userId, string author)
    {
        if (!_userStats.TryGetValue(userId, out var stats))
        {
            stats = new UserStats
            {
                TelegramUserId = userId,
                Username = author,
                DisplayName = author
            };
            _userStats[userId] = stats;
        }
        return stats;
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

    private void LoadUserStats()
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
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка загрузки статистики: {ex.Message}");
        }
    }

    private void SaveUserStats()
    {
        try
        {
            var json = JsonSerializer.Serialize(_userStats, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_dataFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка сохранения статистики: {ex.Message}");
        }
    }
}
