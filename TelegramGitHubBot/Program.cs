using Telegram.Bot;
using Octokit;
using TelegramGitHubBot.Services;
using TelegramGitHubBot.Services.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Render.com: привязка к PORT, если задан
var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(renderPort) && int.TryParse(renderPort, out var port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
    Console.WriteLine($"🌐 Render PORT detected: binding Kestrel to http://0.0.0.0:{port}");
}

// ── Framework services ───────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();
builder.Services.AddHttpClient();

// ── Configuration ────────────────────────────────────────────────────────────
var telegramToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")?.Trim();
var githubPat = Environment.GetEnvironmentVariable("GITHUB_PAT")?.Trim();
var pollingEnabled = (Environment.GetEnvironmentVariable("TELEGRAM_ENABLE_POLLING") ?? "true")
    .Equals("true", StringComparison.OrdinalIgnoreCase);
var selfPingEnabled = (Environment.GetEnvironmentVariable("SELF_PING_ENABLED") ?? "false")
    .Equals("true", StringComparison.OrdinalIgnoreCase);

var hasTelegram = !string.IsNullOrWhiteSpace(telegramToken);
Console.WriteLine(hasTelegram
    ? "✅ TELEGRAM_BOT_TOKEN configured"
    : "❌ TELEGRAM_BOT_TOKEN is not set — bot disabled, only HTTP endpoints will run");

// ── Composition root: every stateful service is a single DI singleton ─────────
// (Previously services were both new-ed inside the polling loop AND registered in
//  DI, producing split-brain state and file-write races. Now there is exactly one
//  instance of each, shared by polling, the webhook and background jobs.)
if (hasTelegram)
{
    builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(telegramToken!));
}

builder.Services.AddSingleton<GitHubClient>(_ =>
{
    var client = new GitHubClient(new ProductHeaderValue("TelegramGitHubBot"));
    if (!string.IsNullOrWhiteSpace(githubPat))
        client.Credentials = new Credentials(githubPat);
    return client;
});
builder.Services.AddSingleton<GitHubService>();

builder.Services.AddSingleton<GeminiManager>(sp => new GeminiManager(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("gemini"),
    sp.GetRequiredService<IConfiguration>()));

builder.Services.AddSingleton<TenorService>(sp => new TenorService(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("tenor"),
    sp.GetRequiredService<IConfiguration>()));

builder.Services.AddSingleton<AchievementService>();
builder.Services.AddSingleton<MessageStatsService>();
builder.Services.AddSingleton<TelegramBotService>();
builder.Services.AddSingleton<WebhookHandlerService>();

// ── Background jobs (only when the bot is actually configured) ────────────────
if (hasTelegram && pollingEnabled)
{
    builder.Services.AddHostedService<TelegramPollingService>();
    builder.Services.AddHostedService<RepositoryScannerService>();
    if (selfPingEnabled)
        builder.Services.AddHostedService<SelfPingService>();
}
else if (!pollingEnabled)
{
    Console.WriteLine("⚠️ Polling disabled (TELEGRAM_ENABLE_POLLING=false)");
}

var app = builder.Build();

// ── HTTP pipeline ────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Render сам терминирует HTTPS — редирект внутри контейнера ломает healthcheck.
var isRender = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PORT"));
if (!isRender)
    app.UseHttpsRedirection();

var allowedOrigins = (Environment.GetEnvironmentVariable("ALLOWED_ORIGINS") ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (allowedOrigins.Length > 0)
{
    Console.WriteLine($"CORS: restricting to origins: {string.Join("; ", allowedOrigins)}");
    app.UseCors(c => c.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader());
}
else
{
    Console.WriteLine("CORS: ALLOWED_ORIGINS not set — allowing any origin (temporary)");
    app.UseCors(c => c.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
}

// ── Endpoints ────────────────────────────────────────────────────────────────
app.MapPost("/webhook/github", async (HttpContext context, WebhookHandlerService webhookHandler) =>
{
    await webhookHandler.HandleGitHubWebhookAsync(context);
});

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

app.MapGet("/ping", () => Results.Ok(new { status = "pong", timestamp = DateTime.UtcNow }));

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    version = "1.0.0",
    environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
    service = "TelegramGitHubBot"
}));

app.Run();
