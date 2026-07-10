using System.Net.Http;
using System.Runtime.Versioning;
using Telegram.Bot;
using Octokit;
using TelegramGitHubBot.Services;

var builder = WebApplication.CreateBuilder(args);

// Render.com: привязка к PORT, если задан
var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(renderPort) && int.TryParse(renderPort, out var port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
    Console.WriteLine($"🌐 Render PORT detected: binding Kestrel to http://0.0.0.0:{port}");
}

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();

// Debug: Print available environment variables
Console.WriteLine("=== Environment Variables Debug ===");
var envVars = Environment.GetEnvironmentVariables();
foreach (string key in envVars.Keys)
{
    if (key.Contains("TELEGRAM") || key.Contains("GITHUB") || key.Contains("ASPNETCORE") || key.Contains("GEMINI") || key.Contains("TENOR"))
    {
        var value = envVars[key]?.ToString() ?? "NULL";
        var displayValue = key.Contains("API_KEY") ? 
            $"{value.Substring(0, Math.Min(20, value.Length))}..." : 
            value;
        Console.WriteLine($"{key} = {displayValue}");
    }
}
Console.WriteLine("===================================");

// Configure Telegram Bot
Console.WriteLine("🔍 Reading Telegram Bot Token...");
var telegramToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");

Console.WriteLine($"   TELEGRAM_BOT_TOKEN: '{telegramToken?.Substring(0, Math.Min(20, telegramToken?.Length ?? 0)) ?? "NULL"}...' (length: {telegramToken?.Length ?? 0})");

