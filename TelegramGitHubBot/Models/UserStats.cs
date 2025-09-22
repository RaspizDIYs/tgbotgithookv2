namespace TelegramGitHubBot.Models;

public class UserStats
{
    public long TelegramUserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int TotalCommits { get; set; }
    public int MaxLinesChanged { get; set; }
    public DateTime? LastCommitDate { get; set; }
    public DateTime? FirstCommitDate { get; set; }
    public int LongestBreakDays { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public int TestCommits { get; set; }
    public int ReleaseCommits { get; set; }
    public int WeekendCommits { get; set; }
    public int NightCommits { get; set; }
    public int EarlyCommits { get; set; }
    public int BugFixCommits { get; set; }
    public int FeatureCommits { get; set; }
    public int RefactorCommits { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class Achievement
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string GifUrl { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public bool IsUnlocked { get; set; }
    public DateTime? UnlockedAt { get; set; }
    public long? HolderUserId { get; set; }
    public string? HolderName { get; set; }
    public int? Value { get; set; } // Значение рекорда (коммиты, строки, дни)
}

public class AchievementDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string GifUrl { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public AchievementType Type { get; set; }
    public int RequiredValue { get; set; }
    public bool IsRecordBased { get; set; } // true для рекордов (только лучший), false для прогресса
}

public enum AchievementType
{
    TotalCommits,
    MaxLinesChanged,
    LongestBreak,
    ConsecutiveDays,
    WeekendWarrior,
    NightOwl,
    EarlyBird,
    BugHunter,
    FeatureMaster,
    RefactorKing,
    StreakMaster,
    TestChampion,
    ReleaseGenius
}
