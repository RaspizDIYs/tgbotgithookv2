using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Timers;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramGitHubBot.Services;

public partial class TelegramBotService
{
    private string NormalizeTenorUrl(string url)

    {

        if (string.IsNullOrWhiteSpace(url)) return url;

        var u = url.Trim();

        // Replace known host variants to media.tenor.com

        if (u.Contains("tenor.com") && !u.Contains("media.tenor.com"))

        {

            u = u.Replace("https://tenor.com/view/", "https://media.tenor.com/")

                 .Replace("https://tenor.com/ru/view/", "https://media.tenor.com/");

        }

        // If page URL slipped through, leave it, fallback in sender will try mp4

        return u;

    }

    private static string StripGifTags(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text ?? "", @"\[GIF:[^\]]*\]", "").Trim();

    private async Task HandleTldrCommandAsync(long chatId)
    {
        if (!_recentMessages.TryGetValue(chatId, out var q) || q.Count < 3)
        {
            await _botClient.SendTextMessageAsync(chatId, "Пока нечего пересказывать — маловато сообщений.", disableNotification: true);
            return;
        }
        try
        {
            var thread = string.Join("\n", q);
            var prompt = "Кратко перескажи простым языком, о чём шёл разговор в чате. Буллетами, без воды, не выдумывай. Сообщения:\n" + thread;
            var response = await _geminiManager.GenerateResponseAsync(prompt);
            var clean = System.Text.RegularExpressions.Regex.Replace(response, @"\[GIF:[^\]]*\]", "").Trim();
            await _botClient.SendTextMessageAsync(chatId, $"📝 Пересказ последних {q.Count} сообщений:\n\n{clean}", disableNotification: true);
        }
        catch (Exception ex)
        {
            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка: {ex.Message}", disableNotification: true);
        }
    }
}