if (!string.IsNullOrWhiteSpace(telegramToken))
{
    Console.WriteLine($"✅ Telegram Bot Token configured (length: {telegramToken.Length})");
    var botClient = new TelegramBotClient(telegramToken.Trim());
    builder.Services.AddSingleton<ITelegramBotClient>(botClient);

    // Регистрируем меню команд в Telegram (только актуальный набор)
    _ = Task.Run(async () =>
    {
        try
        {
            await botClient.SetMyCommandsAsync(new[]
            {
                new Telegram.Bot.Types.BotCommand { Command = "start", Description = "Главное меню" },
                new Telegram.Bot.Types.BotCommand { Command = "help", Description = "Справка по командам" },
                new Telegram.Bot.Types.BotCommand { Command = "info", Description = "Подробная информация" },
                new Telegram.Bot.Types.BotCommand { Command = "status", Description = "Статус репозитория" },
                new Telegram.Bot.Types.BotCommand { Command = "commits", Description = "Коммиты [ветка] [кол-во]" },
                new Telegram.Bot.Types.BotCommand { Command = "branches", Description = "Список веток" },
                new Telegram.Bot.Types.BotCommand { Command = "prs", Description = "Открытые Pull Request" },
                new Telegram.Bot.Types.BotCommand { Command = "ci", Description = "CI/CD статус [ветка]" },
                new Telegram.Bot.Types.BotCommand { Command = "deploy", Description = "Деплой [среда]" },
                new Telegram.Bot.Types.BotCommand { Command = "search", Description = "Поиск по коммитам" },
                new Telegram.Bot.Types.BotCommand { Command = "authors", Description = "Активные авторы" },
                new Telegram.Bot.Types.BotCommand { Command = "files", Description = "Файлы в коммите <sha>" },
                new Telegram.Bot.Types.BotCommand { Command = "ratelimit", Description = "Лимиты GitHub API" },
                new Telegram.Bot.Types.BotCommand { Command = "cache", Description = "Информация о кэше" },
                new Telegram.Bot.Types.BotCommand { Command = "protection", Description = "Защита веток" },
                new Telegram.Bot.Types.BotCommand { Command = "backup", Description = "Резервное копирование" },
                new Telegram.Bot.Types.BotCommand { Command = "laststats", Description = "Последняя статистика" },
                new Telegram.Bot.Types.BotCommand { Command = "weekstats", Description = "Статистика по неделям" },
                new Telegram.Bot.Types.BotCommand { Command = "rating", Description = "Рейтинг разработчиков" },
                new Telegram.Bot.Types.BotCommand { Command = "trends", Description = "Тренды активности" },
                new Telegram.Bot.Types.BotCommand { Command = "achievements", Description = "Список ачивок" },
                new Telegram.Bot.Types.BotCommand { Command = "leaderboard", Description = "Таблица лидеров" },
                new Telegram.Bot.Types.BotCommand { Command = "streaks", Description = "Топ стриков" },
                new Telegram.Bot.Types.BotCommand { Command = "recalc", Description = "Пересчёт статистики" },
                new Telegram.Bot.Types.BotCommand { Command = "glaistart", Description = "Включить режим AI" },
                new Telegram.Bot.Types.BotCommand { Command = "glaistop", Description = "Выключить режим AI" },
                new Telegram.Bot.Types.BotCommand { Command = "glaistats", Description = "Статус всех AI-агентов" },
                new Telegram.Bot.Types.BotCommand { Command = "glaicurrent", Description = "Текущий AI-агент" },
                new Telegram.Bot.Types.BotCommand { Command = "glaiswitch", Description = "Переключить AI-агента" },
                new Telegram.Bot.Types.BotCommand { Command = "glaiclear", Description = "Очистить контекст AI" },
                new Telegram.Bot.Types.BotCommand { Command = "ask", Description = "Разовый вопрос к AI" },
                new Telegram.Bot.Types.BotCommand { Command = "tldr", Description = "Краткая выжимка обсуждения" },
                new Telegram.Bot.Types.BotCommand { Command = "settings", Description = "Настройки уведомлений" },
            });
            Console.WriteLine("✅ Telegram command menu registered");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Failed to register command menu: {ex.Message}");
        }
    });
    builder.Services.AddSingleton<TelegramBotService>();
    builder.Services.AddHttpClient<GeminiManager>();
    builder.Services.AddSingleton<GeminiManager>();

    // Start polling in background only when explicitly enabled
    // По умолчанию включаем polling, если переменная не задана
    var enablePolling = (Environment.GetEnvironmentVariable("TELEGRAM_ENABLE_POLLING") ?? "true").Equals("true", StringComparison.OrdinalIgnoreCase);
    if (enablePolling)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                Console.WriteLine("🔄 Starting Telegram bot polling...");
                // На всякий случай снимаем вебхук, чтобы избежать 409/Conflict
                try
                {
                    await botClient.DeleteWebhookAsync(true);
                    Console.WriteLine("🧹 Telegram webhook deleted before polling start");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Failed to delete webhook: {ex.Message}");
                }
                // Create GitHub service for the polling task
                var githubClient = new GitHubClient(new ProductHeaderValue("TelegramGitHubBot"));
                var githubPat = Environment.GetEnvironmentVariable("GITHUB_PAT");
                if (!string.IsNullOrWhiteSpace(githubPat))
                {
                    githubClient.Credentials = new Credentials(githubPat.Trim());
                }
                var githubService = new GitHubService(githubClient);
                var achievementService = new AchievementService();
                var messageStatsService = new MessageStatsService();
                var httpClient = new HttpClient();
                var geminiManager = new GeminiManager(httpClient, builder.Configuration);
                var tenorService = new TenorService(httpClient, builder.Configuration);
                var telegramService = new TelegramBotService(botClient, githubService, achievementService, geminiManager, messageStatsService, tenorService);

                int? lastUpdateId = null;

                // Self-ping task to keep instance alive
                var selfPingTask = Task.Run(async () =>
                {
                    using var httpClient = new HttpClient();
                    var baseUrl = Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL") ??
                                 Environment.GetEnvironmentVariable("ASPNETCORE_URLS")?.Split(';').FirstOrDefault() ??
                                 "http://localhost:5000";

                    // Если URL содержит '+', подменим на localhost (валидный hostname внутри контейнера)
                    if (baseUrl.Contains("+"))
                    {
                        baseUrl = baseUrl.Replace("http://+", "http://localhost");
                        baseUrl = baseUrl.Replace("https://+", "https://localhost");
                    }

                    while (true)
                    {
                        try
                        {
                            var pingUrl = $"{baseUrl.TrimEnd('/')}/ping";
                            Console.WriteLine($"🏓 Self-ping: {pingUrl}");

                            var response = await httpClient.GetAsync(pingUrl);
                            if (response.IsSuccessStatusCode)
                            {
                                Console.WriteLine("✅ Self-ping successful");
                            }
                            else
                            {
                                Console.WriteLine($"⚠️ Self-ping failed: {response.StatusCode}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ Self-ping error: {ex.Message}");
                        }

                        await Task.Delay(30 * 1000); // Ping every 30 seconds
                    }
                });

                // Фоновый сканер коммитов по всем веткам каждые 5 минут
                var scannerCts = new CancellationTokenSource();
                var scannerTask = Task.Run(async () =>
                {
                    while (!scannerCts.IsCancellationRequested)
                    {
                        try
                        {
                            Console.WriteLine("🧭 Scanner: fetching branches...");
                            var branches = await githubService.GetBranchesListAsync();
                            foreach (var branch in branches)
                            {
                                try
                                {
                                    var commits = await githubService.GetRecentCommitsWithStatsAsync(branch, 20);
                                    foreach (var c in commits)
                                    {
                                        achievementService.ProcessCommit(c.Author, c.Email, c.Message, c.Date, c.Additions, c.Deletions);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Scanner branch {branch} error: {ex.Message}");
                                }
                            }
                            Console.WriteLine("✅ Scanner: pass completed");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Scanner error: {ex.Message}");
                        }
                        await Task.Delay(TimeSpan.FromMinutes(5), scannerCts.Token);
                    }
                }, scannerCts.Token);

                while (true)
                {
                    try
                    {
                        Console.WriteLine($"🔍 Polling for updates... (lastUpdateId: {lastUpdateId})");
                        var updates = await botClient.GetUpdatesAsync(
                            offset: lastUpdateId,
                            timeout: 30);

                        Console.WriteLine($"📦 Received {updates.Length} updates");

                        foreach (var update in updates)
                        {
                            Console.WriteLine($"📨 Processing update {update.Id}: Type={update.Type}");

                            if (update.Message != null)
                            {
                                // Не логируем содержимое сообщений пользователей по соображениям приватности
                                await telegramService.HandleMessageAsync(update.Message);
                            }
                            else if (update.CallbackQuery != null)
                            {
                                Console.WriteLine($"🔘 Callback query from {update.CallbackQuery.From.Id}: {update.CallbackQuery.Data}");
                                await telegramService.HandleCallbackQueryAsync(update.CallbackQuery);
                            }

                            lastUpdateId = update.Id + 1;
                        }

                        if (updates.Length == 0)
                        {
                            Console.WriteLine("⏳ No new updates, waiting...");
                        }
                    }
                    catch (Telegram.Bot.Exceptions.ApiRequestException apiEx) when (apiEx.ErrorCode == 409)
                    {
                        Console.WriteLine($"❌ Polling conflict 409: {apiEx.Message}. Will retry in 10s");
                        await Task.Delay(10_000);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Polling error: {ex.Message}");
                        await Task.Delay(5000); // Wait 5 seconds before retry
                    }

                    await Task.Delay(1000); // Poll every second
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start polling: {ex.Message}");
            }
        });
    }
    else
    {
        Console.WriteLine("⚠️  Polling disabled (set TELEGRAM_ENABLE_POLLING=true to enable)");
    }
}
else
{
    Console.WriteLine("❌ ERROR: TELEGRAM_BOT_TOKEN environment variable is not set or empty!");
    Console.WriteLine("   Please check your Render environment variables.");
}

