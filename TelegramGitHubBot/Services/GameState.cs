namespace TelegramGitHubBot.Services;

public class GameState
{
    public bool IsActive { get; set; } = false;
    public string GameType { get; set; } = ""; // "meme", "lol", "programming"
    public string Difficulty { get; set; } = "medium"; // "easy", "medium", "hard", "expert"
    public int CurrentQuestion { get; set; } = 0;
    public int CorrectAnswers { get; set; } = 0;
    public int WrongAnswers { get; set; } = 0;
    public bool HasUsedLifeline { get; set; } = false; // Одна попытка ошибиться
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public string CurrentQuestionText { get; set; } = "";
    public List<string> CurrentOptions { get; set; } = new();
    public int CorrectAnswerIndex { get; set; } = 0;
}

public abstract class BaseGamePrompt
{
    public abstract string GameType { get; }
    public abstract string GameName { get; }
    
    public abstract string GetPrompt(string difficulty);
    
    protected string GetDifficultyDescription(string difficulty)
    {
        return difficulty switch
        {
            "easy" => "Легкие вопросы - базовые знания, популярные мемы/факты",
            "medium" => "Средние вопросы - требуют некоторого опыта",
            "hard" => "Сложные вопросы - для опытных игроков",
            "expert" => "Экспертные вопросы - очень сложные, для знатоков",
            _ => "Средние вопросы - требуют некоторого опыта"
        };
    }
}

public class MemeGamePrompt : BaseGamePrompt
{
    public override string GameType => "meme";
    public override string GameName => "Что? Где? Мем?";
    
    public override string GetPrompt(string difficulty)
    {
        var basePrompt = @"You are the host of a ""What? Where? When?"" quiz game about Russian internet memes.

IMPORTANT: All questions, answers, and responses must be in RUSSIAN language only!

RULES:
- 10 questions with 4 answer options each
- Player has ONLY 1 lifeline (can make 1 mistake, then game ends)
- Only real popular Russian memes, no fictional ones
- Questions MUST be strictly within the topic of Russian internet memes
- Correct answer is NEVER shown in the question - only revealed after player's response
- EVERY question MUST include a GIF from Tenor API related to the meme being asked about

RESPONSE FORMAT (MUST be in Russian):
Вопрос: [question text]
A) [option 1]
B) [option 2] 
C) [option 3]
D) [option 4]

Правильный ответ: [letter] - [answer text]

CRITICAL: Do NOT include the correct answer in the question text itself!

Start with the first question. Remember: everything must be in Russian!";

        return $@"{basePrompt}

CURRENT DIFFICULTY LEVEL: {GetDifficultyDescription(difficulty)}
Adjust question complexity accordingly while staying strictly within the topic of Russian internet memes.";
    }
}

public class LoLGamePrompt : BaseGamePrompt
{
    public override string GameType => "lol";
    public override string GameName => "Что? Где? Лол?";
    
    public override string GetPrompt(string difficulty)
    {
        var basePrompt = @"You are the host of a ""What? Where? When?"" quiz game about League of Legends.

IMPORTANT: All questions, answers, and responses must be in RUSSIAN language only!

RULES:
- 10 questions with 4 answer options each
- Player has ONLY 1 lifeline (can make 1 mistake, then game ends)
- Only real facts about LoL, no fictional information
- Questions MUST be strictly within the topic of League of Legends
- Correct answer is NEVER shown in the question - only revealed after player's response

RESPONSE FORMAT (MUST be in Russian):
Вопрос: [question text]
A) [option 1]
B) [option 2] 
C) [option 3]
D) [option 4]

Правильный ответ: [letter] - [answer text]

CRITICAL: Do NOT include the correct answer in the question text itself!

Start with the first question. Remember: everything must be in Russian!";

        return $@"{basePrompt}

CURRENT DIFFICULTY LEVEL: {GetDifficultyDescription(difficulty)}
Adjust question complexity accordingly while staying strictly within the topic of League of Legends.";
    }
}

public class ProgrammingGamePrompt : BaseGamePrompt
{
    public override string GameType => "programming";
    public override string GameName => "If? Else? True?";
    
    public override string GetPrompt(string difficulty)
    {
        var basePrompt = @"You are the host of a ""What? Where? When?"" quiz game about programming.

IMPORTANT: All questions, answers, and responses must be in RUSSIAN language only!

RULES:
- 10 questions with 4 answer options each
- Player has ONLY 1 lifeline (can make 1 mistake, then game ends)
- Not very complex programming questions, accessible to beginners
- Questions MUST be strictly within the topic of programming
- Correct answer is NEVER shown in the question - only revealed after player's response

RESPONSE FORMAT (MUST be in Russian):
Вопрос: [question text]
A) [option 1]
B) [option 2] 
C) [option 3]
D) [option 4]

Правильный ответ: [letter] - [answer text]

CRITICAL: Do NOT include the correct answer in the question text itself!

Start with the first question. Remember: everything must be in Russian!";

        return $@"{basePrompt}

CURRENT DIFFICULTY LEVEL: {GetDifficultyDescription(difficulty)}
Adjust question complexity accordingly while staying strictly within the topic of programming.";
    }
}

public class GamePrompts
{
    private static readonly Dictionary<string, BaseGamePrompt> _prompts = new()
    {
        ["meme"] = new MemeGamePrompt(),
        ["lol"] = new LoLGamePrompt(),
        ["programming"] = new ProgrammingGamePrompt()
    };

    public static string GetPromptWithDifficulty(string gameType, string difficulty)
    {
        return _prompts.TryGetValue(gameType, out var prompt) 
            ? prompt.GetPrompt(difficulty) 
            : "";
    }

    public static string GetGameName(string gameType)
    {
        return _prompts.TryGetValue(gameType, out var prompt) 
            ? prompt.GameName 
            : "Неизвестная игра";
    }
}
