namespace TelegramGitHubBot.Services;

/// <summary>Helpers for git SHA formatting that never throw on short/empty input.</summary>
public static class ShaUtils
{
    /// <summary>Returns the first 8 chars of a SHA, or the whole string if shorter.</summary>
    public static string Short(string? sha)
        => string.IsNullOrEmpty(sha) ? string.Empty : (sha.Length >= 8 ? sha[..8] : sha);
}
