using TelegramGitHubBot.Models;
using System.Text.Json;
using Octokit;

namespace TelegramGitHubBot.Services;

public class AchievementService
{
    private readonly Dictionary<long, UserStats> _userStats = new();
    private readonly Dictionary<string, Achievement> _achievements = new();
    private readonly List<AchievementDefinition> _achievementDefinitions;
    private readonly string _dataDir;
    private readonly string _dataFilePath;
    private readonly string _processedShasFilePath;
    private readonly string? _persistOwner;
    private readonly string? _persistRepo;
    private readonly string _persistPath = "tgbot_stats.json";
    private readonly string _persistBranch = "main";
    private GitHubClient? _ghClient;
    private readonly HashSet<string> _processedShas = new();

    public AchievementService()
    {
        _achievementDefinitions = InitializeAchievementDefinitions();

        // Определяем директорию хранения (персистентный диск)
        _dataDir = Environment.GetEnvironmentVariable("DATA_DIR")?.Trim();
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
        LoadProcessedShas();
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
        _processedShas.Clear();
        SaveUserStats();
        SaveProcessedShas();
    }

    public void ProcessCommitIfNew(string sha, string author, string email, string commitMessage, DateTime commitDate, int linesAdded, int linesDeleted)
    {
        if (string.IsNullOrWhiteSpace(sha))
        {
            ProcessCommit(author, email, commitMessage, commitDate, linesAdded, linesDeleted);
            return;
        }
        if (_processedShas.Contains(sha)) return;
        ProcessCommit(author, email, commitMessage, commitDate, linesAdded, linesDeleted);
        _processedShas.Add(sha);
        SaveProcessedShas();
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
            var json = JsonSerializer.Serialize(_userStats, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_dataFilePath, json);
            SaveUserStatsToGitHub(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка сохранения статистики: {ex.Message}");
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
                    foreach (var sha in data) _processedShas.Add(sha);
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
            var json = JsonSerializer.Serialize(_processedShas, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_processedShasFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка сохранения обработанных коммитов: {ex.Message}");
        }
    }
}
