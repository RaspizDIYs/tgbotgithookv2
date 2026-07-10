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
    private void InitializeSwearWords()

    {

        // Хуй и производные

        _swearWords.Add("хуй");

        _swearWords.Add("хуйня");

        _swearWords.Add("хуев");

        _swearWords.Add("хуевый");

        _swearWords.Add("хуярить");

        _swearWords.Add("хуячить");

        _swearWords.Add("хуяк");

        

        // Ебать и производные

        _swearWords.Add("ебать");

        _swearWords.Add("ебаться");

        _swearWords.Add("ебаное");

        _swearWords.Add("ебанутый");

        _swearWords.Add("ебашить");

        _swearWords.Add("ебанько");

        _swearWords.Add("ебучий");

        _swearWords.Add("ебанулся");

        _swearWords.Add("ебанушка");

        _swearWords.Add("ебанат");

        _swearWords.Add("ебало");

        _swearWords.Add("ебатель");

        

        // Блядь и производные

        _swearWords.Add("блядь");

        _swearWords.Add("блядский");

        _swearWords.Add("блядство");

        _swearWords.Add("блядовать");

        _swearWords.Add("блядюга");

        _swearWords.Add("блядня");

        _swearWords.Add("блядюшка");

        

        // Сука и производные

        _swearWords.Add("сука");

        _swearWords.Add("сукин");

        _swearWords.Add("сучий");

        _swearWords.Add("сучара");

        _swearWords.Add("сучатина");

        _swearWords.Add("сучиться");

        _swearWords.Add("сук");

        

        // Уебищ и производные

        _swearWords.Add("уебищ");

        _swearWords.Add("уебок");

        _swearWords.Add("уебан");

        _swearWords.Add("уебать");

        _swearWords.Add("уебаться");

        _swearWords.Add("уебище");

        _swearWords.Add("уебашить");

        

        // Ахуй и производные

        _swearWords.Add("ахуй");

        _swearWords.Add("ахуеть");

        _swearWords.Add("ахуенный");

        _swearWords.Add("ахуевший");

        _swearWords.Add("ахуевать");

        _swearWords.Add("ахуевший");

        _swearWords.Add("ахуительный");

        

        // Пизда и расширенные производные

        _swearWords.Add("пизда");

        _swearWords.Add("пиздец");

        _swearWords.Add("пиздатый");

        _swearWords.Add("пиздецкий");

        _swearWords.Add("пиздить");

        _swearWords.Add("пиздобол");

        _swearWords.Add("пиздюк");

        _swearWords.Add("пиздануть");

        _swearWords.Add("пизданутый");

        _swearWords.Add("припизднутый");

        _swearWords.Add("припиздячить");

        _swearWords.Add("пиздабол");

        _swearWords.Add("пиздюлина");

        _swearWords.Add("пиздюль");

        

        // Похуй и производные

        _swearWords.Add("похуй");

        _swearWords.Add("похуям");

        _swearWords.Add("поахуевали");

        _swearWords.Add("похуист");

        _swearWords.Add("похуистика");

        _swearWords.Add("похуйщина");

        _swearWords.Add("похуйствовать");

        

        // Хуйлан и производные

        _swearWords.Add("хуйлан");

        _swearWords.Add("хуила");

        _swearWords.Add("хуйлуша");

        _swearWords.Add("хуйло");

        _swearWords.Add("хуйня");

        _swearWords.Add("хуйман");

        _swearWords.Add("хуйма");

        

        // Блядота и производные

        _swearWords.Add("блядота");

        _swearWords.Add("блядина");

        _swearWords.Add("блядюк");

        _swearWords.Add("блядюшка");

        _swearWords.Add("блядство");

        _swearWords.Add("блядовать");

    }

    private async Task CheckSwearWordsAsync(long chatId, long userId, string text)

    {

        var lowerText = text.ToLower();

        var swearCount = 0;



        foreach (var swearWord in _swearWords)

        {

            if (lowerText.Contains(swearWord))

            {

                swearCount++;

            }

        }



        if (swearCount > 0)

        {

            if (!_swearWordCounters.ContainsKey(userId))

            {

                _swearWordCounters[userId] = 0;

            }



            _swearWordCounters[userId] += swearCount;



            if (_swearWordCounters[userId] >= 100)

            {

                var shameMessage = "Позор! Позор! Позор! Уже 100 оскорблений в чате от тебя!";

                var gifUrl = "https://media1.tenor.com/m/5t7dwIkeSioAAAAC/shame-bell.gif";

                

                await _botClient.SendAnimationAsync(chatId, InputFile.FromUri(gifUrl), caption: shameMessage);

                _swearWordCounters[userId] = 0;

            }

        }

    }
}
