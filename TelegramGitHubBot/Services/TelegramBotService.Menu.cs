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
    private async Task HandleSubmenuAsync(long chatId, int messageId, string menuData)

    {

        try

        {

            var menuType = menuData.Split(':')[1];



            switch (menuType)

            {

                case "main":

                    await SendHelpMessageAsync(chatId);

                    break;

                case "git":

                    await ShowGitMenuAsync(chatId, messageId);

                    break;

                case "stats":

                    await ShowStatsMenuAsync(chatId, messageId);

                    break;

                case "gemini":

                    await ShowGeminiMenuAsync(chatId, messageId);

                    break;

            }

        }

        catch (Exception ex)

        {

            Console.WriteLine($"❌ Error handling submenu: {ex.Message}");

        }

    }

    private async Task ShowGitMenuAsync(long chatId, int messageId)

    {

        var message = "📦 *Git - Работа с репозиторием*\n\n" +

                     "Выберите действие:";



        var keyboard = new InlineKeyboardMarkup(new[]

        {

            new[]

            {

                InlineKeyboardButton.WithCallbackData("📊 Статус", "/status"),

                InlineKeyboardButton.WithCallbackData("📝 Коммиты", "/commits"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🌿 Ветки", "/branches"),

                InlineKeyboardButton.WithCallbackData("🔄 PR", "/prs"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⚙️ CI/CD", "/ci"),

                InlineKeyboardButton.WithCallbackData("🚀 Деплой", "/deploy"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🔍 Поиск", "search_menu"),

                InlineKeyboardButton.WithCallbackData("👥 Авторы", "/authors"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад")

            }

        });



        await _botClient.SendTextMessageAsync(

            chatId: chatId,

            text: message,

            parseMode: ParseMode.Markdown,

            disableWebPagePreview: true,

            disableNotification: true,

            replyMarkup: keyboard

        );

    }

    private async Task ShowStatsMenuAsync(long chatId, int messageId)

    {

        var message = "📊 *Stats - Статистика и достижения*\n\n" +

                     "Выберите раздел:";



        var keyboard = new InlineKeyboardMarkup(new[]

        {

            new[]

            {

                InlineKeyboardButton.WithCallbackData("📈 Последняя статистика", "/laststats"),

                InlineKeyboardButton.WithCallbackData("📊 По неделям", "/weekstats"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🏆 Рейтинг", "/rating"),

                InlineKeyboardButton.WithCallbackData("📉 Тренды", "/trends"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🏅 Ачивки", "/achivelist"),

                InlineKeyboardButton.WithCallbackData("🥇 Лидеры", "/leaderboard"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🔥 Стрики", "/streaks"),

                InlineKeyboardButton.WithCallbackData("📈 API лимиты", "/ratelimit"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("💾 Кэш", "/cache"),

                InlineKeyboardButton.WithCallbackData("🔄 Пересчёт", "/recalc"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад")

            }

        });



        await _botClient.SendTextMessageAsync(

            chatId: chatId,

            text: message,

            parseMode: ParseMode.Markdown,

            disableWebPagePreview: true,

            disableNotification: true,

            replyMarkup: keyboard

        );

    }

    private async Task ShowGeminiMenuAsync(long chatId, int messageId)

    {

        var message = "🤖 *Gemini AI - Искусственный интеллект*\n\n" +

                     "Выберите действие:";



        var keyboard = new InlineKeyboardMarkup(new[]

        {

            new[]

            {

                InlineKeyboardButton.WithCallbackData("▶️ Включить AI", "/glaistart"),

                InlineKeyboardButton.WithCallbackData("⏹️ Выключить AI", "/glaistop"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("📊 Статус агентов", "/glaistats"),

                InlineKeyboardButton.WithCallbackData("🔍 Текущий агент", "/glaicurrent"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🔄 Переключить", "/glaiswitch"),

                InlineKeyboardButton.WithCallbackData("🧹 Очистить контекст", "/glaiclear"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад")

            }

        });



        await _botClient.EditMessageTextAsync(chatId, messageId, message, parseMode: ParseMode.Markdown, replyMarkup: keyboard);

    }
}
