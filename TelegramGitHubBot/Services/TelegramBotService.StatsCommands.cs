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
    private async Task ShowAchievementPageAsync(long chatId, int index, int? messageIdToEdit)

    {

        var list = _achievementService.GetAllAchievements().OrderBy(a => a.Name).ToList();

        if (list.Count == 0)

        {

            await _botClient.SendTextMessageAsync(chatId, "🏆 Пока нет ачивок", disableNotification: true);

            return;

        }



        var count = list.Count;

        // нормализуем индекс

        var idx = ((index % count) + count) % count;

        var a = list[idx];



        var status = a.IsUnlocked ? "✅" : "❌";

        var holder = a.IsUnlocked && !string.IsNullOrEmpty(a.HolderName) ? $" (\u2014 {a.HolderName})" : "";

        var value = a.Value.HasValue ? $" [{a.Value}]" : "";

        var caption = $"{a.Emoji} *{a.Name}*\n{a.Description}{holder}{value}\n\n_{idx + 1}/{count}_";



        var keyboard = new InlineKeyboardMarkup(new[]

        {

            new []

            {

                InlineKeyboardButton.WithCallbackData("⬅️", $"achv:prev:{idx}"),

                InlineKeyboardButton.WithCallbackData("➡️", $"achv:next:{idx}")

            },

            new [] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

        });



        try

        {

            // Убираем удаление предыдущих сообщений для корректной работы



            var url = a.GifUrl?.Trim() ?? string.Empty;

            try

            {

                await _botClient.SendAnimationAsync(

                    chatId: chatId,

                    animation: InputFile.FromUri(url),

                    caption: caption,

                    parseMode: ParseMode.Markdown,

                    disableNotification: true,

                    replyMarkup: keyboard

                );

            }

            catch (Telegram.Bot.Exceptions.ApiRequestException apiEx)

            {

                // Авто-фолбэк: если это .gif с media.tenor.com, попробуем .mp4

                if (url.Contains("media.tenor.com") && url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))

                {

                    var mp4Url = url[..^4] + ".mp4";

                    Console.WriteLine($"⚠️ GIF failed, retrying MP4: {mp4Url}. Error: {apiEx.Message}");

                    await _botClient.SendAnimationAsync(

                        chatId: chatId,

                        animation: InputFile.FromUri(mp4Url),

                        caption: caption,

                        parseMode: ParseMode.Markdown,

                        disableNotification: true,

                        replyMarkup: keyboard

                    );

                }

                else

                {

                    throw;

                }

            }

        }

        catch (Exception ex)

        {

            Console.WriteLine($"❌ Failed to show achievement page: {ex.Message}");

            // Фоллбек: текст без гифки

            if (messageIdToEdit.HasValue && messageIdToEdit.Value != 0)

            {

                await _botClient.EditMessageTextAsync(chatId, messageIdToEdit.Value, caption, parseMode: ParseMode.Markdown, replyMarkup: keyboard);

            }

            else

            {

                await _botClient.SendTextMessageAsync(chatId, caption, parseMode: ParseMode.Markdown, disableNotification: true, replyMarkup: keyboard);

            }

        }

    }

    private async Task HandleStatsCommandAsync(long chatId)

    {

        var stats = _messageStatsService.GetChatStats(chatId);

        if (stats == null)

        {

            await SendMessageWithBackButtonAsync(chatId, "Статистика пока пуста.");

            return;

        }



        var top = _messageStatsService.GetTopUsers(chatId, 5);

        var lines = new List<string>();

        lines.Add($"Всего сообщений в чате: {stats.TotalMessages}");

        if (top.Count > 0)

        {

            lines.Add("");

            lines.Add("Топ активистов:");

            int rank = 1;

            foreach (var (userId, count) in top)

            {

                lines.Add($"{rank}. id:{userId} — {count}");

                rank++;

            }

        }



        var text = string.Join("\n", lines);

        await SendMessageWithBackButtonAsync(chatId, text);

    }

    private async Task ShowWeekSelectionAsync(long chatId)

    {

        try

        {

            var message = "📊 *Выберите неделю для статистики:*\n\n";

            

            var buttons = new List<InlineKeyboardButton[]>();

            

            // Добавляем кнопки для последних 4 недель

            for (int i = 0; i < 4; i++)

            {

                var weekStart = DateTime.Now.AddDays(-7 * i - (int)DateTime.Now.DayOfWeek + 1);

                var weekEnd = weekStart.AddDays(6);

                var weekText = $"{weekStart:dd.MM} - {weekEnd:dd.MM}";

                

                if (i == 0) weekText += " (текущая)";

                

                buttons.Add(new[] { InlineKeyboardButton.WithCallbackData($"📅 {weekText}", $"week_stats:{i}") });

            }



            buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") });



            var keyboard = new InlineKeyboardMarkup(buttons);



            await _botClient.SendTextMessageAsync(

                chatId: chatId,

                text: message,

                parseMode: ParseMode.Markdown,

                disableNotification: true,

                replyMarkup: keyboard

            );

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка показа недель: {ex.Message}", disableNotification: true);

        }

    }

    private async Task HandleRatingCommandAsync(long chatId)

    {

        try

        {

            var rating = await _gitHubService.GetDeveloperRatingAsync();

            

            var keyboard = new InlineKeyboardMarkup(new[]

            {

                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

            });



            await _botClient.SendTextMessageAsync(

                chatId: chatId,

                text: rating,

                parseMode: ParseMode.Markdown,

                disableNotification: true,

                replyMarkup: keyboard

            );

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения рейтинга: {ex.Message}", disableNotification: true);

        }

    }

    private async Task HandleTrendsCommandAsync(long chatId)

    {

        try

        {

            var trends = await _gitHubService.GetActivityTrendsAsync();

            

            var keyboard = new InlineKeyboardMarkup(new[]

            {

                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

            });



            await _botClient.SendTextMessageAsync(

                chatId: chatId,

                text: trends,

                parseMode: ParseMode.Markdown,

                disableNotification: true,

                replyMarkup: keyboard

            );

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения трендов: {ex.Message}", disableNotification: true);

        }

    }

    private async Task HandleWeekStatsCallbackAsync(long chatId, string callbackData, int messageId)

    {

        try

        {

            var parts = callbackData.Split(':');

            if (parts.Length < 2) return;



            var weekOffset = int.Parse(parts[1]);

            

            // Убираем удаление сообщений для корректной работы



            var weekStats = await _gitHubService.GetWeeklyStatsAsync(weekOffset);



            var keyboard = new InlineKeyboardMarkup(new[]

            {

                new[] { InlineKeyboardButton.WithCallbackData("📊 Выбрать другую неделю", "/weekstats") },

                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

            });



            await _botClient.SendTextMessageAsync(

                chatId: chatId,

                text: weekStats,

                parseMode: ParseMode.Markdown,

                disableNotification: true,

                replyMarkup: keyboard

            );

        }

        catch (Exception ex)

        {

            Console.WriteLine($"❌ Error handling week stats callback: {ex.Message}");

        }

    }

    private async Task HandleAchievementsCommandAsync(long chatId)

    {

        try

        {

            var achievements = _achievementService.GetAllAchievements();

            

            if (!achievements.Any())

            {

                await _botClient.SendTextMessageAsync(chatId, "🏆 Пока никто не получил ачивок!\n\nНачните коммитить, чтобы получить первые награды!", disableNotification: true);

                return;

            }



            var message = "🏆 *Список ачивок*\n\n";

            

            foreach (var achievement in achievements.OrderBy(a => a.Name))

            {

                var status = achievement.IsUnlocked ? "✅" : "❌";

                var holder = achievement.IsUnlocked && !string.IsNullOrEmpty(achievement.HolderName) 

                    ? $" ({achievement.HolderName})" 

                    : "";

                var value = achievement.Value.HasValue ? $" [{achievement.Value}]" : "";

                

                message += $"{status} {achievement.Emoji} *{achievement.Name}*\n";

                message += $"   {achievement.Description}{holder}{value}\n\n";

            }



            await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, disableNotification: true);



            // Отправляем гифки для каждой ачивки (Tenor URL поддерживается Telegram без API)

            foreach (var achievement in achievements.Where(a => a.IsUnlocked))

            {

                try

                {

                    await _botClient.SendAnimationAsync(

                        chatId: chatId,

                        animation: InputFile.FromUri(NormalizeTenorUrl(achievement.GifUrl)),

                        caption: $"{achievement.Emoji} *{achievement.Name}*\n{achievement.Description}",

                        parseMode: ParseMode.Markdown,

                        disableNotification: true

                    );

                    

                    // Небольшая задержка между гифками

                    await Task.Delay(1000);

                }

                catch (Exception ex)

                {

                    Console.WriteLine($"Ошибка отправки гифки для ачивки {achievement.Name}: {ex.Message}");

                }

            }

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения ачивок: {ex.Message}", disableNotification: true);

        }

    }

    private async Task HandleLeaderboardCommandAsync(long chatId)

    {

        try

        {

            var topUsers = _achievementService.GetTopUsers(10);

            var topStreakUsers = _achievementService.GetTopUsersByStreak(5);

            

            if (!topUsers.Any())

            {

                await _botClient.SendTextMessageAsync(chatId, "📊 Пока нет статистики!\n\nНачните коммитить, чтобы попасть в таблицу лидеров!", disableNotification: true);

                return;

            }



            var message = "🏆 *Таблица лидеров*\n\n";

            

            // Основная таблица по коммитам

            message += "📊 *По количеству коммитов:*\n";

            for (int i = 0; i < topUsers.Count; i++)

            {

                var user = topUsers[i];

                var medal = i switch

                {

                    0 => "🥇",

                    1 => "🥈", 

                    2 => "🥉",

                    _ => $"#{i + 1}"

                };

                

                var streakEmoji = _achievementService.GetStreakEmoji(user.LongestStreak);

                

                message += $"{medal} *{user.DisplayName}*\n";

                message += $"   📊 Коммитов: {user.TotalCommits}\n";

                message += $"   ⚡ Макс. строк: {user.MaxLinesChanged}\n";

                message += $"   {streakEmoji} Стрик: {user.LongestStreak} дн.\n";

                message += $"   🧪 Тесты: {user.TestCommits} | 🚀 Релизы: {user.ReleaseCommits}\n";

                message += $"   🐛 Баги: {user.BugFixCommits} | ✨ Фичи: {user.FeatureCommits}\n\n";

            }



            // Топ по стрикам

            if (topStreakUsers.Any())

            {

                message += "🔥 *Топ стриков:*\n";

                for (int i = 0; i < topStreakUsers.Count; i++)

                {

                    var user = topStreakUsers[i];

                    var streakEmoji = _achievementService.GetStreakEmoji(user.LongestStreak);

                    message += $"{streakEmoji} *{user.DisplayName}* - {user.LongestStreak} дн.\n";

                }

            }



            await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, disableNotification: true);

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения таблицы лидеров: {ex.Message}", disableNotification: true);

        }

    }

    private async Task HandleStreaksCommandAsync(long chatId)

    {

        try

        {

            var topStreakUsers = _achievementService.GetTopUsersByStreak(10);

            

            if (!topStreakUsers.Any())

            {

                await _botClient.SendTextMessageAsync(chatId, "🔥 Пока нет стриков!\n\nНачните коммитить каждый день, чтобы создать стрик!", disableNotification: true);

                return;

            }



            var message = "🔥 *Топ стриков*\n\n";

            message += "Подсказка: чем больше стрик, тем больше 🔥\n\n";

            

            for (int i = 0; i < topStreakUsers.Count; i++)

            {

                var user = topStreakUsers[i];

                var medal = i switch

                {

                    0 => "🥇",

                    1 => "🥈", 

                    2 => "🥉",

                    _ => $"#{i + 1}"

                };

                

                var streakEmoji = _achievementService.GetStreakEmoji(user.LongestStreak);

                message += $"{medal} *{user.DisplayName}* — {user.LongestStreak} дн. {streakEmoji}\n";

            }



            await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, disableNotification: true);

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения стриков: {ex.Message}", disableNotification: true);

        }

    }

    private async Task HandleRecalcCommandAsync(long chatId)

    {

        try

        {

            // Проверяем rate limit перед началом

            var (remaining, limit, resetTime) = await _gitHubService.GetRateLimitAsync();

            

            if (remaining < 500)

            {

                var timeUntilReset = resetTime - DateTime.UtcNow;

                var message = $"⚠️ *Предупреждение о лимитах GitHub API*\n\n" +

                             $"📊 Доступно запросов: {remaining}/{limit}\n" +

                             $"⏰ Сброс через: {timeUntilReset.Minutes} мин\n\n" +

                             $"⚡ Пересчёт может израсходовать до 2000+ запросов!\n\n" +

                             $"Рекомендации:\n" +

                             $"• Подождите до сброса лимита\n" +

                             $"• Или используйте /recalc light (только основная ветка)";

                

                await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, disableNotification: true);

                return;

            }



            await _botClient.SendTextMessageAsync(chatId, $"🔄 Запускаю пересчёт ачивок...\n\n📊 Доступно запросов: {remaining}/{limit}", disableNotification: true);



            // Сбрасываем все данные

            _achievementService.ResetAllData();



            // Получаем ветки; если пусто — пробуем основную ветку

            var branches = await _gitHubService.GetBranchesListAsync();

            if (!branches.Any())

            {

                var def = await _gitHubService.TryGetDefaultBranchAsync();

                if (!string.IsNullOrEmpty(def)) branches = new List<string> { def };

            }



            var totalProcessed = 0;

            var branchCount = 0;

            var startTime = DateTime.UtcNow;

            

            foreach (var branch in branches)

            {

                branchCount++;

                await _botClient.SendTextMessageAsync(chatId, $"📊 Обрабатываю ветку {branchCount}/{branches.Count}: `{branch}`...", parseMode: ParseMode.Markdown, disableNotification: true);

                

                var history = await _gitHubService.GetAllCommitsWithStatsForBranchAsync(branch, 2000);

                foreach (var c in history)

                {

                    _achievementService.ProcessCommitBatch(c.Author, c.Email, c.Message, c.Date, c.Additions, c.Deletions);

                }

                totalProcessed += history.Count;

                

                // Показываем промежуточный прогресс

                var (currentRemaining, _, _) = await _gitHubService.GetRateLimitAsync();

                var used = remaining - currentRemaining;

                Console.WriteLine($"📊 Branch {branch}: {history.Count} commits, API calls used: {used}");

            }



            // Сохраняем все изменения один раз в конце

            _achievementService.SaveAll();



            var duration = DateTime.UtcNow - startTime;

            var (finalRemaining, _, _) = await _gitHubService.GetRateLimitAsync();

            var totalUsed = remaining - finalRemaining;



            await _botClient.SendTextMessageAsync(chatId, 

                $"✅ *Пересчёт завершён!*\n\n" +

                $"📊 Обработано коммитов: {totalProcessed}\n" +

                $"🌿 Веток: {branchCount}\n" +

                $"⏱️ Время: {duration.TotalSeconds:F1} сек\n" +

                $"📈 API запросов: {totalUsed}\n" +

                $"💾 Осталось: {finalRemaining}/{limit}\n\n" +

                $"💾 Данные сохранены", 

                parseMode: ParseMode.Markdown, 

                disableNotification: true);

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка пересчёта: {ex.Message}", disableNotification: true);

        }

    }
}