// Configure GitHub Client
Console.WriteLine("🔍 Reading GitHub Personal Access Token...");
var githubToken = Environment.GetEnvironmentVariable("GITHUB_PAT");

Console.WriteLine($"   GITHUB_PAT: '{githubToken?.Substring(0, Math.Min(25, githubToken?.Length ?? 0)) ?? "NULL"}...' (length: {githubToken?.Length ?? 0})");

       if (!string.IsNullOrWhiteSpace(githubToken))
       {
           Console.WriteLine($"✅ GitHub Personal Access Token configured (length: {githubToken.Length})");
           var githubClient = new GitHubClient(new ProductHeaderValue("TelegramGitHubBot"));
           githubClient.Credentials = new Credentials(githubToken.Trim());
           builder.Services.AddSingleton<GitHubClient>(githubClient);
           builder.Services.AddSingleton<GitHubService>();
           builder.Services.AddSingleton<MessageStatsService>();
           builder.Services.AddSingleton<TelegramBotService>();
       }
else
{
    Console.WriteLine("❌ ERROR: GITHUB_PAT environment variable is not set or empty!");
    Console.WriteLine("   Please check your Render environment variables.");
}

// Configure Chat ID for webhooks
Console.WriteLine("🔍 Reading Telegram Chat ID...");
var telegramChatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");

Console.WriteLine($"   TELEGRAM_CHAT_ID: '{telegramChatId ?? "NULL"}'");

if (!string.IsNullOrWhiteSpace(telegramChatId))
{
    Console.WriteLine($"✅ Telegram Chat ID configured: {telegramChatId}");
}
else
{
    Console.WriteLine("⚠️  WARNING: TELEGRAM_CHAT_ID not set - webhooks will not work!");
    Console.WriteLine("   Please add TELEGRAM_CHAT_ID to your Render environment variables.");
}

