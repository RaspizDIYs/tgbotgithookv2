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
    public void ScheduleMessageDeletion(long chatId, int messageId, int delayMinutes = 30)

    {

        var timerKey = $"{chatId}:{messageId}";

        var timer = new System.Timers.Timer(delayMinutes * 60 * 1000); // Конвертируем минуты в миллисекунды



        timer.Elapsed += async (sender, e) =>

        {

            try

            {

                Console.WriteLine($"🗑️ Auto-deleting message {messageId} from chat {chatId} after {delayMinutes} minutes");

                await _botClient.DeleteMessageAsync(chatId, messageId);

                Console.WriteLine($"✅ Message {messageId} deleted successfully");

            }

            catch (Exception ex)

            {

                Console.WriteLine($"❌ Failed to delete message {messageId}: {ex.Message}");

            }

            finally

            {

                // Очищаем таймер после выполнения

                timer.Stop();

                timer.Dispose();

                _messageTimers.TryRemove(timerKey, out _);

            }

        };



        timer.AutoReset = false; // Одноразовый таймер

        timer.Start();



        // Сохраняем таймер для возможной отмены

        _messageTimers[timerKey] = timer;



        Console.WriteLine($"⏰ Scheduled deletion of message {messageId} from chat {chatId} in {delayMinutes} minutes");

    }

    public async Task SendAutoDeletingMessageAsync(long chatId, string text, int delayMinutes = 30, ParseMode? parseMode = null, IReplyMarkup? replyMarkup = null)

    {

        try

        {

            var message = await _botClient.SendTextMessageAsync(

                chatId: chatId,

                text: text,

                parseMode: parseMode,

                disableNotification: true,

                replyMarkup: replyMarkup

            );



            // Запланируем удаление сообщения через указанное время

            ScheduleMessageDeletion(chatId, message.MessageId, delayMinutes);

        }

        catch (Exception ex)

        {

            Console.WriteLine($"❌ Failed to send auto-deleting message: {ex.Message}");

            throw;

        }

    }
}
