using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace EZManifest.Linux.Services;

public sealed class PathToImageConverter : IValueConverter
{
    public static readonly PathToImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class UrlToBitmapConverter : IValueConverter
{
    public static readonly UrlToBitmapConverter Instance = new();

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly ConcurrentDictionary<string, byte[]?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url))
            return null;
        if (Cache.TryGetValue(url, out byte[]? cached))
            return ToBitmap(cached);
        if (Cache.ContainsKey(url))
            return null;
        _ = FetchAsync(url);
        return null;
    }

    private static async Task FetchAsync(string url)
    {
        byte[]? data;
        try
        {
            data = await HttpClient.GetByteArrayAsync(url);
        }
        catch
        {
            data = null;
        }
        Cache[url] = data;
        if (data is null)
            return;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => HttpImageLoaded?.Invoke(url));
    }

    public static event Action<string>? HttpImageLoaded;

    public static Bitmap? ToBitmap(byte[]? data)
    {
        if (data is null || data.Length == 0)
            return null;
        try
        {
            using var stream = new MemoryStream(data);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