// Register services
builder.Services.AddSingleton<AchievementService>();
builder.Services.AddSingleton<MessageStatsService>();
builder.Services.AddSingleton<WebhookHandlerService>();
builder.Services.AddHttpClient<TenorService>();

var app = builder.Build();

// Запускаем фоновый сканер репозитория сразу при старте (независимо от polling/webhook)
try
{
    var scopedProvider = app.Services;
    var ghService = scopedProvider.GetService<GitHubService>();
    var achService = scopedProvider.GetService<AchievementService>();

    if (ghService != null && achService != null)
    {
        Console.WriteLine("🧭 Startup scanner: initializing background scan task...");
        _ = Task.Run(async () =>
        {
            // One-time full backfill on startup
            try
            {
                var flagPath = Path.Combine(AppContext.BaseDirectory, "backfill_state.json");
                if (!File.Exists(flagPath))
                {
                    Console.WriteLine("🧭 Backfill: fetching branches & limited history (one-time)...");
                    var branches = await ghService.GetBranchesListAsync();
                    foreach (var br in branches)
                    {
                        try
                        {
                            var history = await ghService.GetAllCommitsWithStatsForBranchAsync(br, 300, includeStats: true);
                            foreach (var c in history)
                            {
                                achService.ProcessCommitIfNew(c.Sha, c.Author, c.Email, c.Message, c.Date, c.Additions, c.Deletions);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Backfill branch {br} error: {ex.Message}");
                        }
                    }
                    File.WriteAllText(flagPath, "{\"completed\":true,\"ts\":\"" + DateTime.UtcNow.ToString("o") + "\"}");
                    Console.WriteLine("✅ Backfill: completed");
                }
                else
                {
                    Console.WriteLine("⏭️ Backfill skipped: flag exists");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Backfill error: {ex.Message}");
            }

            while (true)
            {
                try
                {
                    Console.WriteLine("🧭 Startup scanner pass: fetching branches...");
                    var branches = await ghService.GetBranchesListAsync();
                    foreach (var branch in branches)
                    {
                        try
                        {
                            var commits = await ghService.GetRecentCommitsWithStatsAsync(branch, 20, includeStats: false);
                            foreach (var c in commits)
                            {
                                achService.ProcessCommitIfNew(c.Sha, c.Author, c.Email, c.Message, c.Date, c.Additions, c.Deletions);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Startup scanner branch {branch} error: {ex.Message}");
                        }
                    }
                    Console.WriteLine("✅ Startup scanner: pass completed");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Startup scanner error: {ex.Message}");
                }
                await Task.Delay(TimeSpan.FromMinutes(20));
            }
        });
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to start startup scanner: {ex.Message}");
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Отключаем HTTPS редирект для Render.com (он сам обрабатывает HTTPS)
var isRender = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PORT"));
if (!isRender)
{
    app.UseHttpsRedirection();
}

// CORS: читаем ALLOWED_ORIGINS (comma-separated). Если не задано — разрешаем все (временно)
var allowedOrigins = (Environment.GetEnvironmentVariable("ALLOWED_ORIGINS") ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

if (allowedOrigins.Length > 0)
{
    Console.WriteLine($"CORS: restricting to origins: {string.Join("; ", allowedOrigins)}");
    app.UseCors(corsBuilder => corsBuilder
        .WithOrigins(allowedOrigins)
        .AllowAnyMethod()
        .AllowAnyHeader());
}
else
{
    Console.WriteLine("CORS: ALLOWED_ORIGINS not set — allowing any origin (temporary)");
    app.UseCors(corsBuilder => corsBuilder
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader());
}

// Webhook endpoint for GitHub
app.MapPost("/webhook/github", async (HttpContext context, WebhookHandlerService webhookHandler) =>
{
    await webhookHandler.HandleGitHubWebhookAsync(context);
});

// Telegram webhook endpoint
app.MapPost("/webhook/telegram/{token}", async (string token, HttpContext context, TelegramBotService telegramService) =>
{
    var configuredToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")?.Trim();
    if (string.IsNullOrWhiteSpace(configuredToken) || !string.Equals(token, configuredToken, StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.CompleteAsync();
        return;
    }

    await telegramService.HandleUpdateAsync(context);
});

// Static for webapp теперь отдаётся из GitHub Pages, локальную директорию больше не публикуем

// Health check endpoint
// Self-ping endpoint to keep instance alive
app.MapGet("/ping", () => Results.Ok(new
{
    status = "pong",
    timestamp = DateTime.Now,
    uptime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    version = "1.0.0",
    environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
    service = "TelegramGitHubBot"
}));


app.Run();