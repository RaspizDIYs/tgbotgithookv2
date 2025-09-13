using Octokit;

namespace TelegramGitHubBot.Services;

public class GitHubService
{
    private readonly GitHubClient _client;
    private const string Owner = "RaspizDIYs";
    private const string Repo = "goodluckv2";

    public GitHubService(GitHubClient client)
    {
        _client = client;
    }

    public async Task<string> GetRepositoryStatusAsync()
    {
        try
        {
            var repository = await _client.Repository.Get(Owner, Repo);
            var branches = await _client.Repository.Branch.GetAll(Owner, Repo);
            var defaultBranch = branches.FirstOrDefault(b => b.Name == repository.DefaultBranch);

            var status = $"📊 *Статус репозитория {Owner}/{Repo}*\n\n" +
                        $"🌟 Звезды: {repository.StargazersCount}\n" +
                        $"🍴 Форки: {repository.ForksCount}\n" +
                        $"📂 Размер: {repository.Size} KB\n" +
                        $"🔧 Язык: {repository.Language}\n" +
                        $"📅 Создан: {repository.CreatedAt:dd.MM.yyyy}\n" +
                        $"🔄 Обновлен: {repository.UpdatedAt:dd.MM.yyyy}\n" +
                        $"🎯 Основная ветка: {repository.DefaultBranch}\n";

            if (defaultBranch?.Commit != null)
            {
                var latestCommit = await _client.Repository.Commit.Get(Owner, Repo, defaultBranch.Commit.Sha);
                status += $"\n📝 Последний коммит:\n" +
                         $"• SHA: `{latestCommit.Sha[..8]}`\n" +
                         $"• Автор: {latestCommit.Commit.Author.Name}\n" +
                         $"• Сообщение: {latestCommit.Commit.Message.Split('\n')[0]}\n" +
                         $"• Дата: {latestCommit.Commit.Author.Date:dd.MM.yyyy HH:mm}";
            }

            return status;
        }
        catch (Exception ex)
        {
            return $"❌ Ошибка получения статуса репозитория: {ex.Message}";
        }
    }

    public async Task<string> GetRecentCommitsAsync(string branch, int count)
    {
        try
        {
            var commits = await _client.Repository.Commit.GetAll(Owner, Repo,
                new CommitRequest { Sha = branch }, new ApiOptions { PageSize = count, PageCount = 1 });

            if (!commits.Any())
                return $"❌ В ветке '{branch}' нет коммитов или ветка не существует.";

            var result = $"📝 *Последние {Math.Min(count, commits.Count)} коммитов в ветке {branch}:*\n\n";

            foreach (var commit in commits.Take(count))
            {
                var author = commit.Commit.Author.Name;
                var message = commit.Commit.Message.Split('\n')[0];
                var date = commit.Commit.Author.Date;
                var sha = commit.Sha[..8];

                result += $"🔹 `{sha}` - {author}\n" +
                         $"   _{message}_\n" +
                         $"   📅 {date:dd.MM.yyyy HH:mm}\n\n";
            }

            return result;
        }
        catch (Exception ex)
        {
            return $"❌ Ошибка получения коммитов: {ex.Message}";
        }
    }

