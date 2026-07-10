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
    private async Task SendWelcomeMessageAsync(long chatId)

    {
        _navigationStack.Remove(chatId); // верхнеуровневый экран — сбрасываем цепочку «назад»

        var message = @"🤖 *GitHub Monitor Bot*

Мониторинг репозитория goodluckv2



📢 Уведомления о:

• Коммитах

• PR/MR

• CI/CD

• Релизах



💡 Выберите раздел из меню ниже:";



        var inlineKeyboard = new InlineKeyboardMarkup(new[]

        {

            new[]

            {

                InlineKeyboardButton.WithCallbackData("📦 Git", "menu:git"),

                InlineKeyboardButton.WithCallbackData("📊 Stats", "menu:stats"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⚙️ Настройки", "/settings"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("ℹ️ Инфо", "/info"),

                InlineKeyboardButton.WithCallbackData("❓ Справка", "/help"),

            }

        });



        await ShowNavScreenAsync(chatId, message, inlineKeyboard);

    }

    private async Task SendHelpMessageAsync(long chatId)

    {
        _navigationStack.Remove(chatId); // верхнеуровневый экран — сбрасываем цепочку «назад»

        var message = @"📋 *Справка по боту*

🏠 /start - Главное меню
ℹ️ /info - Подробная информация
❓ /help - Эта справка

📦 *GitHub - работа с репозиторием:*
📊 /status - Статус репозитория
📝 /commits [ветка] [n] - Коммиты
🌿 /branches - Список веток
🔄 /prs - Открытые PR
⚙️ /ci [ветка] - CI/CD статус
🚀 /deploy [среда] - Деплой
🔎 /search <запрос> - Поиск по коммитам
👥 /authors - Активные авторы
📁 /files <sha> - Файлы в коммите
📈 /ratelimit - GitHub API лимиты
💾 /cache - Информация о кэше
🛡️ /protection - Защита веток
💾 /backup - Резервное копирование

📊 *Статистика чата и достижения:*
📈 /laststats - Последняя статистика
📊 /weekstats - Статистика по неделям
🏆 /rating - Рейтинг разработчиков
📉 /trends - Тренды активности
🏅 /achievements - Список всех ачивок
🥇 /leaderboard - Таблица лидеров
🔥 /streaks - Топ стриков
🔄 /recalc - Пересчёт статистики

🗂 *Jira:*
📋 /jira - Быстрый дайджест задач KAN (frontend/backend)
☀️ /digest - Утренний дайджест-план (Jira срезы + GlitchTip + ИИ, персонально)

🤖 *Gemini AI:*
▶️ /glaistart - Включить режим AI
⏹️ /glaistop - Выключить режим AI
📊 /glaistats - Статус всех агентов
🔍 /glaicurrent - Текущий агент
🔄 /glaiswitch - Переключить агента
🧹 /glaiclear - Очистить контекст
❓ /ask <вопрос> - Вопрос к AI (может смотреть коммиты/PR/CI/статистику)
📝 /tldr - Краткая выжимка обсуждения

⚙️ *Настройки:*
⚙️ /settings - Настройки уведомлений

💡 *Подсказки:*
• В режиме Gemini все сообщения отправляются в AI";



        var inlineKeyboard = new InlineKeyboardMarkup(new[]

        {

            new[]

            {

                InlineKeyboardButton.WithCallbackData("📦 Git", "menu:git"),

                InlineKeyboardButton.WithCallbackData("📊 Stats", "menu:stats"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🤖 Gemini AI", "menu:gemini"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("⚙️ Настройки", "/settings"),

            },

            new[]

            {

                InlineKeyboardButton.WithCallbackData("ℹ️ Инфо", "/info"),

                InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "/start"),

            }

        });



        await ShowNavScreenAsync(chatId, message, inlineKeyboard);

    }

    private async Task SendInfoMessageAsync(long chatId)

    {

        var message = @"ℹ️ *Информация о боте*



🤖 *GitHub Monitor Bot*

Бот для мониторинга репозитория RaspizDIYs/goodluckv2



📦 *Git функционал:*

• Отслеживание коммитов, PR, CI/CD

• Статистика по веткам и авторам

• Поиск по истории коммитов



📊 *Статистика и достижения:*

• Система ачивок и рейтингов

• Стрики коммитов

• Детальная аналитика активности

• Мониторинг лимитов GitHub API



🖱️ *Интеграция с Cursor:*

Команда `/deep` создаёт диплинк для открытия файла или выполнения команды в Cursor.



*Примеры файлов:*

• `/deep src/components/Button.tsx`

  Откроет файл Button.tsx



• `/deep src/components/Button.tsx:150`

  Откроет файл на строке 150



• `/deep src/components/Button.tsx:150:10`

  Откроет файл на строке 150, колонке 10



*Примеры команд:*

• `/deep Запусти в терминале билд`

  Выполнит команду в Cursor



• `/deep Создай компонент Button`

  Попросит Cursor создать компонент



• `/deep Исправь ошибки в коде`

  Попросит Cursor исправить ошибки



*Форматы диплинков:*

• Файлы: `cursor://file/{workspace}/{path}?line={line}&column={column}`

• Команды: `cursor://anysphere.cursor-deeplink/prompt?text={command}`



*Workspace репозитория:*

goodluckv2 (настраивается через GOODLUCK_WORKSPACE_PATH)



📈 *Мониторинг API:*

Команда `/ratelimit` показывает текущие лимиты GitHub API.



⚠️ *Важно:*

• GitHub API: 5000 запросов/час

• `/recalc` использует ~2000+ запросов

• Проверяйте лимиты перед пересчётом

• Данные кешируются в JSON файлах



*Хранение данных*

Бот использует JSON файлы как память

- user_stats.json - статистика пользователей

- achievements.json - полученные достижения

- processed_shas.json - обработанные коммиты



*Умная очистка кэша*

- Автоматическая очистка старых данных

- Максимум 10,000 SHA в кэше

- Удаление неактивных пользователей (более 90 дней)

- Команда /cache



*Настройки*

Используйте settings для настройки уведомлений



*Справка*

help - полный список команд";



        var keyboard = new InlineKeyboardMarkup(new[]

        {

            new[]

            {

                InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "/start"),

                InlineKeyboardButton.WithCallbackData("❓ Справка", "/help"),

            }

        });



        await ShowNavScreenAsync(chatId, message, keyboard);

    }
}
