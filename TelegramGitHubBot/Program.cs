using Telegram.Bot;
using Octokit;
using TelegramGitHubBot.Services;

var builder = WebApplication.CreateBuilder(args);

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

    // Start polling in background (only if GitHub is also configured)
    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_PAT")))
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
                var telegramService = new TelegramBotService(botClient, githubService);

                int? lastUpdateId = null;

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
                                Console.WriteLine($"💬 Message from {update.Message.Chat.Id}: {update.Message.Text}");
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
        Console.WriteLine("⚠️  WARNING: GitHub PAT not configured - Telegram polling disabled");
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
builder.Services.AddSingleton<WebhookHandlerService>();

var app = builder.Build();

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
    await telegramService.HandleUpdateAsync(context);
});

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    version = "1.0.0",
    environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
    service = "TelegramGitHubBot"
}));

app.Run();
