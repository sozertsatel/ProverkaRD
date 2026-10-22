using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ProverkaRD.Engine;

namespace ProverkaRD;

public sealed class SeverityTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Severity severity ? SeverityText.ToRu(severity) : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class SeverityBrushConverter : IValueConverter
{
    public static IBrush Brush(Severity severity) => severity switch
    {
        Severity.Critical => new SolidColorBrush(Color.Parse("#F4C7C3")),
        Severity.Formatting => new SolidColorBrush(Color.Parse("#FFE08A")),
        Severity.Warning => new SolidColorBrush(Color.Parse("#F8CBAD")),
        _ => new SolidColorBrush(Color.Parse("#C6EFCE")),
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Severity severity ? Brush(severity) : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
