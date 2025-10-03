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

    public async Task<byte[]?> AddTextToGifFromBytesAsync(byte[] gifBytes, string text, TextPosition position = TextPosition.Bottom, string textColor = "white")
    {
        try
        {
            // Для простоты, возвращаем оригинальный GIF с заглушкой
            // В реальном проекте здесь можно использовать внешний API для добавления текста
            Console.WriteLine($"📝 Adding text '{text}' to GIF bytes with position {position} and color {textColor}");
            
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
}

public enum TextPosition
{
    Top,
    Center,
    Bottom
}

// Простой GIF энкодер
[SupportedOSPlatform("windows")]
public class GifEncoder : IDisposable
{
    private readonly MemoryStream _stream;
    private readonly BinaryWriter _writer;
    private bool _disposed = false;

    public GifEncoder(MemoryStream stream)
    {
        _stream = stream;
        _writer = new BinaryWriter(stream);
        
        // Записываем заголовок GIF
        _writer.Write("GIF89a"u8.ToArray());
    }

    public void AddFrame(Bitmap frame, int delay)
    {
        if (_disposed) return;
        
        // Конвертируем кадр в GIF формат
        using var frameStream = new MemoryStream();
#pragma warning disable CA1416 // Validate platform compatibility
        frame.Save(frameStream, ImageFormat.Gif);
#pragma warning restore CA1416 // Validate platform compatibility
        
        // Копируем данные кадра (упрощенная версия)
        var frameBytes = frameStream.ToArray();
        _stream.Write(frameBytes, 13, frameBytes.Length - 13); // Пропускаем заголовок
    }

    public void Finish()
    {
        if (_disposed) return;
        
        // Записываем терминатор
        _writer.Write((byte)0x3B);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _writer?.Dispose();
            _disposed = true;
        }
    }
}
