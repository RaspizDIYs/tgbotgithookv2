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
    private async Task SendSettingsMessageAsync(long chatId)

    {

        var settings = GetOrCreateSettings(chatId);



        var message = @"⚙️ *Настройки уведомлений*



Выберите типы уведомлений, которые хотите получать:";



        var inlineKeyboard = new InlineKeyboardMarkup(new[]

        {

            new[]

            {

                InlineKeyboardButton.WithCallbackData(

                    $"{(settings.PushNotifications ? "✅" : "❌")} Коммиты",

                    $"toggle:push:{chatId}"),

                InlineKeyboardButton.WithCallbackData(

                    $"{(settings.PullRequestNotifications ? "✅" : "❌")} PR/MR",

                    $"toggle:pr:{chatId}")

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData(

                    $"{(settings.WorkflowNotifications ? "✅" : "❌")} CI/CD",

                    $"toggle:ci:{chatId}"),

                InlineKeyboardButton.WithCallbackData(

                    $"{(settings.ReleaseNotifications ? "✅" : "❌")} Релизы",

                    $"toggle:release:{chatId}")

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData(

                    $"{(settings.IssueNotifications ? "✅" : "❌")} Задачи",

                    $"toggle:issue:{chatId}")

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

            replyMarkup: inlineKeyboard

        );

    }

    private async Task HandleNotificationToggleAsync(long chatId, string callbackData, int messageId, string callbackQueryId)

    {

        try

        {

            // Разбираем callback data: toggle:type:chatId

            var parts = callbackData.Split(':');

            if (parts.Length < 3)

            {

                await _botClient.AnswerCallbackQueryAsync(callbackQueryId, "❌ Ошибка: некорректные данные");

                return;

            }



            var type = parts[1];

            var targetChatId = long.Parse(parts[2]);



            if (chatId != targetChatId)

            {

                await _botClient.AnswerCallbackQueryAsync(callbackQueryId, "❌ Ошибка: неправильный чат");

                return;

            }



            var settings = GetOrCreateSettings(chatId);



            // Переключаем соответствующую настройку

            string notificationType = "";

            switch (type)

            {

                case "push":

                    settings.PushNotifications = !settings.PushNotifications;

                    notificationType = "Коммиты";

                    break;

                case "pr":

                    settings.PullRequestNotifications = !settings.PullRequestNotifications;

                    notificationType = "PR/MR";

                    break;

                case "ci":

                    settings.WorkflowNotifications = !settings.WorkflowNotifications;

                    notificationType = "CI/CD";

                    break;

                case "release":

                    settings.ReleaseNotifications = !settings.ReleaseNotifications;

                    notificationType = "Релизы";

                    break;

                case "issue":

                    settings.IssueNotifications = !settings.IssueNotifications;

                    notificationType = "Задачи";

                    break;

                default:

                    await _botClient.AnswerCallbackQueryAsync(callbackQueryId, "❌ Неизвестный тип уведомления");

                    return;

            }



            // Обновляем сообщение с новыми настройками

            await UpdateSettingsMessageAsync(chatId, messageId);



            // Отправляем подтверждение

            var statusText = GetNotificationStatus(settings, type);

            await _botClient.AnswerCallbackQueryAsync(callbackQueryId, $"{statusText} {notificationType}");



            Console.WriteLine($"⚙️ Toggled {type} notifications for chat {chatId}: {statusText}");

        }

        catch (Exception ex)

        {

            Console.WriteLine($"❌ Error toggling notification: {ex.Message}");

            await _botClient.AnswerCallbackQueryAsync(callbackQueryId, "❌ Произошла ошибка");

        }

    }

    private async Task UpdateSettingsMessageAsync(long chatId, int messageId)

    {

        try

        {

            var settings = GetOrCreateSettings(chatId);



            var message = @"⚙️ *Настройки уведомлений*



Выберите типы уведомлений, которые хотите получать:";



            var inlineKeyboard = new InlineKeyboardMarkup(new[]

            {

                new[]

                {

                    InlineKeyboardButton.WithCallbackData(

                        $"{(settings.PushNotifications ? "✅" : "❌")} Коммиты",

                        $"toggle:push:{chatId}"),

                    InlineKeyboardButton.WithCallbackData(

                        $"{(settings.PullRequestNotifications ? "✅" : "❌")} PR/MR",

                        $"toggle:pr:{chatId}")

                },

                new[]

                {

                    InlineKeyboardButton.WithCallbackData(

                        $"{(settings.WorkflowNotifications ? "✅" : "❌")} CI/CD",

                        $"toggle:ci:{chatId}"),

                    InlineKeyboardButton.WithCallbackData(

                        $"{(settings.ReleaseNotifications ? "✅" : "❌")} Релизы",

                        $"toggle:release:{chatId}")

                },

                new[]

                {

                    InlineKeyboardButton.WithCallbackData(

                        $"{(settings.IssueNotifications ? "✅" : "❌")} Задачи",

                        $"toggle:issue:{chatId}")

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

                replyMarkup: inlineKeyboard

            );



            Console.WriteLine($"✅ Updated settings message for chat {chatId}");

        }

        catch (Exception ex)

        {

            Console.WriteLine($"❌ Error updating settings message: {ex.Message}");

        }

    }

    private string GetNotificationStatus(NotificationSettings settings, string type)

    {

        return type switch

        {

            "push" => settings.PushNotifications ? "Включены" : "Отключены",

            "pr" => settings.PullRequestNotifications ? "Включены" : "Отключены",

            "ci" => settings.WorkflowNotifications ? "Включены" : "Отключены",

            "release" => settings.ReleaseNotifications ? "Включены" : "Отключены",

            "issue" => settings.IssueNotifications ? "Включены" : "Отключены",

            _ => "Неизвестно"

        };

    }

    public bool ShouldSendNotification(long chatId, string notificationType)

    {

        var settings = GetOrCreateSettings(chatId);



        Console.WriteLine($"🔍 Checking notification settings for chat {chatId}, type: {notificationType}");

        Console.WriteLine($"   Push: {settings.PushNotifications}, PR: {settings.PullRequestNotifications}, CI: {settings.WorkflowNotifications}, Release: {settings.ReleaseNotifications}, Issues: {settings.IssueNotifications}");



        var result = notificationType switch

        {

            "push" => settings.PushNotifications,

            "pull_request" => settings.PullRequestNotifications,

            "workflow" => settings.WorkflowNotifications,

            "release" => settings.ReleaseNotifications,

            "issues" => settings.IssueNotifications,

            _ => true // По умолчанию отправляем все неизвестные типы

        };



        Console.WriteLine($"   Result for {notificationType}: {result}");

        return result;

    }
}
