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
}
