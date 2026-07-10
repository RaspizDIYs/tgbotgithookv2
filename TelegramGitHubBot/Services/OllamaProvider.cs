using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TelegramGitHubBot.Services;

/// <summary>
/// Провайдер локальной LLM через Ollama (OpenAI-совместимый API /v1/chat/completions).
/// Используется как альтернатива Gemini, когда задан LLM_PROVIDER=ollama.
/// Ходит на публичный endpoint (reverse-proxy на glprod) с Basic-авторизацией.
/// </summary>
public class OllamaProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string? _basicAuth; // формат "user:pass"
    private readonly Dictionary<long, List<ChatMessage>> _chatContexts = new();
    private const int MAX_CONTEXT_MESSAGES = 50;
    private readonly object _lockObject = new object();
    private int _requestCount = 0;

    public OllamaProvider(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _baseUrl = (configuration["OLLAMA_BASE_URL"] ?? "http://localhost:11434").TrimEnd('/');
        _model = configuration["OLLAMA_MODEL"] ?? "qwen2.5:7b";
        _basicAuth = configuration["OLLAMA_BASIC_AUTH"]; // "ollama-bot:token", опционально
        Console.WriteLine($"🦙 OllamaProvider: {_baseUrl}/v1/chat/completions (модель: {_model}, auth: {(!string.IsNullOrEmpty(_basicAuth) ? "basic" : "нет")})");
    }

    public Task<string> GenerateResponseAsync(string prompt) => GenerateResponseAsync(prompt, raw: false);

    /// <param name="raw">true — нейтральный системный промпт (для служебных вызовов, где нужен чистый JSON).</param>
    public async Task<string> GenerateResponseAsync(string prompt, bool raw)
    {
        var system = raw
            ? "Ты — точный инструментальный ассистент. Следуй инструкции буквально. Если просят вернуть только JSON — верни только JSON, без пояснений и без markdown."
            : GetSystemPrompt();
        var messages = new List<object>
        {
            new { role = "system", content = system },
            new { role = "user", content = prompt },
        };
        return await SendAsync(messages);
    }

    public async Task<string> GenerateResponseWithContextAsync(string prompt, long chatId)
    {
        AddMessageToContext(chatId, "user", prompt);

        var messages = new List<object> { new { role = "system", content = GetSystemPrompt() } };
        lock (_lockObject)
        {
            if (_chatContexts.TryGetValue(chatId, out var history))
            {
                foreach (var m in history.TakeLast(10))
                    messages.Add(new { role = m.Role, content = m.Content });
            }
        }

        var response = await SendAsync(messages);
        if (!response.Contains("❌") && !response.Contains("⚠️"))
            AddMessageToContext(chatId, "assistant", response);
        return response;
    }

    private async Task<string> SendAsync(List<object> messages)
    {
        try
        {
            var requestBody = new
            {
                model = _model,
                messages = messages.ToArray(),
                stream = false,
                temperature = 0.7,
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/chat/completions")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrEmpty(_basicAuth))
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(_basicAuth));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
            }

            var response = await _httpClient.SendAsync(request);
            lock (_lockObject) { _requestCount++; }

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                return $"❌ **Ollama ошибка:** {(int)response.StatusCode} {response.StatusCode}\n{err}";
            }

            // Явно декодируем как UTF-8: если сервер не проставил charset,
            // ReadAsStringAsync() может взять неверную кодировку и превратить
            // кириллицу в «китайский»/мусор.
            var responseBytes = await response.Content.ReadAsByteArrayAsync();
            var responseContent = Encoding.UTF8.GetString(responseBytes);
            var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);
            if (responseJson.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("message", out var messageObj) &&
                    messageObj.TryGetProperty("content", out var text))
                {
                    return text.GetString() ?? "Не удалось получить ответ от Ollama";
                }
            }
            return "Не удалось получить ответ от Ollama";
        }
        catch (Exception ex)
        {
            return $"❌ **Ollama недоступна:** {ex.Message}";
        }
    }

    public string GetStatus()
    {
        lock (_lockObject)
        {
            return $"🦙 **Ollama** (локальная LLM)\n" +
                   $"📍 {_baseUrl}\n" +
                   $"🤖 Модель: {_model}\n" +
                   $"✅ Без лимитов, запросов: {_requestCount}";
        }
    }

    public void ClearContext(long chatId)
    {
        lock (_lockObject)
        {
            if (_chatContexts.ContainsKey(chatId))
                _chatContexts[chatId].Clear();
        }
    }

    public int GetTotalRequests()
    {
        lock (_lockObject) { return _requestCount; }
    }

    private void AddMessageToContext(long chatId, string role, string content)
    {
        lock (_lockObject)
        {
            if (!_chatContexts.ContainsKey(chatId))
                _chatContexts[chatId] = new List<ChatMessage>();

            _chatContexts[chatId].Add(new ChatMessage { Role = role, Content = content, Timestamp = DateTime.UtcNow });

            if (_chatContexts[chatId].Count > MAX_CONTEXT_MESSAGES)
                _chatContexts[chatId] = _chatContexts[chatId].TakeLast(MAX_CONTEXT_MESSAGES).ToList();
        }
    }

    // Тот же системный промпт с инструкциями по GIF, что и у Gemini —
    // чтобы теги [GIF:эмоция] в ответах продолжали работать.
    private string GetSystemPrompt()
    {
        return @"Ты - дружелюбный AI ассистент в Telegram боте. Твоя задача - общаться с пользователями и помогать им.

ВАЖНО: У тебя есть возможность отправлять GIF анимации через Tenor API!

ПРАВИЛА ОТПРАВКИ GIF:
1. Если твой ответ эмоциональный, веселый, грустный, удивленный, злой, любовный или смешной - добавь в конец ответа тег [GIF:эмоция]
2. Если пользователь просит мем, GIF, анимацию - добавь тег [GIF:мемы]
3. Если контекст разговора подходит для GIF - добавь соответствующий тег
4. Используй РАЗНЫЕ теги для разнообразия

ДОСТУПНЫЕ ЭМОЦИИ: [GIF:смех] [GIF:грусть] [GIF:злость] [GIF:счастье] [GIF:удивление] [GIF:страх] [GIF:любовь] [GIF:мемы] [GIF:шутка] [GIF:работа] [GIF:программирование] [GIF:игры] [GIF:кино] [GIF:музыка] [GIF:животные]

ПРО ЯЗЫК: отвечай на том же языке, на котором задан вопрос. По умолчанию — русский; если спрашивают по-английски, отвечай по-английски. НЕ переходи на китайский или другие языки, если об этом явно не попросили. Если ввод непонятен — переспроси на русском. Будь дружелюбным!";
    }
}
