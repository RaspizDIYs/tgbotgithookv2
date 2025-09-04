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
var telegramToken = builder.Configuration["Telegram:BotToken"] ??
                   Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
if (!string.IsNullOrEmpty(telegramToken))
{
    Console.WriteLine($"✅ Telegram Bot Token configured (length: {telegramToken.Length})");
    builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(telegramToken));
    builder.Services.AddSingleton<TelegramBotService>();
}
else
{
    Console.WriteLine("❌ Warning: Telegram Bot Token not configured. Telegram features will be disabled.");
    Console.WriteLine("   Make sure TELEGRAM_BOT_TOKEN environment variable is set.");
}

// Configure GitHub Client
var githubToken = builder.Configuration["GitHub:PersonalAccessToken"] ??
                 Environment.GetEnvironmentVariable("GITHUB_PAT");
if (!string.IsNullOrEmpty(githubToken))
{
    Console.WriteLine($"✅ GitHub Personal Access Token configured (length: {githubToken.Length})");
    var githubClient = new GitHubClient(new ProductHeaderValue("TelegramGitHubBot"));
    githubClient.Credentials = new Credentials(githubToken);
    builder.Services.AddSingleton<GitHubClient>(githubClient);
    builder.Services.AddSingleton<GitHubService>();
}
else
{
    Console.WriteLine("❌ Warning: GitHub Personal Access Token not configured. GitHub features will be disabled.");
    Console.WriteLine("   Make sure GITHUB_PAT environment variable is set.");
}

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
