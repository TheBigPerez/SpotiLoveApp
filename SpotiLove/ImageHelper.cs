using System.Globalization;
using Microsoft.Maui.Controls;

namespace SpotiLove;

public static class ImageHelper
{
    public static ImageSource Resolve(string? src)
    {
        if (string.IsNullOrWhiteSpace(src))
            return ImageSource.FromFile("default_user.png");

        if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return ImageSource.FromUri(new Uri(src));

        var s = src;
        var comma = s.IndexOf(',');
        if (s.StartsWith("data:") && comma >= 0) s = s[(comma + 1)..];

        try
        {
            var bytes = Convert.FromBase64String(s);
            return ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch
        {
            return ImageSource.FromFile("default_user.png");
        }
    }
}

public class ProfileImageConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => ImageHelper.Resolve(value as string);

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c)
        => throw new NotImplementedException();
}