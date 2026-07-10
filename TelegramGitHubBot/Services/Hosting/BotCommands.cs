using Telegram.Bot.Types;

namespace TelegramGitHubBot.Services.Hosting;

/// <summary>
/// Single source of truth for the Telegram command menu (shown in the client UI).
/// Keep this list in sync with the switch in <see cref="TelegramBotService"/> and /help.
/// </summary>
public static class BotCommands
{
    public static readonly BotCommand[] Menu =
    {
        new() { Command = "start", Description = "Главное меню" },
        new() { Command = "help", Description = "Справка по командам" },
        new() { Command = "info", Description = "Подробная информация" },
        new() { Command = "status", Description = "Статус репозитория" },
        new() { Command = "commits", Description = "Коммиты [ветка] [кол-во]" },
        new() { Command = "branches", Description = "Список веток" },
        new() { Command = "prs", Description = "Открытые Pull Request" },
        new() { Command = "ci", Description = "CI/CD статус [ветка]" },
        new() { Command = "deploy", Description = "Деплой [среда]" },
        new() { Command = "search", Description = "Поиск по коммитам" },
        new() { Command = "authors", Description = "Активные авторы" },
        new() { Command = "files", Description = "Файлы в коммите <sha>" },
        new() { Command = "ratelimit", Description = "Лимиты GitHub API" },
        new() { Command = "cache", Description = "Информация о кэше" },
        new() { Command = "protection", Description = "Защита веток" },
        new() { Command = "backup", Description = "Резервное копирование" },
        new() { Command = "laststats", Description = "Последняя статистика" },
        new() { Command = "weekstats", Description = "Статистика по неделям" },
        new() { Command = "rating", Description = "Рейтинг разработчиков" },
        new() { Command = "trends", Description = "Тренды активности" },
        new() { Command = "achievements", Description = "Список ачивок" },
        new() { Command = "leaderboard", Description = "Таблица лидеров" },
        new() { Command = "streaks", Description = "Топ стриков" },
        new() { Command = "recalc", Description = "Пересчёт статистики" },
        new() { Command = "jira", Description = "Быстрый дайджест задач Jira (KAN) по ролям" },
        new() { Command = "digest", Description = "Утренний дайджест-план (Jira срезы + GlitchTip + ИИ)" },
        new() { Command = "glaistart", Description = "Включить режим AI" },
        new() { Command = "glaistop", Description = "Выключить режим AI" },
        new() { Command = "glaistats", Description = "Статус всех AI-агентов" },
        new() { Command = "glaicurrent", Description = "Текущий AI-агент" },
        new() { Command = "glaiswitch", Description = "Переключить AI-агента" },
        new() { Command = "glaiclear", Description = "Очистить контекст AI" },
        new() { Command = "ask", Description = "Вопрос к AI (может смотреть коммиты/PR/CI/статистику)" },
        new() { Command = "tldr", Description = "Краткая выжимка обсуждения" },
        new() { Command = "settings", Description = "Настройки уведомлений" },
    };
}
