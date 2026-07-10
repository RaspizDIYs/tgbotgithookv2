namespace TelegramGitHubBot.Services.Hosting;

/// <summary>
/// Periodically pings the app's own /ping endpoint to keep the instance warm on hosts
/// that idle out free tiers (e.g. Render). Enabled only when SELF_PING_ENABLED=true.
/// </summary>
public sealed class SelfPingService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SelfPingService> _logger;

    public SelfPingService(IHttpClientFactory httpClientFactory, ILogger<SelfPingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var baseUrl = ResolveBaseUrl();
        var pingUrl = $"{baseUrl.TrimEnd('/')}/ping";
        var client = _httpClientFactory.CreateClient();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await client.GetAsync(pingUrl, stoppingToken);
                if (!response.IsSuccessStatusCode)
                    _logger.LogWarning("⚠️ Self-ping failed: {StatusCode}", response.StatusCode);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning("❌ Self-ping error: {Message}", ex.Message);
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static string ResolveBaseUrl()
    {
        var baseUrl = Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")?.Split(';').FirstOrDefault()
            ?? "http://localhost:5000";

        // '+' — валидный биндинг Kestrel, но не валидный hostname для запроса.
        return baseUrl.Replace("http://+", "http://localhost").Replace("https://+", "https://localhost");
    }
}
