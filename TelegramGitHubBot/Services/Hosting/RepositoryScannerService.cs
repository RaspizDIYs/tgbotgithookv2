namespace TelegramGitHubBot.Services.Hosting;

/// <summary>
/// Periodically scans all branches and feeds commits into <see cref="AchievementService"/>.
/// Runs a one-time full backfill on first start (guarded by a flag file under DATA_DIR).
/// Single instance — replaces the two duplicate scanners that previously fought over the
/// same JSON files from different service instances.
/// </summary>
public sealed class RepositoryScannerService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(20);

    private readonly GitHubService _gitHub;
    private readonly AchievementService _achievements;
    private readonly ILogger<RepositoryScannerService> _logger;
    private readonly string _backfillFlagPath;

    public RepositoryScannerService(
        GitHubService gitHub,
        AchievementService achievements,
        ILogger<RepositoryScannerService> logger)
    {
        _gitHub = gitHub;
        _achievements = achievements;
        _logger = logger;

        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR")?.Trim();
        if (string.IsNullOrWhiteSpace(dataDir))
            dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        try { Directory.CreateDirectory(dataDir); } catch { /* best effort */ }
        _backfillFlagPath = Path.Combine(dataDir, "backfill_state.json");
    }

    // Не запускаем фоновые проходы, если лимит GitHub почти исчерпан — иначе бот
    // добивает остаток и команды пользователя (/ask, /commits) падают на rate limit.
    private async Task<bool> HasRateBudgetAsync(int min = 500)
    {
        try
        {
            var (remaining, _, reset) = await _gitHub.GetRateLimitAsync();
            if (remaining < min)
            {
                _logger.LogWarning("⏳ GitHub rate limit low ({Remaining}); фоновой проход пропущен до {Reset:HH:mm} UTC", remaining, reset);
                return false;
            }
            return true;
        }
        catch
        {
            return true; // не смогли узнать лимит — не блокируем
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await BackfillOnceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await ScanPassAsync(stoppingToken);
            try { await Task.Delay(ScanInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task BackfillOnceAsync(CancellationToken ct)
    {
        if (File.Exists(_backfillFlagPath))
        {
            _logger.LogInformation("⏭️ Backfill skipped: flag exists");
            return;
        }

        if (!await HasRateBudgetAsync())
            return; // не выжигаем остаток лимита фоновым бэкфиллом — команды пользователя важнее

        try
        {
            _logger.LogInformation("🧭 Backfill: fetching branches & limited history (one-time)...");
            var branches = await _gitHub.GetBranchesListAsync();
            foreach (var branch in branches)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // includeStats:false — не тянем per-commit статистику (это отдельный
                    // API-вызов на КАЖДЫЙ коммит → выжигает rate limit при каждом рестарте,
                    // т.к. DATA_DIR на Render эфемерный и бэкфилл повторяется). Additions/
                    // Deletions по историческим коммитам всё равно приходят через webhook.
                    var history = await _gitHub.GetAllCommitsWithStatsForBranchAsync(branch, 300, includeStats: false);
                    foreach (var c in history)
                        _achievements.ProcessCommitIfNew(c.Sha, c.Author, c.Email, c.Message, c.Date, c.Additions, c.Deletions);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Backfill branch {Branch} error: {Message}", branch, ex.Message);
                }
            }

            File.WriteAllText(_backfillFlagPath, $"{{\"completed\":true,\"ts\":\"{DateTime.UtcNow:o}\"}}");
            _logger.LogInformation("✅ Backfill: completed");
        }
        catch (Exception ex)
        {
            _logger.LogError("Backfill error: {Message}", ex.Message);
        }
    }

    private async Task ScanPassAsync(CancellationToken ct)
    {
        if (!await HasRateBudgetAsync())
            return;

        try
        {
            var branches = await _gitHub.GetBranchesListAsync();
            foreach (var branch in branches)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var commits = await _gitHub.GetRecentCommitsWithStatsAsync(branch, 20, includeStats: false);
                    foreach (var c in commits)
                        _achievements.ProcessCommitIfNew(c.Sha, c.Author, c.Email, c.Message, c.Date, c.Additions, c.Deletions);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Scanner branch {Branch} error: {Message}", branch, ex.Message);
                }
            }
            _logger.LogInformation("✅ Scanner: pass completed");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError("Scanner error: {Message}", ex.Message);
        }
    }
}
