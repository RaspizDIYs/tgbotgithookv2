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

public class GamePrompts
{
    public static readonly Dictionary<string, string> Prompts = new()
    {
        ["meme"] = @"You are the host of a ""What? Where? When?"" quiz game about Russian internet memes.

IMPORTANT: All questions, answers, and responses must be in RUSSIAN language only!

RULES:
- 10 questions with 4 answer options each
- Difficulty progression: 3 easy → 3 medium → 3 hard → 1 very hard
- Player has 1 lifeline (can make 1 mistake)
- Only real popular Russian memes, no fictional ones

RESPONSE FORMAT (MUST be in Russian):
Вопрос: [question text]
A) [option 1]
B) [option 2] 
C) [option 3]
D) [option 4]

Правильный ответ: [letter] - [answer text]

Start with the first easy question. Remember: everything must be in Russian!",

        ["lol"] = @"You are the host of a ""What? Where? When?"" quiz game about League of Legends.

IMPORTANT: All questions, answers, and responses must be in RUSSIAN language only!

RULES:
- 10 questions with 4 answer options each
- Difficulty progression: 3 easy → 3 medium → 3 hard → 1 very hard
- Player has 1 lifeline (can make 1 mistake)
- Only real facts about LoL, no fictional information

RESPONSE FORMAT (MUST be in Russian):
Вопрос: [question text]
A) [option 1]
B) [option 2] 
C) [option 3]
D) [option 4]

Правильный ответ: [letter] - [answer text]

Start with the first easy question. Remember: everything must be in Russian!",

        ["programming"] = @"You are the host of a ""What? Where? When?"" quiz game about programming.

IMPORTANT: All questions, answers, and responses must be in RUSSIAN language only!

RULES:
- 10 questions with 4 answer options each
- Difficulty progression: 3 easy → 3 medium → 3 hard → 1 very hard
- Player has 1 lifeline (can make 1 mistake)
- Not very complex programming questions, accessible to beginners

RESPONSE FORMAT (MUST be in Russian):
Вопрос: [question text]
A) [option 1]
B) [option 2] 
C) [option 3]
D) [option 4]

Правильный ответ: [letter] - [answer text]

Start with the first easy question. Remember: everything must be in Russian!"
    };

    public static readonly Dictionary<string, string> GameNames = new()
    {
        ["meme"] = "Что? Где? Мем?",
        ["lol"] = "Что? Где? Лол?",
        ["programming"] = "If? Else? True?"
    };

    public static string GetPromptWithDifficulty(string gameType, string difficulty)
    {
        var basePrompt = Prompts[gameType];
        var difficultyDescription = GetDifficultyDescription(difficulty);
        
        return basePrompt.Replace(
            "Difficulty progression: 3 easy → 3 medium → 3 hard → 1 very hard",
            $"Difficulty level: {difficultyDescription}"
        );
    }

    private static string GetDifficultyDescription(string difficulty)
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
