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
    private void SetupDailySummaryTimer()

    {

        lock (_dailySummaryTimerLock)

        {

            if (_dailySummaryTimer != null)

            {

                Console.WriteLine("⏰ Daily summary timer already initialized (another instance), skipping");

                return;

            }

            _dailySummaryTimer = new System.Timers.Timer();

            _dailySummaryTimer.Elapsed += async (sender, e) =>
            {
                try { await SendDailySummaryAsync(); }
                catch (Exception ex) { Console.WriteLine($"❌ Daily summary error: {ex.Message}"); }
            };

            _dailySummaryTimer.AutoReset = false; // Отключаем автоповтор



            // Рассчитываем время до следующего запуска в 18:00 МСК

            var now = DateTime.Now;

            var mskTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");

            var nowMsk = TimeZoneInfo.ConvertTime(now, mskTimeZone);



            var nextRun = nowMsk.Date.AddHours(18);

            if (nowMsk >= nextRun)

            {

                nextRun = nextRun.AddDays(1);

            }



            var timeUntilNextRun = nextRun - nowMsk;

            _dailySummaryTimer.Interval = timeUntilNextRun.TotalMilliseconds;



            _dailySummaryTimer.Start();

            Console.WriteLine($"⏰ Daily summary timer set to run in {timeUntilNextRun.TotalHours:F1} hours");

        }

    }

    private async Task SendDailySummaryAsync(long? targetChatId = null)

    {

        try

        {

            // Получаем статистику

            var (branchStats, authorStats) = await _gitHubService.GetDailyCommitStatsAsync();

            var (workflowSuccess, workflowFailure) = await _gitHubService.GetDailyWorkflowStatsAsync();



            // Определяем Chat ID: если передан параметр - используем его, иначе получаем из конфигурации

            long chatId;

            if (targetChatId.HasValue)

            {

                chatId = targetChatId.Value;

            }

            else

            {

                var configChatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID") ??

                                  throw new InvalidOperationException("TELEGRAM_CHAT_ID not configured");



                if (!long.TryParse(configChatId, out chatId))

                {

                    Console.WriteLine("❌ Invalid TELEGRAM_CHAT_ID format");

                    return;

                }

            }



            // Формируем сообщение со сводкой с учетом МСК

            var mskTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");

            var yesterdayMsk = TimeZoneInfo.ConvertTime(DateTime.UtcNow.AddDays(-1), mskTimeZone);

            

            var title = targetChatId.HasValue

                ? $"📊 *Запрошенная сводка за {yesterdayMsk:dd.MM.yyyy}*"

                : $"📊 *Ежедневная сводка за {yesterdayMsk:dd.MM.yyyy}*";

            var message = $"{title}\n\n";



            // Статистика коммитов по веткам

            message += "📝 *Коммиты по веткам:*\n";

            var totalCommits = 0;



            foreach (var (branch, count) in branchStats.OrderByDescending(x => x.Value))

            {

                if (count > 0)

                {

                    message += $"🌿 `{branch}`: {count} коммит{(count != 1 ? "ов" : "")}\n";

                    totalCommits += count;

                }

            }



            if (totalCommits == 0)

            {

                // Если нет коммитов - показываем "выходной" с гифкой

                message = $"🍺 *Выходной! {yesterdayMsk:dd.MM.yyyy}*\n\n";

                message += "Никто не коммитил — значит отдыхаем! 🎉\n\n";

                message += "🍻 Пьём пиво и наслаждаемся жизнью!";

                

                // Пробуем отправить анимацию с Tenor (URL из переменной окружения TENOR_WEEKEND_GIF)

                var weekendGif = Environment.GetEnvironmentVariable("TENOR_WEEKEND_GIF");

                if (!string.IsNullOrWhiteSpace(weekendGif))

                {

                    try

                    {

                        await _botClient.SendAnimationAsync(

                            chatId: chatId,

                            animation: InputFile.FromUri(NormalizeTenorUrl(weekendGif)),

                            caption: message,

                            parseMode: ParseMode.Markdown,

                            disableNotification: targetChatId.HasValue

                        );

                        var weekendSummaryType = targetChatId.HasValue ? "requested" : "automatic";

                        Console.WriteLine($"✅ {weekendSummaryType} weekend summary sent to chat {chatId} (Tenor GIF)");

                    }

                    catch (Exception ex)

                    {

                        Console.WriteLine($"⚠️ Failed to send Tenor GIF: {ex.Message}. Sending text fallback.");

                        await _botClient.SendTextMessageAsync(

                            chatId: chatId,

                            text: message,

                            parseMode: ParseMode.Markdown,

                            disableNotification: targetChatId.HasValue

                        );

                    }

                }

                else

                {

                    // Fallback только текст, без внешних хостов

                    await _botClient.SendTextMessageAsync(

                        chatId: chatId,

                        text: message,

                        parseMode: ParseMode.Markdown,

                        disableNotification: targetChatId.HasValue

                    );

                    var weekendSummaryType = targetChatId.HasValue ? "requested" : "automatic";

                    Console.WriteLine($"✅ {weekendSummaryType} weekend summary sent to chat {chatId} (text only)");

                }



                // Перепланируем таймер на следующий день только для автоматических сводок

                if (_dailySummaryTimer != null && !targetChatId.HasValue)

                {

                    _dailySummaryTimer.Stop();

                    _dailySummaryTimer.Interval = 24 * 60 * 60 * 1000;

                    _dailySummaryTimer.Start();

                }

                return;

            }

            else

            {

                message += $"\n📈 *Всего коммитов:* {totalCommits}\n\n";



                // Статистика по авторам

                message += "👥 *Коммиты по авторам:*\n";

                foreach (var (author, stats) in authorStats.OrderByDescending(x => x.Value.Commits))

                {

                    var commitsText = stats.Commits == 1 ? "коммит" : "коммитов";

                    var changesText = stats.TotalChanges == 1 ? "изменение" : 

                                     stats.TotalChanges < 5 ? "изменения" : "изменений";

                    

                    message += $"👤 {author}: {stats.Commits} {commitsText}\n";

                    if (stats.TotalChanges > 0)

                    {

                        message += $"   📊 +{stats.Additions} -{stats.Deletions} ({stats.TotalChanges} {changesText})\n";

                    }

                }

                message += "\n";

            }



            // Статистика CI/CD

            message += "⚙️ *CI/CD статусы:*\n";

            if (workflowSuccess > 0 || workflowFailure > 0)

            {

                message += $"✅ Успешных: {workflowSuccess}\n";

                message += $"❌ Неудачных: {workflowFailure}\n";

                var totalWorkflows = workflowSuccess + workflowFailure;

                var successRate = totalWorkflows > 0 ? (double)workflowSuccess / totalWorkflows * 100 : 0;

                message += $"📊 Процент успеха: {successRate:F1}%\n";

            }

            else

            {

                message += "😴 CI/CD запусков не было\n";

            }



            // «Что сделали за день» — человеческим языком (тот же флаг SUMMARIZE_GITHUB_EVENTS).
            // Источник — тексты коммитов из GitHub, а не эфемерный буфер (устойчиво к рестартам Render).
            if (DigestEnabled)
            {
                try
                {
                    var commitMessages = await _gitHubService.GetDailyCommitMessagesAsync();
                    if (commitMessages.Count > 0)
                    {
                        var prompt =
                            "Ниже сообщения коммитов за день по репозиторию. Расскажи человеческим языком, " +
                            "2-4 предложения, что было сделано за день — по сути, без воды, без эмодзи, " +
                            "без списков и без тегов. Сообщения коммитов:\n" + string.Join("\n", commitMessages);
                        var prose = StripGifTags(await _geminiManager.GenerateResponseAsync(prompt));
                        if (!string.IsNullOrWhiteSpace(prose) && !prose.Contains("❌"))
                        {
                            message += $"\n🌇 *Что сделали за день:*\n{prose.Trim()}\n";
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Ошибка прозаического блока сводки: {ex.Message}");
                }
            }

            // Автоматическую сводку шлём в тему 8146 (PUSH_SUMMARY_*), запрошенную (/summary) — в чат запроса
            int? threadId = null;
            if (!targetChatId.HasValue)
            {
                var summaryChatId = Environment.GetEnvironmentVariable("PUSH_SUMMARY_CHAT_ID");
                if (!string.IsNullOrWhiteSpace(summaryChatId) && long.TryParse(summaryChatId, out var scid))
                {
                    chatId = scid;
                    threadId = int.TryParse(Environment.GetEnvironmentVariable("PUSH_SUMMARY_THREAD_ID"), out var tid) ? tid : null;
                }
            }

            await _botClient.SendTextMessageAsync(

                chatId: chatId,

                text: message,

                parseMode: ParseMode.Markdown,

                messageThreadId: threadId,

                disableNotification: targetChatId.HasValue // Без уведомления для запрошенных сводок, с уведомлением для автоматических

            );



            var summaryType = targetChatId.HasValue ? "requested" : "automatic";

            Console.WriteLine($"✅ {summaryType} summary sent to chat {chatId}");



            // Перепланируем таймер на следующий день только для автоматических сводок

            if (_dailySummaryTimer != null && !targetChatId.HasValue)

            {

                _dailySummaryTimer.Stop();

                _dailySummaryTimer.Interval = 24 * 60 * 60 * 1000; // 24 часа в миллисекундах

                _dailySummaryTimer.Start();

            }

        }

        catch (Exception ex)

        {

            Console.WriteLine($"❌ Error sending daily summary: {ex.Message}");

        }

    }

    private Task StartScheduledUpdatesTimer()

    {

        // Таймер для проверки расписания каждые 30 минут

        var scheduledTimer = new System.Timers.Timer(TimeSpan.FromMinutes(30).TotalMilliseconds);

        scheduledTimer.Elapsed += async (sender, e) => 

        {

            try

            {

                await CheckScheduledUpdates();

            }

            catch (Exception ex)

            {

                Console.WriteLine($"❌ Критическая ошибка в таймере обновлений: {ex.Message}");

            }

        };

        scheduledTimer.AutoReset = true;

        scheduledTimer.Start();

        

        Console.WriteLine("⏰ Система запланированных обновлений запущена (проверка каждые 30 минут)");

        return Task.CompletedTask;

    }

    

    // Включена ли выдача дайджеста (тот же флаг, что раньше отвечал за резюме пушей)
    private static bool DigestEnabled =>
        (Environment.GetEnvironmentVariable("SUMMARIZE_GITHUB_EVENTS") ?? "").ToLowerInvariant() is "true" or "1" or "yes";

    private async Task CheckScheduledUpdates()

    {

        try

        {

            if (!_achievementService.ShouldUpdateScheduledStats())

            {

                return;

            }

            

            Console.WriteLine("🔄 Начинаю запланированное обновление статистики...");

            

            // Проверяем API лимиты

            var (remaining, limit, resetTime) = await _gitHubService.GetRateLimitAsync();

            

            if (remaining < _achievementService.GetMinApiCallsThreshold())

            {

                Console.WriteLine($"⚠️ Пропуск запланированного обновления - мало API вызовов: {remaining}/{limit}");

                Console.WriteLine($"⏰ Следующая проверка через час или при сбросе лимитов");

                return;

            }

            

            // Создаем резервную копию перед обновлением

            _achievementService.CreateBackup();

            

            // Обновляем все статистические данные поочередно

            var success = await UpdateAllScheduledStatsSequentially();

            

            if (success)

            {

                // Отмечаем время обновления

                _achievementService.MarkScheduledUpdate();

                

                // Очищаем старые данные только после успешного обновления

                _achievementService.ClearOldScheduledStats();

                

                // Очищаем резервную копию

                _achievementService.ClearBackup();

                

                Console.WriteLine($"✅ Запланированное обновление завершено успешно");

            }

            else

            {

                // Восстанавливаем из резервной копии при сбое

                Console.WriteLine("🔄 Восстанавливаю данные из резервной копии...");

                _achievementService.RestoreFromBackup();

            }

            

            // Проверяем финальные лимиты

            var (finalRemaining, _, _) = await _gitHubService.GetRateLimitAsync();

            Console.WriteLine($"📊 API вызовов осталось: {finalRemaining}");

        }

        catch (Exception ex)

        {

            Console.WriteLine($"❌ Ошибка запланированного обновления: {ex.Message}");

            

            // Восстанавливаем из резервной копии при критической ошибке

            if (_achievementService.IsBackupValid())

            {

                Console.WriteLine("🔄 Восстанавливаю данные из резервной копии после ошибки...");

                _achievementService.RestoreFromBackup();

            }

        }

    }

    

    private async Task<bool> UpdateAllScheduledStatsSequentially()

    {

        try

        {

            var updateTasks = new List<(string key, string type, string parameters, Func<Task<string>> getData)>();

            

            var task1 = ("status_main", "status", "", (Func<Task<string>>)(() => _gitHubService.GetRepositoryStatusAsync()));

            var task2 = ("authors_main", "authors", "", (Func<Task<string>>)(() => _gitHubService.GetActiveAuthorsAsync()));

            var task3 = ("weekly_0", "weekly", "", (Func<Task<string>>)(() => _gitHubService.GetWeeklyStatsAsync()));

            var task4 = ("achievements_main", "achievements", "", (Func<Task<string>>)(() => Task.FromResult(_achievementService.GetAchievementStats())));

            var task5 = ("streaks_main", "streaks", "", (Func<Task<string>>)(() => Task.FromResult(_achievementService.GetStreaks())));

            var task6 = ("rating_main", "rating", "", (Func<Task<string>>)(() => Task.FromResult(_achievementService.GetRating())));

            var task7 = ("leaderboard_main", "leaderboard", "", (Func<Task<string>>)(() => Task.FromResult(_achievementService.GetLeaderboard())));

            

            updateTasks.Add(task1);

            updateTasks.Add(task2);

            updateTasks.Add(task3);

            updateTasks.Add(task4);

            updateTasks.Add(task5);

            updateTasks.Add(task6);

            updateTasks.Add(task7);

            

            var successCount = 0;

            var totalTasks = updateTasks.Count;

            

            foreach (var task in updateTasks)

            {

                try

                {

                    // Проверяем API лимиты перед каждым обновлением

                    var (remaining, limit, resetTime) = await _gitHubService.GetRateLimitAsync();

                    

                    if (remaining < _achievementService.GetMinApiCallsThreshold())

                    {

                        Console.WriteLine($"⚠️ Прерывание обновления - мало API вызовов: {remaining}/{limit}");

                        Console.WriteLine($"⏰ Сброс лимитов в: {resetTime:HH:mm dd.MM.yyyy}");

                        break;

                    }

                    

                    Console.WriteLine($"🔄 Обновляю {task.type}...");

                    

                    // Получаем данные

                    var data = await task.getData();

                    

                    // Проверяем, что данные не пустые

                    if (string.IsNullOrWhiteSpace(data))

                    {

                        Console.WriteLine($"⚠️ Получены пустые данные для {task.type}, пропускаю");

                        continue;

                    }

                    

                    // Безопасно сохраняем данные

                    var saved = _achievementService.SafeSaveScheduledStats(task.key, data, task.type, task.parameters);

                    

                    if (saved)

                    {

                        successCount++;

                        Console.WriteLine($"✅ {task.type} обновлен успешно");

                    }

                    else

                    {

                        Console.WriteLine($"❌ Ошибка сохранения {task.type}");

                    }

                    

                    // Небольшая пауза между обновлениями

                    await Task.Delay(1000);

                }

                catch (Exception ex)

                {

                    Console.WriteLine($"❌ Ошибка обновления {task.type}: {ex.Message}");

                }

            }

            

            // Обновляем коммиты для основных веток (если остались API вызовы)

            try

            {

                var (remaining, _, _) = await _gitHubService.GetRateLimitAsync();

                if (remaining >= _achievementService.GetMinApiCallsThreshold())

                {

                    var branches = await _gitHubService.GetBranchesListAsync();

                    foreach (var branch in branches.Take(3))

                    {

                        var commits = await _gitHubService.GetRecentCommitsAsync(branch, 10);

                        if (!string.IsNullOrWhiteSpace(commits))

                        {

                            var saved = _achievementService.SafeSaveScheduledStats($"commits_{branch}_10", commits, "commits", branch);

                            if (saved) successCount++;

                        }

                        

                        // Проверяем лимиты после каждого обновления коммитов

                        var (currentRemaining, _, _) = await _gitHubService.GetRateLimitAsync();

                        if (currentRemaining < _achievementService.GetMinApiCallsThreshold())

                        {

                            Console.WriteLine($"⚠️ Прерывание обновления коммитов - мало API вызовов: {currentRemaining}");

                            break;

                        }

                        

                        await Task.Delay(1000);

                    }

                }

            }

            catch (Exception ex)

            {

                Console.WriteLine($"❌ Ошибка обновления коммитов: {ex.Message}");

            }

            

            var success = successCount >= totalTasks * 0.7; // Считаем успешным если обновлено 70% задач

            

            Console.WriteLine($"📊 Результат обновления: {successCount}/{totalTasks} задач выполнено успешно");

            

            // Проверяем целостность данных

            var isValid = _achievementService.ValidateDataIntegrity();

            if (!isValid)

            {

                Console.WriteLine("⚠️ Обнаружены проблемы с целостностью данных");

                return false;

            }

            

            return success;

        }

        catch (Exception ex)

        {

            Console.WriteLine($"❌ Критическая ошибка поочередного обновления: {ex.Message}");

            return false;

        }

    }

    
}
