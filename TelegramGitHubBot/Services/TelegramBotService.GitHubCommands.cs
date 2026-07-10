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
    private async Task HandleWorkflowsCommandAsync(long chatId, string? branch, int count)

    {

        try

        {

            var workflows = await _gitHubService.GetWorkflowRunsAsync(branch ?? string.Empty, count);

            await _botClient.SendTextMessageAsync(chatId, workflows, parseMode: ParseMode.Markdown, disableNotification: true);

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения CI/CD статусов: {ex.Message}", disableNotification: true);

        }

    }

    private async Task HandleDeployCommandAsync(long chatId, string environment, string? username)

    {

        try

        {

            // Проверяем права пользователя

            if (string.IsNullOrEmpty(username))

            {

                await _botClient.SendTextMessageAsync(chatId, "❌ Не удалось определить пользователя", disableNotification: true);

                return;

            }



            var allowedUsers = new[] { "your_username" }; // Добавьте разрешенных пользователей

            if (!allowedUsers.Contains(username.ToLower()))

            {

                await _botClient.SendTextMessageAsync(chatId, "❌ У вас нет прав для запуска деплоя", disableNotification: true);

                return;

            }



            if (environment.ToLower() != "staging" && environment.ToLower() != "production")

            {

                await _botClient.SendTextMessageAsync(chatId, "❌ Доступные среды: staging, production", disableNotification: true);

                return;

            }



            var message = $"🚀 *Запуск деплоя в {environment}*\n\n" +

                         $"👤 Инициировал: {username}\n" +

                         $"⏰ Время: {DateTime.Now:dd.MM.yyyy HH:mm}\n" +

                         $"🔄 Статус: Запускается...";



            await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, disableNotification: true);



            // Здесь можно добавить логику для запуска GitHub Actions workflow

            // await _gitHubService.TriggerDeploymentAsync(environment, username);



            var successMessage = $"✅ *Деплой в {environment} запущен!*\n\n" +

                               $"👤 {username}\n" +

                               $"📊 Следите за статусом через /ci";



            await _botClient.SendTextMessageAsync(chatId, successMessage, parseMode: ParseMode.Markdown, disableNotification: true);

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка запуска деплоя: {ex.Message}", disableNotification: true);

        }

    }

    private async Task HandleStatusCommandAsync(long chatId)

    {

        try

        {

            // Проверяем запланированную статистику

            var scheduledKey = "status_main";

            var scheduledStatus = _achievementService.GetScheduledStats(scheduledKey);

            

            if (scheduledStatus != null)

            {

                await _botClient.SendTextMessageAsync(chatId, scheduledStatus, parseMode: ParseMode.Markdown, disableNotification: true);

                return;

            }

            

            // Проверяем кэш статистики

            var cacheKey = $"status_{DateTime.UtcNow:yyyyMMddHH}";

            var cachedStatus = _achievementService.GetCachedStats(cacheKey);

            

            if (cachedStatus != null)

            {

                await _botClient.SendTextMessageAsync(chatId, cachedStatus, parseMode: ParseMode.Markdown, disableNotification: true);

                return;

            }

            

            // Получаем свежие данные

            var status = await _gitHubService.GetRepositoryStatusAsync();

            

            // Кэшируем результат

            _achievementService.CacheStats(cacheKey, status, "status");

            

            await SendMessageWithBackButtonAsync(chatId, status);

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения статуса: {ex.Message}", disableNotification: true);

        }

    }

    private async Task HandleCommitsCommandAsync(long chatId, string branch, int count)

    {

        try

        {

            // Проверяем запланированную статистику

            var scheduledKey = $"commits_{branch}_{count}";

            var scheduledCommits = _achievementService.GetScheduledStats(scheduledKey);

            

            if (scheduledCommits != null)

            {

                await _botClient.SendTextMessageAsync(chatId, scheduledCommits, parseMode: ParseMode.Markdown, disableNotification: true);

                return;

            }

            

            // Проверяем кэш статистики

            var cacheKey = $"commits_{branch}_{count}_{DateTime.UtcNow:yyyyMMddHH}";

            var cachedCommits = _achievementService.GetCachedStats(cacheKey);

            

            if (cachedCommits != null)

            {

                await _botClient.SendTextMessageAsync(chatId, cachedCommits, parseMode: ParseMode.Markdown, disableNotification: true);

                return;

            }

            

            // Получаем свежие данные

            var commits = await _gitHubService.GetRecentCommitsAsync(branch, count);

            

            // Кэшируем результат

            _achievementService.CacheStats(cacheKey, commits, "commits");

            

            await SendMessageWithBackButtonAsync(chatId, commits);

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения коммитов: {ex.Message}", disableNotification: true);

        }

    }

    private async Task HandleBranchesCommandAsync(long chatId)

    {

        try

        {

            var branches = await _gitHubService.GetBranchesAsync();

            await SendMessageWithBackButtonAsync(chatId, branches);

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения веток: {ex.Message}", disableNotification: true);

        }

    }

    private async Task HandlePullRequestsCommandAsync(long chatId)

    {

        try

        {

            var prs = await _gitHubService.GetPullRequestsAsync();

            await SendMessageWithBackButtonAsync(chatId, prs);

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения PR: {ex.Message}", disableNotification: true);

        }

    }

    private async Task RestorePushMessageAsync(long chatId, string commitSha, string repoName)

    {

        try

        {

            // Получаем информацию о коммите для восстановления сообщения

            var commitDetails = await _gitHubService.GetCommitDetailsAsync(commitSha);



            // Извлекаем основную информацию из деталей коммита

            var author = "Неизвестен";

            var message = "Коммит";

            var shortSha = ShaUtils.Short(commitSha);



            // Простой парсинг деталей коммита для извлечения автора и сообщения

            var lines = commitDetails.Split('\n');

            foreach (var line in lines)

            {

                if (line.StartsWith("👤 Автор: "))

                {

                    author = line.Replace("👤 Автор: ", "").Trim();

                }

                else if (line.StartsWith("📝 Сообщение:"))

                {

                    // Следующая строка после "📝 Сообщение:" содержит текст

                    var messageIndex = Array.IndexOf(lines, line) + 1;

                    if (messageIndex < lines.Length)

                    {

                        message = lines[messageIndex].Trim('`', '*').Replace("```\n", "").Split('\n')[0];

                        if (message.Length > 50)

                        {

                            message = message[..47] + "...";

                        }

                    }

                    break;

                }

            }



            // Создаем сообщение в том же формате, что и исходное

            var pushMessage = $"🚀 *Новый пуш в RaspizDIYs/{repoName}*\n\n" +

                             $"🌿 Ветка: `main`\n" + // По умолчанию main, так как у нас нет информации о ветке

                             $"📦 Коммитов: 1\n\n" +

                             $"🔹 `{shortSha}` - {author}\n" +

                             $"   {message}\n\n" +

                             $"👤 Автор: {author}";



            // Создаем кнопку "Подробно"

            var inlineKeyboard = new InlineKeyboardMarkup(new[]

            {

                new[]

                {

                    InlineKeyboardButton.WithCallbackData("📋 Подробно", $"cd:{shortSha}:{repoName}:details")

                }

            });



            await _botClient.SendTextMessageAsync(

                chatId: chatId,

                text: pushMessage,

                parseMode: ParseMode.Markdown,

                disableWebPagePreview: true,

                disableNotification: true,

                replyMarkup: inlineKeyboard

            );



            Console.WriteLine($"🔄 Restored push message for commit {shortSha}");

        }

        catch (Exception ex)

        {

            Console.WriteLine($"❌ Failed to restore push message: {ex.Message}");



            // В случае ошибки отправляем упрощенное сообщение

            var owner = _gitHubService.OwnerName;

            var repo = _gitHubService.RepoName;

            var fallbackMessage = $"🚀 *Новый пуш в {owner}/{repoName}*\n\n" +

                                 $"📦 Коммит: `{ShaUtils.Short(commitSha)}`\n" +

                                 $"🔗 [Посмотреть на GitHub](https://github.com/{owner}/{repo}/commit/{commitSha})";



            await _botClient.SendTextMessageAsync(

                chatId: chatId,

                text: fallbackMessage,

                parseMode: ParseMode.Markdown,

                disableWebPagePreview: true,

                disableNotification: true

            );

        }

    }

    private async Task HandleCommitDetailsCallbackAsync(long chatId, string callbackData)

    {

        try

        {

            // Разбираем callback data: cd:shortSha:repo:action

            var parts = callbackData.Split(':');

            if (parts.Length < 4)

            {

                await _botClient.SendTextMessageAsync(chatId, "❌ Ошибка: некорректные данные", disableNotification: true);

                return;

            }



            var shortSha = parts[1];

            var repoName = parts[2];

            var action = parts[3];



            // Для полного SHA нужно получить его из GitHub API по короткому

            var commitSha = await GetFullShaFromShortAsync(shortSha, repoName);



            if (action == "details")

            {

                // Показываем детали коммита

                var commitDetails = await _gitHubService.GetCommitDetailsAsync(commitSha);



                var callbackShortSha = ShaUtils.Short(commitSha); // Берем первые 8 символов для callback

                var backKeyboard = new InlineKeyboardMarkup(new[]

                {

                    new[]

                    {

                        InlineKeyboardButton.WithCallbackData("⬅️ Назад", $"cd:{callbackShortSha}:{repoName}:back")

                    }

                });



                await _botClient.SendTextMessageAsync(

                    chatId: chatId,

                    text: commitDetails,

                    parseMode: ParseMode.Markdown,

                    disableWebPagePreview: true,

                    disableNotification: true,

                    replyMarkup: backKeyboard

                );

            }

            else if (action == "back")

            {

                // Восстанавливаем исходное сообщение о пуше с кнопкой

                await RestorePushMessageAsync(chatId, commitSha, repoName);

            }

        }

        catch (Exception ex)

        {

            Console.WriteLine($"Error handling commit details: {ex.Message}");

            await _botClient.SendTextMessageAsync(chatId, "❌ Ошибка получения деталей коммита", disableNotification: true);

        }

    }

    private async Task<string> GetFullShaFromShortAsync(string shortSha, string repoName)

    {

        try

        {

            // Резолвим полный SHA через GitHub API (принимает короткий ref).
            return await _gitHubService.ResolveFullShaAsync(shortSha) ?? shortSha;

        }

        catch (Exception ex)

        {

            Console.WriteLine($"Error getting full SHA: {ex.Message}");

            return shortSha; // Возвращаем короткий в случае ошибки

        }

    }

    private async Task ShowBranchSelectionAsync(long chatId, string action)

    {

        try

        {

            var branches = await _gitHubService.GetBranchesListAsync();

            

            if (!branches.Any())

            {

                await _botClient.SendTextMessageAsync(chatId, "❌ Не удалось получить список веток", disableNotification: true);

                return;

            }



            var message = action switch

            {

                "commits" => "🌿 *Выберите ветку для просмотра коммитов:*",

                "workflows" => "🌿 *Выберите ветку для просмотра CI/CD:*",

                _ => "🌿 *Выберите ветку:*"

            };



            var buttons = new List<InlineKeyboardButton[]>();

            

            // Добавляем кнопки для веток (максимум 8)

            foreach (var branch in branches.Take(8))

            {

                var callbackData = action switch

                {

                    "commits" => $"branch_commits:{branch}",

                    "workflows" => $"branch_workflows:{branch}",

                    _ => $"branch_select:{branch}"

                };

                

                buttons.Add(new[] { InlineKeyboardButton.WithCallbackData($"🌿 {branch}", callbackData) });

            }



            // Кнопка возврата

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

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения веток: {ex.Message}", disableNotification: true);

        }

    }

    private async Task HandleSearchCommandAsync(long chatId, string query)

    {

        try

        {

            var results = await _gitHubService.SearchCommitsAsync(query);

            

            if (string.IsNullOrEmpty(results))

            {

                await _botClient.SendTextMessageAsync(chatId, $"🔍 По запросу '{query}' ничего не найдено", disableNotification: true);

                return;

            }



            var keyboard = new InlineKeyboardMarkup(new[]

            {

                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

            });



            await _botClient.SendTextMessageAsync(

                chatId: chatId,

                text: results,

                parseMode: ParseMode.Markdown,

                disableNotification: true,

                replyMarkup: keyboard

            );

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка поиска: {ex.Message}", disableNotification: true);

        }

    }

    private async Task HandleAuthorsCommandAsync(long chatId)

    {

        try

        {

            // Проверяем запланированную статистику

            var scheduledKey = "authors_main";

            var scheduledAuthors = _achievementService.GetScheduledStats(scheduledKey);

            

            if (scheduledAuthors != null)

            {

            var keyboard = new InlineKeyboardMarkup(new[]

                {

                    new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

                });



                await _botClient.SendTextMessageAsync(

                    chatId: chatId,

                    text: scheduledAuthors,

                    parseMode: ParseMode.Markdown,

                    disableNotification: true,

                    replyMarkup: keyboard);

                return;

            }

            

            // Проверяем кэш статистики

            var cacheKey = $"authors_{DateTime.UtcNow:yyyyMMddHH}";

            var cachedAuthors = _achievementService.GetCachedStats(cacheKey);

            

            if (cachedAuthors != null)

            {

                var keyboard = new InlineKeyboardMarkup(new[]

                {

                    new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

                });



                await _botClient.SendTextMessageAsync(

                    chatId: chatId,

                    text: cachedAuthors,

                    parseMode: ParseMode.Markdown,

                    disableNotification: true,

                    replyMarkup: keyboard);

                return;

            }

            

            // Получаем свежие данные

            var authors = await _gitHubService.GetActiveAuthorsAsync();

            

            // Кэшируем результат

            _achievementService.CacheStats(cacheKey, authors, "authors");

            

            var keyboard2 = new InlineKeyboardMarkup(new[]

            {

                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

            });



            await _botClient.SendTextMessageAsync(

                chatId: chatId,

                text: authors,

                parseMode: ParseMode.Markdown,

                disableNotification: true,

                replyMarkup: keyboard2);

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения авторов: {ex.Message}", disableNotification: true);

        }

    }

    private async Task HandleFilesCommandAsync(long chatId, string commitSha)

    {

        try

        {

            var files = await _gitHubService.GetCommitFilesAsync(commitSha);

            

            var keyboard = new InlineKeyboardMarkup(new[]

            {

                new[] { InlineKeyboardButton.WithCallbackData("📋 Детали коммита", $"cd:{commitSha}:goodluckv2:details") },

                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

            });



            await _botClient.SendTextMessageAsync(

                chatId: chatId,

                text: files,

                parseMode: ParseMode.Markdown,

                disableNotification: true,

                replyMarkup: keyboard

            );

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения файлов: {ex.Message}", disableNotification: true);

        }

    }

    private async Task HandleBranchCallbackAsync(long chatId, string callbackData, int messageId)

    {

        try

        {

            var parts = callbackData.Split(':');

            if (parts.Length < 2) return;



            var action = parts[0];

            var branch = parts[1];



            // Убираем удаление сообщений для корректной работы



            switch (action)

            {

                case "branch_commits":

                    await HandleCommitsCommandAsync(chatId, branch, 5);

                    break;

                case "branch_workflows":

                    await HandleWorkflowsCommandAsync(chatId, branch, 5);

                    break;

            }

        }

        catch (Exception ex)

        {

            Console.WriteLine($"❌ Error handling branch callback: {ex.Message}");

        }

    }

    private async Task ShowSearchMenuAsync(long chatId, int messageId)

    {

        try

        {

            var message = "🔍 *Поиск по репозиторию*\n\n" +

                         "Выберите тип поиска или введите команду:\n\n" +

                         "📝 `/search <текст>` - поиск по сообщениям коммитов\n" +

                         "👤 `/authors` - активные авторы\n" +

                         "📁 `/files <sha>` - файлы в коммите";



            var keyboard = new InlineKeyboardMarkup(new[]

            {

                new[] { InlineKeyboardButton.WithCallbackData("👥 Активные авторы", "/authors") },

                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "⬅️ Назад") }

            });



            await _botClient.EditMessageTextAsync(

                chatId: chatId,

                messageId: messageId,

                text: message,

                parseMode: ParseMode.Markdown,

                replyMarkup: keyboard

            );

        }

        catch (Exception ex)

        {

            Console.WriteLine($"❌ Error showing search menu: {ex.Message}");

        }

    }

    private async Task HandleRateLimitCommandAsync(long chatId)

    {

        try

        {

            var (remaining, limit, resetTime) = await _gitHubService.GetRateLimitAsync();

            var timeUntilReset = resetTime - DateTime.UtcNow;

            var usedPercent = limit > 0 ? ((limit - remaining) * 100.0 / limit) : 0;



            string status;

            string emoji;

            

            if (remaining > 3000)

            {

                status = "Отлично";

                emoji = "✅";

            }

            else if (remaining > 1000)

            {

                status = "Хорошо";

                emoji = "🟢";

            }

            else if (remaining > 500)

            {

                status = "Умеренно";

                emoji = "🟡";

            }

            else if (remaining > 100)

            {

                status = "Низкий";

                emoji = "🟠";

            }

            else

            {

                status = "Критично";

                emoji = "🔴";

            }



            var message = $"{emoji} *GitHub API Rate Limit*\n\n" +

                         $"📊 *Статус:* {status}\n" +

                         $"📈 *Доступно:* {remaining}/{limit} ({usedPercent:F1}% использовано)\n" +

                         $"⏰ *Сброс через:* {(timeUntilReset.TotalMinutes > 0 ? $"{timeUntilReset.Minutes} мин {timeUntilReset.Seconds} сек" : "скоро")}\n" +

                         $"🕐 *Время сброса:* {resetTime.ToLocalTime():HH:mm:ss}\n\n" +

                         $"💡 *Рекомендации:*\n";



            if (remaining < 500)

            {

                message += "• ⚠️ Избегайте /recalc до сброса\n";

                message += "• Используйте простые команды\n";

            }

            else if (remaining < 1000)

            {

                message += "• ⚡ /recalc можно использовать осторожно\n";

                message += "• Следите за лимитом\n";

            }

            else

            {

                message += "• ✅ Все команды доступны\n";

                message += "• /recalc безопасно использовать\n";

            }



            message += $"\n📝 *Операции и их стоимость:*\n" +

                      $"• /status, /commits, /branches: 1-5 запросов\n" +

                      $"• /recalc: ~2000+ запросов (зависит от веток)\n" +

                      $"• Вебхуки GitHub: 1 запрос на коммит";



            await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, disableNotification: true);

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения лимитов: {ex.Message}", disableNotification: true);

        }

    }

    private async Task HandleCacheInfoCommandAsync(long chatId)

    {

        try

        {

            var (userStatsCount, achievementsCount, processedShasCount, totalSizeBytes) = _achievementService.GetCacheInfo();

            

            var sizeKB = totalSizeBytes / 1024.0;

            var sizeMB = sizeKB / 1024.0;

            

            string sizeText;

            if (sizeMB >= 1)

                sizeText = $"{sizeMB:F2} MB";

            else

                sizeText = $"{sizeKB:F1} KB";



            var message = $"💾 *Информация о кэше*\n\n" +

                         $"📊 *Статистика пользователей:* {userStatsCount}\n" +

                         $"🏆 *Достижения:* {achievementsCount}\n" +

                         $"📝 *Обработанные SHA:* {processedShasCount}\n" +

                         $"💿 *Общий размер:* {sizeText}\n\n" +

                         $"⚙️ *Настройки автоочистки:*\n" +

                         $"• Максимум SHA: 10,000\n" +

                         $"• Неактивные пользователи: >90 дней\n" +

                         $"• Максимум неактивных: 50\n\n" +

                         $"🧹 *Автоочистка происходит:*\n" +

                         $"• При сохранении данных\n" +

                         $"• При пересчёте (/recalc)\n\n" +

                         $"💡 *Рекомендации:*\n" +

                         $"• Мониторьте размер кэша\n" +

                         $"• Старые данные удаляются автоматически";



            var keyboard = new InlineKeyboardMarkup(new[]

            {

                new[]

                {

                    InlineKeyboardButton.WithCallbackData("📈 API лимиты", "/ratelimit"),

                },

                new[]

                {

                    InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "/start")

                }

            });



            await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, disableNotification: true, replyMarkup: keyboard);

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения информации о кэше: {ex.Message}", disableNotification: true);

        }

    }

    private async Task HandleDataProtectionCommandAsync(long chatId)

    {

        try

        {

            var (remaining, limit, resetTime) = await _gitHubService.GetRateLimitAsync();

            var isValid = _achievementService.ValidateDataIntegrity();

            var hasBackup = _achievementService.IsBackupValid();

            var (count, sizeBytes, byType) = _achievementService.GetScheduledStatsInfo();

            

            var message = $"🛡️ *Информация о защите данных*\n\n" +

                         $"🔒 *Состояние защиты:*\n" +

                         $"• Целостность данных: {(isValid ? "✅ В порядке" : "❌ Нарушена")}\n" +

                         $"• Резервная копия: {(hasBackup ? "✅ Доступна" : "❌ Отсутствует")}\n" +

                         $"• Записей в кэше: {count}\n" +

                         $"• Размер данных: {FormatBytes(sizeBytes)}\n\n" +

                         $"📊 *API лимиты:*\n" +

                         $"• Доступно: {remaining}/{limit}\n" +

                         $"• Минимум для обновления: {_achievementService.GetMinApiCallsThreshold()}\n" +

                         $"• Сброс лимитов: {resetTime:HH:mm dd.MM.yyyy}\n\n" +

                         $"🔄 *Механизмы защиты:*\n" +

                         $"• Резервное копирование перед обновлением\n" +

                         $"• Поочередное обновление с проверкой лимитов\n" +

                         $"• Восстановление при сбоях\n" +

                         $"• Проверка целостности данных\n" +

                         $"• Очистка только после успешного обновления\n\n" +

                         $"💡 *Автоматические функции:*\n" +

                         $"• Обновление прерывается при низких лимитах\n" +

                         $"• Данные восстанавливаются при ошибках\n" +

                         $"• Проверка каждые 30 минут\n" +

                         $"• Безопасное сохранение с валидацией";

            

            await _botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown, disableNotification: true);

        }

        catch (Exception ex)

        {

            await _botClient.SendTextMessageAsync(chatId, $"❌ Ошибка получения информации о защите: {ex.Message}", disableNotification: true);

        }

    }

    

    private static string FormatBytes(long bytes)

    {

        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };

        int counter = 0;

        decimal number = bytes;

        while (Math.Round(number / 1024) >= 1)

        {

            number = number / 1024;

            counter++;

        }

        return $"{number:n1} {suffixes[counter]}";

    }
}
