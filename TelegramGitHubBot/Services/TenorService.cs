using System.Text.Json;
using System.Text.Json.Serialization;

namespace TelegramGitHubBot.Services;

public class TenorService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private static readonly Random _random = new Random();

    public TenorService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["TENOR_API_KEY"] ?? throw new InvalidOperationException("TENOR_API_KEY not configured");
        
        Console.WriteLine($"🔑 Tenor API Key: {(_apiKey.Length >= 20 ? "configured" : "too short — check TENOR_API_KEY")}");
    }

    public async Task<List<TenorGif>> SearchGifsAsync(string query, int limit = 10)
    {
        try
        {
            var url = $"https://tenor.googleapis.com/v2/search?q={Uri.EscapeDataString(query)}&key={_apiKey}&client_key=telegram_bot&limit={limit}&media_filter=gif&contentfilter=medium";
            Console.WriteLine($"🔍 Tenor search: q={query}, limit={limit}");
            
            var httpResponse = await _httpClient.GetAsync(url);
            if (!httpResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"❌ HTTP Error: {httpResponse.StatusCode} - {httpResponse.ReasonPhrase}");
                var errorContent = await httpResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"❌ Error Content: {errorContent}");
                return new List<TenorGif>();
            }
            
            var response = await httpResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"📡 Tenor Response Length: {response.Length}");
            Console.WriteLine($"📡 Tenor Response Preview: {response.Substring(0, Math.Min(500, response.Length))}...");
            
            var result = JsonSerializer.Deserialize<TenorResponse>(response);
            
            var gifs = result?.Results?.Select(g => new TenorGif
            {
                Id = g.Id,
                Title = g.Title ?? "",
                Url = g.MediaFormats?.Gif?.Url ?? g.MediaFormats?.TinyGif?.Url ?? "",
                PreviewUrl = g.MediaFormats?.TinyGif?.Url ?? "",
                Tags = g.Tags ?? new List<string>()
            }).ToList() ?? new List<TenorGif>();
            
            Console.WriteLine($"🎬 Found {gifs.Count} GIFs for query: {query}");
            return gifs;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Tenor API error: {ex.Message}");
            Console.WriteLine($"❌ Stack trace: {ex.StackTrace}");
            return new List<TenorGif>();
        }
    }

    public async Task<List<TenorGif>> GetTrendingGifsAsync(int limit = 10)
    {
        try
        {
            var url = $"https://tenor.googleapis.com/v2/trending?key={_apiKey}&client_key=telegram_bot&limit={limit}&media_filter=gif&contentfilter=medium";
            Console.WriteLine($"📈 Tenor trending: limit={limit}");
            
            var httpResponse = await _httpClient.GetAsync(url);
            if (!httpResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"❌ HTTP Error: {httpResponse.StatusCode} - {httpResponse.ReasonPhrase}");
                var errorContent = await httpResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"❌ Error Content: {errorContent}");
                return new List<TenorGif>();
            }
            
            var response = await httpResponse.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TenorResponse>(response);
            
            return result?.Results?.Select(g => new TenorGif
            {
                Id = g.Id,
                Title = g.Title,
                Url = g.MediaFormats?.Gif?.Url ?? g.MediaFormats?.TinyGif?.Url ?? "",
                PreviewUrl = g.MediaFormats?.TinyGif?.Url ?? "",
                Tags = g.Tags
            }).ToList() ?? new List<TenorGif>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Tenor API error: {ex.Message}");
            return new List<TenorGif>();
        }
    }

    public async Task<TenorGif?> GetRandomGifAsync(string category = "memes")
    {
        try
        {
            var gifs = await SearchGifsAsync(category, 50);
            if (gifs.Count == 0) return null;
            
            return gifs[_random.Next(gifs.Count)];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Tenor API error: {ex.Message}");
            return null;
        }
    }

    public async Task<List<TenorGif>> GetGifsByEmotionAsync(string emotion)
    {
        var emotionQueries = emotion.ToLower() switch
        {
            // Основные эмоции
            "злость" or "злой" or "angry" => new[] { "angry", "mad", "rage", "furious" },
            "счастье" or "счастливый" or "happy" => new[] { "happy", "joy", "celebration", "excited" },
            "грусть" or "грустный" or "sad" => new[] { "sad", "crying", "depressed", "melancholy" },
            "удивление" or "удивленный" or "surprised" => new[] { "surprised", "shocked", "amazed", "wow" },
            "страх" or "испуг" or "scared" => new[] { "scared", "fear", "terrified", "afraid" },
            "любовь" or "любовный" or "love" => new[] { "love", "romance", "heart", "kiss" },
            "смех" or "смешной" or "laugh" => new[] { "laugh", "funny", "comedy", "hilarious" },
            "шутка" or "joke" => new[] { "joke", "funny", "comedy", "humor" },
            "работа" or "work" => new[] { "work", "office", "business", "professional" },
            "оффтоп" or "offtopic" => new[] { "random", "funny", "meme", "reaction" },
            "фол" or "follow" or "support" => new[] { "support", "help", "teamwork", "friendship" },
            "программирование" or "programming" or "код" or "code" => new[] { "programming", "coding", "computer", "tech" },
            
            // Дополнительные категории для разнообразия
            "танцы" or "dance" => new[] { "dance", "dancing", "party", "celebration" },
            "еда" or "food" => new[] { "food", "eating", "cooking", "hungry" },
            "спорт" or "sport" => new[] { "sport", "fitness", "gym", "exercise" },
            "музыка" or "music" => new[] { "music", "singing", "concert", "dancing" },
            "путешествия" or "travel" => new[] { "travel", "vacation", "adventure", "trip" },
            "животные" or "animals" => new[] { "cute animals", "cats", "dogs", "pets" },
            "природа" or "nature" => new[] { "nature", "weather", "landscape", "outdoor" },
            "технологии" or "technology" => new[] { "technology", "gadgets", "computer", "internet" },
            "игры" or "games" => new[] { "gaming", "video games", "fun", "play" },
            "кино" or "movie" => new[] { "movie", "cinema", "film", "actor" },
            "книги" or "books" => new[] { "books", "reading", "study", "knowledge" },
            "искусство" or "art" => new[] { "art", "creative", "drawing", "design" },
            
            _ => new[] { emotion, "meme", "reaction" }
        };

        var allGifs = new List<TenorGif>();
        foreach (var query in emotionQueries.Take(3))
        {
            var gifs = await SearchGifsAsync(query, 5);
            allGifs.AddRange(gifs);
        }

        return allGifs.DistinctBy(g => g.Id).ToList();
    }
}

public class TenorGif
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string PreviewUrl { get; set; } = "";
    public List<string> Tags { get; set; } = new();
}

public class TenorResponse
{
    [JsonPropertyName("results")]
    public List<TenorResult>? Results { get; set; }
}

public class TenorResult
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
    
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
    
    [JsonPropertyName("media_formats")]
    public TenorMediaFormats? MediaFormats { get; set; }
}

public class TenorMediaFormats
{
    [JsonPropertyName("gif")]
    public TenorMedia? Gif { get; set; }
    
    [JsonPropertyName("tinygif")]
    public TenorMedia? TinyGif { get; set; }
}

public class TenorMedia
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
    
    [JsonPropertyName("dims")]
    public int[]? Dims { get; set; }
    
    public int Width => Dims?[0] ?? 0;
    public int Height => Dims?[1] ?? 0;
}
