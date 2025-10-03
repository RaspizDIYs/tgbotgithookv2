using System.Text.Json;

namespace TelegramGitHubBot.Services;

public class TenorService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public TenorService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["TENOR_API_KEY"] ?? throw new InvalidOperationException("TENOR_API_KEY not configured");
    }

    public async Task<List<TenorGif>> SearchGifsAsync(string query, int limit = 10)
    {
        try
        {
            var url = $"https://tenor.googleapis.com/v2/search?q={Uri.EscapeDataString(query)}&key={_apiKey}&limit={limit}&media_filter=gif";
            var response = await _httpClient.GetStringAsync(url);
            var result = JsonSerializer.Deserialize<TenorResponse>(response);
            
            return result?.Results?.Select(g => new TenorGif
            {
                Id = g.Id,
                Title = g.Title,
                Url = g.MediaFormats?.Gif?.Url ?? g.MediaFormats?.TinyGif?.Url,
                PreviewUrl = g.MediaFormats?.TinyGif?.Url,
                Tags = g.Tags
            }).ToList() ?? new List<TenorGif>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Tenor API error: {ex.Message}");
            return new List<TenorGif>();
        }
    }

    public async Task<List<TenorGif>> GetTrendingGifsAsync(int limit = 10)
    {
        try
        {
            var url = $"https://tenor.googleapis.com/v2/trending?key={_apiKey}&limit={limit}&media_filter=gif";
            var response = await _httpClient.GetStringAsync(url);
            var result = JsonSerializer.Deserialize<TenorResponse>(response);
            
            return result?.Results?.Select(g => new TenorGif
            {
                Id = g.Id,
                Title = g.Title,
                Url = g.MediaFormats?.Gif?.Url ?? g.MediaFormats?.TinyGif?.Url,
                PreviewUrl = g.MediaFormats?.TinyGif?.Url,
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
            
            var random = new Random();
            return gifs[random.Next(gifs.Count)];
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
            "злость" or "злой" or "angry" => new[] { "angry", "mad", "rage", "furious" },
            "счастье" or "счастливый" or "happy" => new[] { "happy", "joy", "celebration", "excited" },
            "грусть" or "грустный" or "sad" => new[] { "sad", "crying", "depressed", "melancholy" },
            "удивление" or "удивленный" or "surprised" => new[] { "surprised", "shocked", "amazed", "wow" },
            "страх" or "испуг" or "scared" => new[] { "scared", "fear", "terrified", "afraid" },
            "любовь" or "любовный" or "love" => new[] { "love", "romance", "heart", "kiss" },
            "смех" or "смешной" or "laugh" => new[] { "laugh", "funny", "comedy", "hilarious" },
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
    public List<TenorResult>? Results { get; set; }
}

public class TenorResult
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public TenorMediaFormats? MediaFormats { get; set; }
}

public class TenorMediaFormats
{
    public TenorMedia? Gif { get; set; }
    public TenorMedia? TinyGif { get; set; }
}

public class TenorMedia
{
    public string Url { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
}
