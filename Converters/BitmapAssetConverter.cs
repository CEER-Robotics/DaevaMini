using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DaevaMini.Converters;

public sealed class BitmapAssetConverter : IValueConverter
{
    public static readonly BitmapAssetConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
            return null;
        if (!targetType.IsAssignableFrom(typeof(Bitmap)))
            return null;
        try
        {
            var uri = path.StartsWith("avares://", StringComparison.Ordinal)
                ? new Uri(path)
                : new Uri($"avares://DaevaMini/Assets/{path.TrimStart('/')}");
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
