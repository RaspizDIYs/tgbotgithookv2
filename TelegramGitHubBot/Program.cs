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
#pragma warning disable CA1416 // Validate platform compatibility
                var gifTextEditorService = new GifTextEditorService(httpClient);
#pragma warning restore CA1416 // Validate platform compatibility
                var telegramService = new TelegramBotService(botClient, githubService, achievementService, geminiManager, messageStatsService, tenorService, gifTextEditorService);

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
#pragma warning disable CA1416 // Validate platform compatibility
builder.Services.AddHttpClient<GifTextEditorService>();
#pragma warning restore CA1416 // Validate platform compatibility

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

app.UseHttpsRedirection();

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

// Web App API endpoints
app.MapGet("/api/bot/status", (TelegramBotService telegramService) =>
{
    try
    {
        var stats = telegramService.GetBotStats();
        return Results.Ok(new
        {
            isActive = true,
            uptime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            version = "1.0.0",
            totalCommits = stats.TotalCommits,
            totalMessages = stats.TotalMessages,
            activeUsers = stats.ActiveUsers,
            aiRequests = stats.AiRequests
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error getting bot status: {ex.Message}");
    }
});

app.MapGet("/api/ai/status", (GeminiManager geminiManager) =>
{
    try
    {
        var status = geminiManager.GetCurrentAgentStatus();
        return Results.Ok(new
        {
            isActive = !status.Contains("неактивен") && !status.Contains("ошибка"),
            status = status
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error getting AI status: {ex.Message}");
    }
});

app.MapPost("/api/ai/start", async (HttpContext context, TelegramBotService telegramService) =>
{
    try
    {
        var chatId = long.Parse(context.Request.Query["chatId"].FirstOrDefault() ?? "0");
        if (chatId == 0)
        {
            return Results.BadRequest("chatId parameter is required");
        }
        
        await telegramService.HandleCommandAsync(chatId, "/glaistart");
        return Results.Ok(new { success = true, message = "AI started" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error starting AI: {ex.Message}");
    }
});

// Универсальный проброс текстовой команды бота из WebApp
app.MapPost("/api/bot/command", async (HttpContext context, TelegramBotService telegramService) =>
{
    try
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        var payload = System.Text.Json.JsonDocument.Parse(body);
        var command = payload.RootElement.GetProperty("command").GetString() ?? string.Empty;
        var chatIdStr = payload.RootElement.TryGetProperty("chatId", out var c) ? c.GetString() : null;
        if (string.IsNullOrWhiteSpace(command))
            return Results.BadRequest("command is required");
        long chatId = 0;
        if (!string.IsNullOrWhiteSpace(chatIdStr)) long.TryParse(chatIdStr, out chatId);
        if (chatId == 0)
        {
            var envChat = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
            if (!string.IsNullOrWhiteSpace(envChat) && long.TryParse(envChat, out var envChatId)) chatId = envChatId;
        }
        if (chatId == 0) return Results.BadRequest("chatId is required (or set TELEGRAM_CHAT_ID)");

        var result = await telegramService.HandleCommandForWebAppAsync(chatId, command);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error sending command: {ex.Message}");
    }
});

app.MapPost("/api/ai/stop", async (HttpContext context, TelegramBotService telegramService) =>
{
    try
    {
        var chatId = long.Parse(context.Request.Query["chatId"].FirstOrDefault() ?? "0");
        if (chatId == 0)
        {
            return Results.BadRequest("chatId parameter is required");
        }
        
        await telegramService.HandleCommandAsync(chatId, "/glaistop");
        return Results.Ok(new { success = true, message = "AI stopped" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error stopping AI: {ex.Message}");
    }
});

app.MapGet("/api/ai/stats", (GeminiManager geminiManager) =>
{
    try
    {
        var stats = geminiManager.GetAllAgentsStatus();
        return Results.Ok(stats);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error getting AI stats: {ex.Message}");
    }
});

app.MapPost("/api/ai/clear", async (HttpContext context, TelegramBotService telegramService) =>
{
    try
    {
        var chatId = long.Parse(context.Request.Query["chatId"].FirstOrDefault() ?? "0");
        if (chatId == 0)
        {
            return Results.BadRequest("chatId parameter is required");
        }
        
        await telegramService.HandleCommandAsync(chatId, "/glaiclear");
        return Results.Ok(new { success = true, message = "AI context cleared" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error clearing AI context: {ex.Message}");
    }
});

// AI Chat endpoint for WebApp
app.MapPost("/api/ai/chat", async (HttpContext context, GeminiManager geminiManager) =>
{
    try
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        var payload = System.Text.Json.JsonDocument.Parse(body);
        var text = payload.RootElement.GetProperty("text").GetString() ?? string.Empty;
        var chatIdStr = payload.RootElement.TryGetProperty("chatId", out var c) ? c.GetString() : null;
        
        if (string.IsNullOrWhiteSpace(text))
            return Results.BadRequest("text is required");
            
        long chatId = 0;
        if (!string.IsNullOrWhiteSpace(chatIdStr)) long.TryParse(chatIdStr, out chatId);
        if (chatId == 0)
        {
            var envChat = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
            if (!string.IsNullOrWhiteSpace(envChat) && long.TryParse(envChat, out var envChatId)) chatId = envChatId;
        }
        if (chatId == 0) return Results.BadRequest("chatId is required (or set TELEGRAM_CHAT_ID)");

        var reply = await geminiManager.GenerateResponseWithContextAsync(text, chatId);
        return Results.Ok(new { reply });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error generating AI response: {ex.Message}");
    }
});

app.MapGet("/api/git/stats", async (GitHubService githubService) =>
{
    try
    {
        var branches = await githubService.GetBranchesListAsync();
        var recentCommits = await githubService.GetRecentCommitsWithStatsAsync("main", 1);
        
        return Results.Ok(new
        {
            branches = branches.Count,
            commits = recentCommits.Count,
            lastCommit = recentCommits.FirstOrDefault()?.Date ?? DateTime.MinValue
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error getting git stats: {ex.Message}");
    }
});

app.MapGet("/api/git/commits", async (GitHubService githubService, int limit = 10) =>
{
    try
    {
        var commits = await githubService.GetRecentCommitsWithStatsAsync("main", limit);
        return Results.Ok(commits.Select(c => new
        {
            sha = c.Sha,
            message = c.Message,
            author = c.Author,
            email = c.Email,
            date = c.Date,
            additions = c.Additions,
            deletions = c.Deletions
        }));
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error getting commits: {ex.Message}");
    }
});

app.MapGet("/api/stats/leaderboard", (AchievementService achievementService) =>
{
    try
    {
        var leaderboard = achievementService.GetLeaderboardUsers();
        return Results.Ok(leaderboard.Select((user, index) => new
        {
            rank = index + 1,
            username = user.Username,
            commits = user.TotalCommits,
            maxLinesChanged = user.MaxLinesChanged,
            currentStreak = user.CurrentStreak,
            longestStreak = user.LongestStreak
        }));
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error getting leaderboard: {ex.Message}");
    }
});

// Web App main page
app.MapGet("/webapp", () => Results.Redirect("/webapp/index.html"));

app.Run();