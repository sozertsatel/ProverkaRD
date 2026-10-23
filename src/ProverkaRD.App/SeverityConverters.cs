using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ProverkaRD.Engine;

namespace ProverkaRD;

public sealed class MissingBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true
            ? new SolidColorBrush(Color.Parse("#B91C1C"))
            : new SolidColorBrush(Color.Parse("#1F2937"));

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class SeverityTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Severity severity ? SeverityText.ToRu(severity) : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class SeverityBrushConverter : IValueConverter
{
    public static IBrush Brush(Severity severity) =>
        new SolidColorBrush(Color.Parse(SeverityPalette.For(severity)));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Severity severity ? Brush(severity) : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Цвет только у незачёта. У «ОК» ячейка остаётся без заливки.</summary>
public sealed class FailBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Severity severity && severity != Severity.Ok
            ? SeverityBrushConverter.Brush(severity)
            : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class MoneyTextConverter : IValueConverter
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is decimal amount ? amount.ToString("N2", Ru) : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StateBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string) switch
        {
            "checking" => new SolidColorBrush(Color.Parse("#D6E3F0")),
            "empty" or "locked" or "missing" => new SolidColorBrush(Color.Parse(SeverityPalette.Warning)),
            "unreadable" or "failed" => new SolidColorBrush(Color.Parse(SeverityPalette.Critical)),
            "finished" => new SolidColorBrush(Color.Parse(SeverityPalette.Ok)),
            _ => new SolidColorBrush(Color.Parse("#E8EEF5")),
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
