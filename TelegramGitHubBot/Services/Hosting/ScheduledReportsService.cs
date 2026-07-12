using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace TelegramGitHubBot.Services.Hosting;

/// <summary>
/// Планировщик перенесённых из automation джоб: «зависшие задачи» (ежедневно в
/// STALE_HOUR) и «месячная сводка» (1-го числа в MONTHLY_HOUR). Дедуп по дате,
/// чтобы не слать дважды; состояние — best-effort в DATA_DIR (переживает рестарт
/// при персистентном диске). Время — МСК (UTC+3, без DST).
/// </summary>
public sealed class ScheduledReportsService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(20);

    private readonly TelegramBotService _bot;
    private readonly ITelegramBotClient _botClient;
    private readonly IConfiguration _cfg;
    private readonly ILogger<ScheduledReportsService> _logger;
    private readonly string _statePath;
    private readonly DateTime _startedAtMsk;

    private string _lastStaleDate = "";
    private string _lastMonthlyMonth = "";

    public ScheduledReportsService(TelegramBotService bot, ITelegramBotClient botClient,
        IConfiguration cfg, ILogger<ScheduledReportsService> logger)
    {
        _bot = bot;
        _botClient = botClient;
        _cfg = cfg;
        _logger = logger;

        var dir = Environment.GetEnvironmentVariable("DATA_DIR")?.Trim();
        if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine(AppContext.BaseDirectory, "data");
        try { Directory.CreateDirectory(dir); } catch { }
        _statePath = Path.Combine(dir, "reports_state.json");
        _startedAtMsk = DateTime.UtcNow.AddHours(3);
        LoadState();
    }

    /// <summary>
    /// DATA_DIR на Render эфемерный без persistent disk — reports_state.json не
    /// переживает рестарт (а рестартует бот при каждом деплое/мердже). Без этой
    /// защиты процесс, поднявшийся УЖЕ ПОСЛЕ часа отправки, считает "ещё не слал
    /// сегодня" и шлёт повторно — отсюда спам "Зависшие задачи" по несколько раз
    /// в день при частых деплоях. Разрешаем автослать только если процесс живёт с
    /// момента до часа X сегодня (или поднялся ещё вчера) — то есть застал реальный
    /// переход через порог, а не бывший рестарт-после-порога.
    /// </summary>
    private bool CrossedThresholdWhileRunning(DateTime now, int hour) =>
        _startedAtMsk.Date < now.Date || _startedAtMsk.Hour < hour;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(); }
            catch (Exception ex) { _logger.LogWarning("ScheduledReports tick error: {Message}", ex.Message); }

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync()
    {
        var now = DateTime.UtcNow.AddHours(3); // МСК
        var today = now.ToString("yyyy-MM-dd");
        var month = now.ToString("yyyy-MM");

        // Зависшие задачи — ежедневно в STALE_HOUR
        var staleHour = Int("STALE_HOUR", 10);
        if (Bool("STALE_ENABLED", true) && now.Hour >= staleHour && _lastStaleDate != today)
        {
            if (!CrossedThresholdWhileRunning(now, staleHour))
            {
                _logger.LogInformation(
                    "stale: пропуск автослания — процесс поднялся сегодня в {StartedHour}ч, порог {Hour}ч уже прошёл " +
                    "(вероятно рестарт от деплоя, а не первый проход через час X)", _startedAtMsk.Hour, staleHour);
            }
            else
            {
                var chat = ChatId("STALE_CHAT_ID");
                if (chat != 0)
                {
                    await SendAsync(chat, ThreadId("STALE_THREAD_ID"), await _bot.BuildStaleTasksAsync(), "stale");
                }
            }
            _lastStaleDate = today; // помечаем в обоих случаях, чтобы не долбить билд каждый тик
            SaveState();
        }

        // Месячная сводка — 1-го числа в MONTHLY_HOUR
        var monthlyHour = Int("MONTHLY_HOUR", 10);
        if (Bool("MONTHLY_ENABLED", true) && now.Day == 1 && now.Hour >= monthlyHour && _lastMonthlyMonth != month)
        {
            if (!CrossedThresholdWhileRunning(now, monthlyHour))
            {
                _logger.LogInformation(
                    "monthly: пропуск автослания — процесс поднялся сегодня в {StartedHour}ч, порог {Hour}ч уже прошёл",
                    _startedAtMsk.Hour, monthlyHour);
            }
            else
            {
                var chat = ChatId("MONTHLY_CHAT_ID");
                if (chat != 0)
                {
                    await SendAsync(chat, ThreadId("MONTHLY_THREAD_ID"), await _bot.BuildMonthlySummaryAsync(), "monthly");
                }
            }
            _lastMonthlyMonth = month;
            SaveState();
        }
    }

    private async Task SendAsync(long chatId, int? threadId, string text, string what)
    {
        try
        {
            await _botClient.SendTextMessageAsync(chatId, text, parseMode: ParseMode.Markdown, messageThreadId: threadId);
            _logger.LogInformation("✅ scheduled {What} sent to {Chat}", what, chatId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("scheduled {What} send failed: {Message}", what, ex.Message);
        }
    }

    // STALE_CHAT_ID / MONTHLY_CHAT_ID, с фолбэком на общий PUSH_SUMMARY_CHAT_ID
    private long ChatId(string key)
    {
        var raw = _cfg[key];
        if (string.IsNullOrWhiteSpace(raw)) raw = _cfg["PUSH_SUMMARY_CHAT_ID"];
        return long.TryParse(raw, out var id) ? id : 0;
    }

    private int? ThreadId(string key) => int.TryParse(_cfg[key], out var t) ? t : null;
    private bool Bool(string key, bool def) => (_cfg[key] ?? def.ToString()).Equals("true", StringComparison.OrdinalIgnoreCase);
    private int Int(string key, int def) => int.TryParse(_cfg[key], out var v) ? v : def;

    private void LoadState()
    {
        try
        {
            if (!File.Exists(_statePath)) return;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_statePath));
            var r = doc.RootElement;
            if (r.TryGetProperty("stale", out var s)) _lastStaleDate = s.GetString() ?? "";
            if (r.TryGetProperty("monthly", out var m)) _lastMonthlyMonth = m.GetString() ?? "";
        }
        catch { }
    }

    private void SaveState()
    {
        try
        {
            File.WriteAllText(_statePath,
                $"{{\"stale\":\"{_lastStaleDate}\",\"monthly\":\"{_lastMonthlyMonth}\"}}");
        }
        catch { }
    }
}
