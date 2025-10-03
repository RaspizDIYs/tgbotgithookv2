using System.Text.Json;

namespace TelegramGitHubBot.Services;

public class GifTextEditorService
{
    private readonly HttpClient _httpClient;

    public GifTextEditorService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<byte[]?> AddTextToGifAsync(string gifUrl, string text, TextPosition position = TextPosition.Bottom, string textColor = "white")
    {
        try
        {
            // Скачиваем GIF
            var gifBytes = await _httpClient.GetByteArrayAsync(gifUrl);
            
            // Для простоты, возвращаем оригинальный GIF с заглушкой
            // В реальном проекте здесь можно использовать внешний API для добавления текста
            Console.WriteLine($"📝 Adding text '{text}' to GIF with position {position} and color {textColor}");
            
            // Пока что возвращаем оригинальный GIF
            // TODO: Интегрировать с внешним API для добавления текста на GIF
            return gifBytes;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ GIF editing error: {ex.Message}");
            return null;
        }
    }

    public Task<byte[]?> AddTextToGifFromBytesAsync(byte[] gifBytes, string text, TextPosition position = TextPosition.Bottom, string textColor = "white")
    {
        try
        {
            // Для простоты, возвращаем оригинальный GIF с заглушкой
            // В реальном проекте здесь можно использовать внешний API для добавления текста
            Console.WriteLine($"📝 Adding text '{text}' to GIF bytes with position {position} and color {textColor}");
            
            // Пока что возвращаем оригинальный GIF
            // TODO: Интегрировать с внешним API для добавления текста на GIF
            return Task.FromResult<byte[]?>(gifBytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ GIF editing error: {ex.Message}");
            return Task.FromResult<byte[]?>(null);
        }
    }
}

public enum TextPosition
{
    Top,
    Center,
    Bottom
}