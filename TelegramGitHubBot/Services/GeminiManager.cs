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

    // Альтернативный провайдер: локальная Ollama (если LLM_PROVIDER=ollama).
    // Когда задан — все запросы генерации делегируются сюда, Gemini не используется.
    private readonly OllamaProvider? _ollama;

    // Контекст разговора по чатам
    private readonly Dictionary<long, List<ChatMessage>> _chatContexts = new();
    private const int MAX_CONTEXT_MESSAGES = 50;

    public GeminiManager(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;

        var provider = (configuration["LLM_PROVIDER"] ?? "gemini").ToLowerInvariant();
        if (provider == "ollama")
        {
            _ollama = new OllamaProvider(httpClient, configuration);
            Console.WriteLine("🦙 LLM провайдер: Ollama (Gemini отключён)");
            return; // Gemini-агенты не инициализируем
        }

        InitializeAgents(configuration);
    }

    private void InitializeAgents(IConfiguration configuration)
    {
        // Основной агент
        var primaryApiKey = configuration["GEMINI_API_KEY"];
        Console.WriteLine($"🔑 GEMINI_API_KEY: {(string.IsNullOrEmpty(primaryApiKey) ? "НЕ НАЙДЕН" : $"{primaryApiKey.Substring(0, Math.Min(10, primaryApiKey.Length))}...")}");
        
        if (!string.IsNullOrEmpty(primaryApiKey))
        {
            if (primaryApiKey.Length < 20)
            {
                Console.WriteLine($"⚠️ ПРЕДУПРЕЖДЕНИЕ: GEMINI_API_KEY слишком короткий ({primaryApiKey.Length} символов). Возможно, ключ неполный!");
            }
            _agents.Add(new GeminiAgent(_httpClient, primaryApiKey, "Gemini Primary"));
        }
        else
        {
            Console.WriteLine("❌ ОШИБКА: GEMINI_API_KEY не найден! Проверь переменные окружения.");
        }

        // Дополнительные агенты (если настроены)
        var secondaryApiKey = configuration["GEMINI_API_KEY_2"];
        Console.WriteLine($"🔑 GEMINI_API_KEY_2: {(string.IsNullOrEmpty(secondaryApiKey) ? "НЕ НАЙДЕН" : $"{secondaryApiKey.Substring(0, Math.Min(10, secondaryApiKey.Length))}...")}");
        
        if (!string.IsNullOrEmpty(secondaryApiKey))
        {
            _agents.Add(new GeminiAgent(_httpClient, secondaryApiKey, "Gemini Secondary"));
        }

        var tertiaryApiKey = configuration["GEMINI_API_KEY_3"];
        Console.WriteLine($"🔑 GEMINI_API_KEY_3: {(string.IsNullOrEmpty(tertiaryApiKey) ? "НЕ НАЙДЕН" : $"{tertiaryApiKey.Substring(0, Math.Min(10, tertiaryApiKey.Length))}...")}");
        
        if (!string.IsNullOrEmpty(tertiaryApiKey))
        {
            _agents.Add(new GeminiAgent(_httpClient, tertiaryApiKey, "Gemini Tertiary"));
        }

        Console.WriteLine($"🤖 Инициализировано агентов: {_agents.Count}");

        if (_agents.Count == 0)
        {
            throw new InvalidOperationException("Не настроено ни одного Gemini API ключа!");
        }
    }

    public async Task<string> GenerateResponseAsync(string prompt)
    {
        if (_ollama != null)
        {
            return await _ollama.GenerateResponseAsync(prompt);
        }

        if (_agents.Count == 0)
        {
            return "❌ **Ошибка:** Не настроено ни одного AI агента!";
        }

        // Пробуем всех агентов по очереди
        var attempts = 0;
        var startIndex = _currentAgentIndex;
        Exception? lastException = null;

        while (attempts < _agents.Count)
        {
            GeminiAgent currentAgent;
            lock (_lockObject)
            {
                currentAgent = _agents[_currentAgentIndex];
            }

            try
            {
                Console.WriteLine($"🔄 Попытка {attempts + 1}/{_agents.Count}: {currentAgent.Name}");
                var response = await currentAgent.GenerateResponseAsync(prompt);
                
                // Если ответ успешный, возвращаем его
                if (!response.Contains("❌") && !response.Contains("⚠️"))
                {
                    Console.WriteLine($"✅ Успешно получен ответ от {currentAgent.Name}");
                    return response;
                }
                
                // Если ответ содержит ошибку, пробуем следующего агента
                Console.WriteLine($"⚠️ {currentAgent.Name} вернул ошибку, пробуем следующего агента");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка от {currentAgent.Name}: {ex.Message}");
                lastException = ex;
            }

            // Переключаемся на следующего агента
            lock (_lockObject)
            {
                _currentAgentIndex = (_currentAgentIndex + 1) % _agents.Count;
            }
            attempts++;
        }

        // Если все агенты не сработали, возвращаем ошибку
        var errorMessage = $"❌ **Все AI агенты недоступны!**\n\n";
        errorMessage += $"🔄 Попробовано агентов: {attempts}\n";
        errorMessage += $"📊 Доступно агентов: {_agents.Count(a => a.IsAvailable)}/{_agents.Count}\n";
        
        if (lastException != null)
        {
            errorMessage += $"🔍 Последняя ошибка: {lastException.Message}";
        }

        return errorMessage;
    }

    public string GetAllAgentsStatus()
    {
        if (_ollama != null)
        {
            return _ollama.GetStatus();
        }

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
        if (_ollama != null)
        {
            return _ollama.GetStatus();
        }

        if (_agents.Count == 0)
        {
            return "❌ **Ошибка:** Не настроено ни одного AI агента!";
        }

        var currentAgent = _agents[_currentAgentIndex];
        return $"🤖 **Текущий агент:**\n\n{currentAgent.GetStatus()}";
    }

    public void SwitchToNextAgent()
    {
        if (_ollama != null) return;
        lock (_lockObject)
        {
            _currentAgentIndex = (_currentAgentIndex + 1) % _agents.Count;
            Console.WriteLine($"🔄 Переключение на агента: {_agents[_currentAgentIndex].Name}");
        }
    }

    public void SwitchToAgent(int agentIndex)
    {
        if (_ollama != null) return;
        lock (_lockObject)
        {
            if (agentIndex >= 0 && agentIndex < _agents.Count)
            {
                _currentAgentIndex = agentIndex;
                Console.WriteLine($"🔄 Переключение на агента: {_agents[_currentAgentIndex].Name}");
            }
        }
    }

    public void SwitchToWorkingAgent()
    {
        if (_ollama != null) return;
        lock (_lockObject)
        {
            var workingAgent = _agents.FirstOrDefault(a => a.IsAvailable);
            if (workingAgent != null)
            {
                _currentAgentIndex = _agents.IndexOf(workingAgent);
                Console.WriteLine($"🔄 Переключение на рабочий агент: {workingAgent.Name}");
            }
            else
            {
                Console.WriteLine("⚠️ Нет доступных агентов для переключения");
            }
        }
    }

    public int GetAgentsCount()
    {
        return _agents.Count;
    }

    public bool HasAvailableAgents()
    {
        if (_ollama != null) return true;
        return _agents.Any(a => a.IsAvailable);
    }

    // Новый метод с поддержкой контекста
    public async Task<string> GenerateResponseWithContextAsync(string prompt, long chatId)
    {
        if (_ollama != null)
        {
            return await _ollama.GenerateResponseWithContextAsync(prompt, chatId);
        }

        if (_agents.Count == 0)
        {
            return "❌ **Ошибка:** Не настроено ни одного AI агента!";
        }

        // Добавляем сообщение пользователя в контекст
        AddMessageToContext(chatId, "user", prompt);

        // Пробуем всех агентов по очереди
        var attempts = 0;
        Exception? lastException = null;

        while (attempts < _agents.Count)
        {
            GeminiAgent currentAgent;
            lock (_lockObject)
            {
                currentAgent = _agents[_currentAgentIndex];
            }

            try
            {
                Console.WriteLine($"🔄 Попытка {attempts + 1}/{_agents.Count}: {currentAgent.Name} (с контекстом)");
                
                // Формируем контекст для отправки
                var contextPrompt = BuildContextPrompt(chatId);
                var response = await currentAgent.GenerateResponseAsync(contextPrompt);
                
                // Если ответ успешный, добавляем в контекст и возвращаем
                if (!response.Contains("❌") && !response.Contains("⚠️"))
                {
                    Console.WriteLine($"✅ Успешно получен ответ от {currentAgent.Name}");
                    AddMessageToContext(chatId, "assistant", response);
                    return response;
                }
                
                // Если ответ содержит ошибку, пробуем следующего агента
                Console.WriteLine($"⚠️ {currentAgent.Name} вернул ошибку, пробуем следующего агента");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка от {currentAgent.Name}: {ex.Message}");
                lastException = ex;
            }

            // Переключаемся на следующего агента
            lock (_lockObject)
            {
                _currentAgentIndex = (_currentAgentIndex + 1) % _agents.Count;
            }
            attempts++;
        }

        // Если все агенты не сработали, возвращаем ошибку
        var errorMessage = $"❌ **Все AI агенты недоступны!**\n\n";
        errorMessage += $"🔄 Попробовано агентов: {attempts}\n";
        errorMessage += $"📊 Доступно агентов: {_agents.Count(a => a.IsAvailable)}/{_agents.Count}\n";
        
        if (lastException != null)
        {
            errorMessage += $"🔍 Последняя ошибка: {lastException.Message}";
        }

        return errorMessage;
    }

    private void AddMessageToContext(long chatId, string role, string content)
    {
        lock (_lockObject)
        {
            if (!_chatContexts.TryGetValue(chatId, out var list))
            {
                list = new List<ChatMessage>();
                _chatContexts[chatId] = list;
            }

            list.Add(new ChatMessage
            {
                Role = role,
                Content = content,
                Timestamp = DateTime.UtcNow
            });

            // Ограничиваем количество сообщений в контексте
            if (list.Count > MAX_CONTEXT_MESSAGES)
            {
                _chatContexts[chatId] = list.TakeLast(MAX_CONTEXT_MESSAGES).ToList();
            }
        }
    }

    private string BuildContextPrompt(long chatId)
    {
        lock (_lockObject)
        {
            if (!_chatContexts.TryGetValue(chatId, out var list) || list.Count == 0)
            {
                return ""; // Нет контекста — раньше здесь был KeyNotFoundException.
            }

            var context = "Контекст предыдущего разговора:\n\n";
            foreach (var message in list.TakeLast(10)) // последние 10 сообщений
            {
                context += $"{message.Role}: {message.Content}\n\n";
            }
            return context;
        }
    }

    public void ClearContext(long chatId)
    {
        if (_ollama != null)
        {
            _ollama.ClearContext(chatId);
            return;
        }

        lock (_lockObject)
        {
            if (_chatContexts.TryGetValue(chatId, out var list))
            {
                list.Clear();
            }
        }
    }

    public int GetTotalRequests()
    {
        if (_ollama != null)
        {
            return _ollama.GetTotalRequests();
        }

        lock (_lockObject)
        {
            return _agents.Sum(agent => agent.GetRequestCount());
        }
    }
}