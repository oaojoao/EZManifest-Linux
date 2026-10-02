using System.Globalization;
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
            return new Bitmap(path);
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

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url))
            return null;
        try
        {
            using var httpClient = new System.Net.Http.HttpClient();
            var data = httpClient.GetByteArrayAsync(url).GetAwaiter().GetResult();
            using var stream = new System.IO.MemoryStream(data);
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
