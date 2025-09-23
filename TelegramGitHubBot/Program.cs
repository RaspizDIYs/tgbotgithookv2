using System.Net.Http;
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

// Debug: Print available environment variables
Console.WriteLine("=== Environment Variables Debug ===");
var envVars = Environment.GetEnvironmentVariables();
foreach (string key in envVars.Keys)
{
    if (key.Contains("TELEGRAM") || key.Contains("GITHUB") || key.Contains("ASPNETCORE"))
    {
        Console.WriteLine($"{key} = {envVars[key]}");
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
    builder.Services.AddSingleton<TelegramBotService>();

    // Start polling in background only when explicitly enabled
    var enablePolling = (Environment.GetEnvironmentVariable("TELEGRAM_ENABLE_POLLING") ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase);
    if (enablePolling)
    {
        Task.Run(async () =>
        {
            try
            {
                Console.WriteLine("🔄 Starting Telegram bot polling...");
                // Create GitHub service for the polling task
                var githubClient = new GitHubClient(new ProductHeaderValue("TelegramGitHubBot"));
                var githubPat = Environment.GetEnvironmentVariable("GITHUB_PAT");
                if (!string.IsNullOrWhiteSpace(githubPat))
                {
                    githubClient.Credentials = new Credentials(githubPat.Trim());
                }
                var githubService = new GitHubService(githubClient);
                var achievementService = new AchievementService();
                var telegramService = new TelegramBotService(botClient, githubService, achievementService);

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
builder.Services.AddSingleton<WebhookHandlerService>();

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
                Console.WriteLine("🧭 Backfill: fetching all branches and full history (one-time)...");
                var branches = await ghService.GetBranchesListAsync();
                foreach (var br in branches)
                {
                    try
                    {
                        var history = await ghService.GetAllCommitsWithStatsForBranchAsync(br, 1000);
                        foreach (var c in history)
                        {
                            achService.ProcessCommit(c.Author, c.Email, c.Message, c.Date, c.Additions, c.Deletions);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Backfill branch {br} error: {ex.Message}");
                    }
                }
                Console.WriteLine("✅ Backfill: completed");
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
                            var commits = await ghService.GetRecentCommitsWithStatsAsync(branch, 20);
                            foreach (var c in commits)
                            {
                                achService.ProcessCommit(c.Author, c.Email, c.Message, c.Date, c.Additions, c.Deletions);
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
                await Task.Delay(TimeSpan.FromMinutes(5));
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

app.UseHttpsRedirection();

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
