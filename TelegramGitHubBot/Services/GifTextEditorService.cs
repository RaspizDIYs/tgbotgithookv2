using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.Versioning;

namespace TelegramGitHubBot.Services;

[SupportedOSPlatform("windows")]
public class GifTextEditorService
{
    private readonly HttpClient _httpClient;

    public GifTextEditorService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<byte[]?> AddTextToGifAsync(string gifUrl, string text, TextPosition position = TextPosition.Bottom, Color? textColor = null)
    {
        try
        {
            // Скачиваем GIF
            var gifBytes = await _httpClient.GetByteArrayAsync(gifUrl);
            
            // Загружаем GIF как изображение
            using var originalImage = Image.FromStream(new MemoryStream(gifBytes));
            
            // Создаем новый GIF с текстом
            return await CreateGifWithTextAsync(originalImage, text, position, textColor ?? Color.White);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ GIF editing error: {ex.Message}");
            return null;
        }
    }

    private Task<byte[]> CreateGifWithTextAsync(Image originalImage, string text, TextPosition position, Color textColor)
    {
        return Task.Run(() =>
        {
            using var memoryStream = new MemoryStream();
            
            // Получаем информацию о кадрах GIF
            var frameCount = originalImage.GetFrameCount(FrameDimension.Time);
            var frameDelay = GetFrameDelay(originalImage);
            
            // Создаем новый GIF с текстом
            using var gifEncoder = new GifEncoder(memoryStream);
            
            for (int i = 0; i < frameCount; i++)
            {
                originalImage.SelectActiveFrame(FrameDimension.Time, i);
                
                // Создаем копию кадра
                using var frame = new Bitmap(originalImage);
                using var graphics = Graphics.FromImage(frame);
                
                // Настраиваем качество рендеринга
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
                
                // Добавляем текст
                AddTextToFrame(graphics, frame, text, position, textColor);
                
                // Добавляем кадр в GIF
                gifEncoder.AddFrame(frame, frameDelay);
            }
            
            gifEncoder.Finish();
            return memoryStream.ToArray();
        });
    }

    private void AddTextToFrame(Graphics graphics, Bitmap frame, string text, TextPosition position, Color textColor)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // Настройки шрифта
        var fontSize = Math.Max(16, frame.Width / 20); // Адаптивный размер шрифта
        using var font = new Font("Arial", fontSize, FontStyle.Bold);
        
        // Создаем контур для лучшей читаемости
        using var outlineBrush = new SolidBrush(Color.Black);
        using var textBrush = new SolidBrush(textColor);
        
        // Измеряем текст
        var textSize = graphics.MeasureString(text, font);
        
        // Вычисляем позицию
        var x = (frame.Width - textSize.Width) / 2;
        var y = position switch
        {
            TextPosition.Top => 10,
            TextPosition.Bottom => frame.Height - textSize.Height - 10,
            TextPosition.Center => (frame.Height - textSize.Height) / 2,
            _ => frame.Height - textSize.Height - 10
        };
        
        // Рисуем контур (4 направления)
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                graphics.DrawString(text, font, outlineBrush, x + dx, y + dy);
            }
        }
        
        // Рисуем основной текст
        graphics.DrawString(text, font, textBrush, x, y);
    }

    private int GetFrameDelay(Image image)
    {
        try
        {
            var delayProperty = image.GetPropertyItem(0x5100); // PropertyTagFrameDelay
            if (delayProperty?.Value != null)
            {
                return BitConverter.ToInt32(delayProperty.Value, 0) * 10; // Конвертируем в миллисекунды
            }
            return 100; // По умолчанию 100мс
        }
        catch
        {
            return 100; // По умолчанию 100мс
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
