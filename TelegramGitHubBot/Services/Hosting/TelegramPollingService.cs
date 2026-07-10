using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Exceptions;

namespace TelegramGitHubBot.Services.Hosting;

/// <summary>
/// Long-polling loop for Telegram updates. Resolves the shared <see cref="TelegramBotService"/>
/// from DI, so polling, the webhook endpoint and background jobs all operate on one instance.
/// Registers the bot command menu on startup and honours graceful shutdown.
/// </summary>
public sealed class TelegramPollingService : BackgroundService
{
    private readonly ITelegramBotClient _botClient;
    private readonly TelegramBotService _bot;
    private readonly ILogger<TelegramPollingService> _logger;

    public TelegramPollingService(
        ITelegramBotClient botClient,
        TelegramBotService bot,
        ILogger<TelegramPollingService> logger)
    {
        _botClient = botClient;
        _bot = bot;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RegisterCommandMenuAsync(stoppingToken);

        try
        {
            // Снимаем вебхук, чтобы избежать 409/Conflict при long polling.
            await _botClient.DeleteWebhookAsync(dropPendingUpdates: true, cancellationToken: stoppingToken);
            _logger.LogInformation("🧹 Telegram webhook deleted before polling start");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("⚠️ Failed to delete webhook: {Message}", ex.Message);
        }

        int? offset = null;
        _logger.LogInformation("🔄 Telegram polling started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await _botClient.GetUpdatesAsync(
                    offset: offset,
                    timeout: 30,
                    cancellationToken: stoppingToken);

                foreach (var update in updates)
                {
                    await DispatchAsync(update, stoppingToken);
                    offset = update.Id + 1;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ApiRequestException apiEx) when (apiEx.ErrorCode == 409)
            {
                _logger.LogWarning("❌ Polling conflict 409: {Message}. Retrying in 10s", apiEx.Message);
                await DelaySafeAsync(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError("❌ Polling error: {Message}", ex.Message);
                await DelaySafeAsync(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("🛑 Telegram polling stopped");
    }

    private async Task DispatchAsync(Update update, CancellationToken ct)
    {
        try
        {
            if (update.Message != null)
            {
                await _bot.HandleMessageAsync(update.Message);
            }
            else if (update.CallbackQuery != null)
            {
                await _bot.HandleCallbackQueryAsync(update.CallbackQuery);
            }
        }
        catch (Exception ex)
        {
            // Один сбойный апдейт не должен ронять весь цикл.
            _logger.LogError("❌ Failed to handle update {UpdateId}: {Message}", update.Id, ex.Message);
        }
    }

    private async Task RegisterCommandMenuAsync(CancellationToken ct)
    {
        try
        {
            await _botClient.SetMyCommandsAsync(BotCommands.Menu, cancellationToken: ct);
            _logger.LogInformation("✅ Telegram command menu registered");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("⚠️ Failed to register command menu: {Message}", ex.Message);
        }
    }

    private static async Task DelaySafeAsync(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); }
        catch (OperationCanceledException) { }
    }
}
