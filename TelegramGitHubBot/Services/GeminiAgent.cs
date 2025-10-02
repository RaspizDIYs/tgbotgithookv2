using System.Text.Json;

namespace TelegramGitHubBot.Services;

public class GeminiAgent
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _agentName;
    private readonly string _baseUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";
    
    // Счетчики лимитов
    private int _requestsPerMinute = 0;
    private long _tokensUsedToday = 0;
    private DateTime _lastMinuteReset = DateTime.UtcNow;
    private DateTime _lastDayReset = DateTime.UtcNow;
    private readonly object _lockObject = new object();

    // Лимиты Gemini 2.5 Flash (бесплатный тариф)
    private const int MAX_REQUESTS_PER_MINUTE = 15;
    private const long MAX_TOKENS_PER_DAY = 1_000_000;

    public string Name => _agentName;
    public bool IsAvailable => CanMakeRequest();

    public GeminiAgent(HttpClient httpClient, string apiKey, string agentName)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _agentName = agentName;
    }

    public bool CanMakeRequest()
    {
        lock (_lockObject)
        {
            ResetCountersIfNeeded();
            return _requestsPerMinute < MAX_REQUESTS_PER_MINUTE && _tokensUsedToday < MAX_TOKENS_PER_DAY;
        }
    }

    public string GetStatus()
    {
        lock (_lockObject)
        {
            ResetCountersIfNeeded();
            
            var requestsLeft = MAX_REQUESTS_PER_MINUTE - _requestsPerMinute;
            var tokensLeft = MAX_TOKENS_PER_DAY - _tokensUsedToday;
            var nextMinuteReset = _lastMinuteReset.AddMinutes(1);
            var nextDayReset = _lastDayReset.AddDays(1);
            var status = IsAvailable ? "✅ Доступен" : "❌ Недоступен";
            
            return $"🤖 **{_agentName}** - {status}\n" +
                   $"🔄 Запросы: {_requestsPerMinute}/{MAX_REQUESTS_PER_MINUTE} (осталось: {requestsLeft})\n" +
                   $"📝 Токены: {_tokensUsedToday:N0}/{MAX_TOKENS_PER_DAY:N0} (осталось: {tokensLeft:N0})\n" +
                   $"⏰ Сброс запросов: {nextMinuteReset:HH:mm:ss}\n" +
                   $"📅 Сброс токенов: {nextDayReset:dd.MM.yyyy HH:mm}\n";
        }
    }

    private void ResetCountersIfNeeded()
    {
        var now = DateTime.UtcNow;
        
        // Сброс счетчика запросов каждую минуту
        if (now.Subtract(_lastMinuteReset).TotalMinutes >= 1)
        {
            _requestsPerMinute = 0;
            _lastMinuteReset = now;
        }
        
        // Сброс счетчика токенов каждый день
        if (now.Subtract(_lastDayReset).TotalDays >= 1)
        {
            _tokensUsedToday = 0;
            _lastDayReset = now;
        }
    }

    private void IncrementCounters(int estimatedTokens = 100)
    {
        lock (_lockObject)
        {
            ResetCountersIfNeeded();
            _requestsPerMinute++;
            _tokensUsedToday += estimatedTokens;
        }
    }

    public async Task<string> GenerateResponseAsync(string prompt)
    {
        try
        {
            // Проверяем лимиты перед запросом
            if (!CanMakeRequest())
            {
                return $"❌ **{_agentName}: Лимиты исчерпаны!**\n\n" + GetStatus();
            }

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    thinkingConfig = new
                    {
                        thinkingBudget = 0
                    }
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", _apiKey);

            var response = await _httpClient.PostAsync(_baseUrl, content);
            
            // Увеличиваем счетчики после успешного запроса
            var estimatedTokens = prompt.Length / 4 + 100; // Примерная оценка токенов
            IncrementCounters(estimatedTokens);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                
                // Специальная обработка ошибок лимитов
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    return $"⚠️ **{_agentName}: Превышен лимит запросов!**\n\n" + GetStatus();
                }
                
                throw new Exception($"Gemini API error ({_agentName}): {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);

            if (responseJson.TryGetProperty("candidates", out var candidates) && 
                candidates.GetArrayLength() > 0)
            {
                var candidate = candidates[0];
                if (candidate.TryGetProperty("content", out var contentObj) &&
                    contentObj.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0)
                {
                    var textPart = parts[0];
                    if (textPart.TryGetProperty("text", out var text))
                    {
                        return text.GetString() ?? $"Ошибка получения ответа от {_agentName}";
                    }
                }
            }

            return $"Не удалось получить ответ от {_agentName}";
        }
        catch (Exception ex)
        {
            return $"Ошибка {_agentName}: {ex.Message}";
        }
    }
}
