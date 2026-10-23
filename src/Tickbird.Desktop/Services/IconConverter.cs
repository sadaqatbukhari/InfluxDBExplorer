using System.Globalization;
using Avalonia.Data.Converters;

namespace Tickbird.Desktop.Services;

/// <summary>Maps a shell icon name to an outline image, falling back to legacy assets.</summary>
public sealed class IconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string name ? ShellIcons.Get(name) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
