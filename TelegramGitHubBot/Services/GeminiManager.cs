using System.Text.Json;

namespace TelegramGitHubBot.Services;

public class ChatMessage
{
    public string Role { get; set; } = ""; // "user" или "assistant"
    public string Content { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class GeminiManager
{
    private readonly List<GeminiAgent> _agents = new();
    private readonly HttpClient _httpClient;
    private int _currentAgentIndex = 0;
    private readonly object _lockObject = new object();
    
    // Контекст разговора по чатам
    private readonly Dictionary<long, List<ChatMessage>> _chatContexts = new();
    private const int MAX_CONTEXT_MESSAGES = 50;

    public GeminiManager(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        InitializeAgents(configuration);
    }

    private void InitializeAgents(IConfiguration configuration)
    {
        // Основной агент
        var primaryApiKey = configuration["GEMINI_API_KEY"];
        if (!string.IsNullOrEmpty(primaryApiKey))
        {
            _agents.Add(new GeminiAgent(_httpClient, primaryApiKey, "Gemini Primary"));
        }

        // Дополнительные агенты (если настроены)
        var secondaryApiKey = configuration["GEMINI_API_KEY_2"];
        if (!string.IsNullOrEmpty(secondaryApiKey))
        {
            _agents.Add(new GeminiAgent(_httpClient, secondaryApiKey, "Gemini Secondary"));
        }

        var tertiaryApiKey = configuration["GEMINI_API_KEY_3"];
        if (!string.IsNullOrEmpty(tertiaryApiKey))
        {
            _agents.Add(new GeminiAgent(_httpClient, tertiaryApiKey, "Gemini Tertiary"));
        }

        if (_agents.Count == 0)
        {
            throw new InvalidOperationException("Не настроено ни одного Gemini API ключа!");
        }
    }

    public async Task<string> GenerateResponseAsync(string prompt)
    {
        if (_agents.Count == 0)
        {
            return "❌ **Ошибка:** Не настроено ни одного AI агента!";
        }

        GeminiAgent selectedAgent;
        lock (_lockObject)
        {
            // Ищем доступного агента, начиная с текущего
            var startIndex = _currentAgentIndex;
            var attempts = 0;

            while (attempts < _agents.Count)
            {
                var agent = _agents[_currentAgentIndex];
                
                if (agent.IsAvailable)
                {
                    // Найден доступный агент, используем его
                    break;
                }

                // Переключаемся на следующего агента
                _currentAgentIndex = (_currentAgentIndex + 1) % _agents.Count;
                attempts++;
            }

            // Если все агенты недоступны, используем текущего (покажет ошибку лимитов)
            selectedAgent = _agents[_currentAgentIndex];
        }
        
        return await selectedAgent.GenerateResponseAsync(prompt);
    }

    public string GetAllAgentsStatus()
    {
        if (_agents.Count == 0)
        {
            return "❌ **Ошибка:** Не настроено ни одного AI агента!";
        }

        var status = "🤖 **Статус всех AI агентов:**\n\n";
        
        for (int i = 0; i < _agents.Count; i++)
        {
            var agent = _agents[i];
            var currentIndicator = i == _currentAgentIndex ? " 👈 **ТЕКУЩИЙ**" : "";
            status += $"**{i + 1}.** {agent.GetStatus()}{currentIndicator}\n";
        }

        var availableCount = _agents.Count(a => a.IsAvailable);
        status += $"\n📊 **Итого:** {availableCount}/{_agents.Count} агентов доступно";

        return status;
    }

    public string GetCurrentAgentStatus()
    {
        if (_agents.Count == 0)
        {
            return "❌ **Ошибка:** Не настроено ни одного AI агента!";
        }

        var currentAgent = _agents[_currentAgentIndex];
        return $"🤖 **Текущий агент:**\n\n{currentAgent.GetStatus()}";
    }

    public void SwitchToNextAgent()
    {
        lock (_lockObject)
        {
            _currentAgentIndex = (_currentAgentIndex + 1) % _agents.Count;
        }
    }

    public void SwitchToAgent(int agentIndex)
    {
        lock (_lockObject)
        {
            if (agentIndex >= 0 && agentIndex < _agents.Count)
            {
                _currentAgentIndex = agentIndex;
            }
        }
    }

    public int GetAgentsCount()
    {
        return _agents.Count;
    }

    public bool HasAvailableAgents()
    {
        return _agents.Any(a => a.IsAvailable);
    }

    // Новый метод с поддержкой контекста
    public async Task<string> GenerateResponseWithContextAsync(string prompt, long chatId)
    {
        if (_agents.Count == 0)
        {
            return "❌ **Ошибка:** Не настроено ни одного AI агента!";
        }

        // Добавляем сообщение пользователя в контекст
        AddMessageToContext(chatId, "user", prompt);

        GeminiAgent selectedAgent;
        lock (_lockObject)
        {
            // Ищем доступного агента, начиная с текущего
            var startIndex = _currentAgentIndex;
            var attempts = 0;

            while (attempts < _agents.Count)
            {
                var agent = _agents[_currentAgentIndex];
                
                if (agent.IsAvailable)
                {
                    // Найден доступный агент, используем его
                    break;
                }

                // Переключаемся на следующий агент
                _currentAgentIndex = (_currentAgentIndex + 1) % _agents.Count;
                attempts++;
            }

            // Если все агенты недоступны, используем текущего (покажет ошибку лимитов)
            selectedAgent = _agents[_currentAgentIndex];
        }
        
        // Формируем контекст для отправки
        var contextPrompt = BuildContextPrompt(chatId);
        
        var response = await selectedAgent.GenerateResponseAsync(contextPrompt);
            
            // Добавляем ответ ассистента в контекст
            if (!response.Contains("❌") && !response.Contains("⚠️"))
            {
                AddMessageToContext(chatId, "assistant", response);
            }
            
            return response;
        }
    }

    private void AddMessageToContext(long chatId, string role, string content)
    {
        if (!_chatContexts.ContainsKey(chatId))
        {
            _chatContexts[chatId] = new List<ChatMessage>();
        }

        _chatContexts[chatId].Add(new ChatMessage
        {
            Role = role,
            Content = content,
            Timestamp = DateTime.UtcNow
        });

        // Ограничиваем количество сообщений в контексте
        if (_chatContexts[chatId].Count > MAX_CONTEXT_MESSAGES)
        {
            _chatContexts[chatId] = _chatContexts[chatId]
                .TakeLast(MAX_CONTEXT_MESSAGES)
                .ToList();
        }
    }

    private string BuildContextPrompt(long chatId)
    {
        if (!_chatContexts.ContainsKey(chatId) || _chatContexts[chatId].Count == 0)
        {
            return _chatContexts[chatId].LastOrDefault()?.Content ?? "";
        }

        var context = "Контекст предыдущего разговора:\n\n";
        
        foreach (var message in _chatContexts[chatId].TakeLast(10)) // Берем последние 10 сообщений для контекста
        {
            context += $"{message.Role}: {message.Content}\n\n";
        }

        return context;
    }

    public void ClearContext(long chatId)
    {
        if (_chatContexts.ContainsKey(chatId))
        {
            _chatContexts[chatId].Clear();
        }
    }
}