    public async Task<string> GetBranchesAsync()
    {
        try
        {
            var branches = await _client.Repository.Branch.GetAll(Owner, Repo);
            var repository = await _client.Repository.Get(Owner, Repo);
            var defaultBranch = repository.DefaultBranch;

            var result = $"🌿 *Ветки репозитория {Owner}/{Repo}:*\n\n";

            foreach (var branch in branches.OrderByDescending(b => b.Name == defaultBranch))
            {
                var isDefault = branch.Name == defaultBranch ? " (основная)" : "";
                var protectedBadge = branch.Protected ? "🔒" : "🔓";

                result += $"{protectedBadge} `{branch.Name}`{isDefault}\n";

                if (branch.Commit != null)
                {
                    try
                    {
                        var commit = await _client.Repository.Commit.Get(Owner, Repo, branch.Commit.Sha);
                        result += $"   📝 {commit.Commit.Author.Name}: {commit.Commit.Message.Split('\n')[0]}\n" +
                                 $"   📅 {commit.Commit.Author.Date:dd.MM.yyyy HH:mm}\n\n";
                    }
                    catch
                    {
                        result += $"   📝 Последний коммит: {branch.Commit.Sha[..8]}\n\n";
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            return $"❌ Ошибка получения веток: {ex.Message}";
        }
    }

    public async Task<string> GetPullRequestsAsync()
    {
        try
        {
            var prs = await _client.PullRequest.GetAllForRepository(Owner, Repo,
                new PullRequestRequest { State = ItemStateFilter.Open });

            if (!prs.Any())
                return "✅ *Открытых pull requests нет*";

            var result = $"🔄 *Открытые pull requests ({prs.Count}):*\n\n";

            foreach (var pr in prs.OrderByDescending(p => p.CreatedAt))
            {
                var status = pr.Draft ? "📝 Draft" : "✅ Ready";
                var reviews = pr.RequestedReviewers?.Any() == true ? $" 👀 {pr.RequestedReviewers.Count}" : "";

                result += $"#{pr.Number} {status}{reviews}\n" +
                         $"📋 *{pr.Title}*\n" +
                         $"👤 {pr.User.Login}\n" +
                         $"📊 {pr.Commits} commits, {pr.ChangedFiles} files\n" +
                         $"📅 Создан: {pr.CreatedAt:dd.MM.yyyy HH:mm}\n";

                if (!string.IsNullOrEmpty(pr.Body))
                {
                    var description = pr.Body.Length > 100 ? pr.Body[..97] + "..." : pr.Body;
                    result += $"📝 {description.Replace('\n', ' ').Replace('\r', ' ')}\n";
                }

                result += $"\n🔗 [Посмотреть PR]({pr.HtmlUrl})\n\n";
            }

            return result;
        }
        catch (Exception ex)
        {
            return $"❌ Ошибка получения pull requests: {ex.Message}";
        }
    }

    public async Task<string> GetWorkflowRunsAsync(string? branch = null, int count = 5)
    {
        try
        {
            var request = new WorkflowRunsRequest();
            if (!string.IsNullOrEmpty(branch))
                request.Branch = branch!;

            var runs = await _client.Actions.Workflows.Runs.List(Owner, Repo, request,
                new ApiOptions { PageSize = count, PageCount = 1 });

            if (!runs.WorkflowRuns.Any())
                return $"❌ CI/CD запусков не найдено для ветки '{branch ?? "все"}'";

            var result = $"⚙️ *Последние {Math.Min(count, runs.WorkflowRuns.Count)} CI/CD запусков:*\n\n";

            foreach (var run in runs.WorkflowRuns.OrderByDescending(r => r.CreatedAt))
            {
                var status = run.Status.StringValue switch
                {
                    "completed" => run.Conclusion?.StringValue switch
                    {
                        "success" => "✅",
                        "failure" => "❌",
                        "cancelled" => "🚫",
                        _ => "⚠️"
                    },
                    "in_progress" => "🔄",
                    "queued" => "⏳",
                    _ => "❓"
                };

                result += $"{status} `{run.Name}` #{run.RunNumber}\n" +
                         $"🌿 Ветка: {run.HeadBranch}\n" +
                         $"📅 {run.CreatedAt:dd.MM.yyyy HH:mm} - {run.UpdatedAt:dd.MM.yyyy HH:mm}\n" +
                         $"👤 {run.TriggeringActor.Login}\n" +
                         $"🔗 [Детали]({run.HtmlUrl})\n\n";
            }

            return result;
        }
        catch (Exception ex)
        {
            return $"❌ Ошибка получения CI/CD статусов: {ex.Message}";
        }
    }

    public async Task<string> GetCommitDetailsAsync(string commitSha)
    {
        try
        {
            var commit = await _client.Repository.Commit.Get(Owner, Repo, commitSha);

            var details = $"📋 *Детали коммита*\n\n" +
                         $"👤 Автор: {commit.Commit.Author.Name}\n" +
                         $"📅 Дата: {commit.Commit.Author.Date:dd.MM.yyyy HH:mm:ss}\n\n" +
                         $"📝 Сообщение:\n```\n{commit.Commit.Message}\n```\n";

            if (commit.Files?.Any() == true)
            {
                details += $"📁 Измененные файлы ({commit.Files.Count}):\n";

                foreach (var file in commit.Files.Take(10)) // Показываем максимум 10 файлов
                {
                    var changeType = file.Status switch
                    {
                        "added" => "🟢",
                        "modified" => "🟡",
                        "removed" => "🔴",
                        "renamed" => "🔵",
                        _ => "⚪"
                    };

                    details += $"{changeType} `{file.Filename}`\n";

                    // Проверяем, был ли файл переименован (если доступно)
                    try
                    {
                        var previousFileName = file.GetType().GetProperty("PreviousFileName")?.GetValue(file) as string;
                        if (!string.IsNullOrEmpty(previousFileName) && previousFileName != file.Filename)
                        {
                            details += $"   ↳ переименован из `{previousFileName}`\n";
                        }
                    }
                    catch
                    {
                        // Игнорируем ошибку, если свойство недоступно
                    }
                }

                if (commit.Files.Count > 10)
                {
                    details += $"... и ещё {commit.Files.Count - 10} файлов\n";
                }

                // Статистика изменений
                var additions = commit.Stats?.Additions ?? 0;
                var deletions = commit.Stats?.Deletions ?? 0;
                var totalChanges = commit.Stats?.Total ?? 0;

                details += $"\n📊 Статистика:\n" +
                          $"➕ Добавлено: {additions} строк\n" +
                          $"➖ Удалено: {deletions} строк\n" +
                          $"📈 Всего изменений: {totalChanges} строк\n";
            }

            if (commit.Parents?.Any() == true)
            {
                details += $"\n👨‍👩‍👧‍👦 Родительские коммиты:\n";
                foreach (var parent in commit.Parents.Take(3))
                {
                    details += $"• `{parent.Sha[..8]}`\n";
                }
            }

            details += $"\n🔗 [Посмотреть на GitHub]({commit.HtmlUrl})";

            return details;
        }
        catch (Exception ex)
        {
            return $"❌ Ошибка получения деталей коммита {commitSha}: {ex.Message}";
        }
    }

    public class AuthorStats
    {
        public int Commits { get; set; }
        public int Additions { get; set; }
        public int Deletions { get; set; }
        public int TotalChanges => Additions + Deletions;
    }

    public async Task<(Dictionary<string, int> BranchStats, Dictionary<string, AuthorStats> AuthorStats)> GetDailyCommitStatsAsync()
    {
        try
        {
            var branches = await _client.Repository.Branch.GetAll(Owner, Repo);
            var branchStats = new Dictionary<string, int>();
            var authorStats = new Dictionary<string, AuthorStats>();
            var yesterday = DateTime.UtcNow.AddDays(-1);
            var today = DateTime.UtcNow;

            foreach (var branch in branches)
            {
                try
                {
                    var commits = await _client.Repository.Commit.GetAll(Owner, Repo,
                        new CommitRequest { Sha = branch.Name, Since = yesterday, Until = today });

                    branchStats[branch.Name] = commits.Count;

                    // Собираем статистику по авторам с детальной информацией
                    foreach (var commit in commits)
                    {
                        var author = commit.Commit.Author.Name ?? "Неизвестен";
                        
                        if (!authorStats.ContainsKey(author))
                        {
                            authorStats[author] = new AuthorStats();
                        }

                        authorStats[author].Commits++;

                        // Получаем детальную информацию о коммите для подсчета изменений
                        try
                        {
                            var detailedCommit = await _client.Repository.Commit.Get(Owner, Repo, commit.Sha);
                            if (detailedCommit.Stats != null)
                            {
                                authorStats[author].Additions += detailedCommit.Stats.Additions;
                                authorStats[author].Deletions += detailedCommit.Stats.Deletions;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error getting detailed commit {commit.Sha}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error getting commits for branch {branch.Name}: {ex.Message}");
                    branchStats[branch.Name] = 0;
                }
            }

            return (branchStats, authorStats);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting daily commit stats: {ex.Message}");
            return (new Dictionary<string, int>(), new Dictionary<string, AuthorStats>());
        }
    }

    public async Task<(int Success, int Failure)> GetDailyWorkflowStatsAsync()
    {
        try
        {
            var yesterday = DateTime.UtcNow.AddDays(-1);
            var request = new WorkflowRunsRequest { Created = $">{yesterday.ToString("yyyy-MM-ddTHH:mm:ssZ")}" };

            var runs = await _client.Actions.Workflows.Runs.List(Owner, Repo, request);

            var successCount = runs.WorkflowRuns.Count(r => r.Status.StringValue == "completed" && r.Conclusion?.StringValue == "success");
            var failureCount = runs.WorkflowRuns.Count(r => r.Status.StringValue == "completed" && r.Conclusion?.StringValue == "failure");

            return (successCount, failureCount);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting daily workflow stats: {ex.Message}");
            return (0, 0);
        }
    }

    public async Task<List<string>> GetBranchesListAsync()
    {
        try
        {
            var branches = await _client.Repository.Branch.GetAll(Owner, Repo);
            return branches.Select(b => b.Name).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting branches list: {ex.Message}");
            return new List<string>();
        }
    }

    public async Task<string> SearchCommitsAsync(string query, int limit = 10)
    {
        try
        {
            var commits = await _client.Repository.Commit.GetAll(Owner, Repo, 
                new CommitRequest(), new ApiOptions { PageSize = 50, PageCount = 1 });

            var filteredCommits = commits
                .Where(c => c.Commit.Message.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToList();

            if (!filteredCommits.Any())
                return "";

            var result = $"🔍 *Результаты поиска '{query}':*\n\n";

            foreach (var commit in filteredCommits)
            {
                var author = commit.Commit.Author.Name;
                var message = commit.Commit.Message.Split('\n')[0];
                var date = commit.Commit.Author.Date;
                var sha = commit.Sha[..8];

                result += $"🔹 `{sha}` - {author}\n" +
                         $"   _{message}_\n" +
                         $"   📅 {date:dd.MM.yyyy HH:mm}\n\n";
            }

            return result;
        }
        catch (Exception ex)
        {
            return $"❌ Ошибка поиска: {ex.Message}";
        }
    }

    public async Task<string> GetActiveAuthorsAsync(int days = 30)
    {
        try
        {
            var since = DateTime.UtcNow.AddDays(-days);
            var commits = await _client.Repository.Commit.GetAll(Owner, Repo,
                new CommitRequest { Since = since }, new ApiOptions { PageSize = 100, PageCount = 1 });

            var authorStats = commits
                .GroupBy(c => c.Commit.Author.Name)
                .Select(g => new { Author = g.Key, Count = g.Count() })
                .OrderByDescending(a => a.Count)
                .Take(10)
                .ToList();

            if (!authorStats.Any())
                return "👥 *Активных авторов не найдено*";

            var result = $"👥 *Активные авторы за {days} дней:*\n\n";

            foreach (var author in authorStats)
            {
                result += $"👤 {author.Author}: {author.Count} коммит{(author.Count != 1 ? "ов" : "")}\n";
            }

            return result;
        }
        catch (Exception ex)
        {
            return $"❌ Ошибка получения авторов: {ex.Message}";
        }
    }

    public async Task<string> GetCommitFilesAsync(string commitSha)
    {
        try
        {
            var commit = await _client.Repository.Commit.Get(Owner, Repo, commitSha);

            if (commit.Files?.Any() != true)
                return $"📁 *Файлы в коммите {commitSha[..8]}:*\n\nФайлы не найдены";

            var result = $"📁 *Файлы в коммите {commitSha[..8]}:*\n\n";

            foreach (var file in commit.Files.Take(15))
            {
                var changeType = file.Status switch
                {
                    "added" => "🟢 Добавлен",
                    "modified" => "🟡 Изменен",
                    "removed" => "🔴 Удален",
                    "renamed" => "🔵 Переименован",
                    _ => "⚪ Изменен"
                };

                result += $"{changeType}: `{file.Filename}`\n";
                
                if (file.Additions > 0 || file.Deletions > 0)
                {
                    result += $"   📊 +{file.Additions} -{file.Deletions}\n";
                }
                result += "\n";
            }

            if (commit.Files.Count > 15)
            {
                result += $"... и ещё {commit.Files.Count - 15} файлов\n";
            }

            return result;
        }
        catch (Exception ex)
        {
            return $"❌ Ошибка получения файлов коммита: {ex.Message}";
        }
    }
}
