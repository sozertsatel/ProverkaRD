using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ProverkaRD.Engine;

namespace ProverkaRD;

public partial class CheckCell : UserControl
{
    public static readonly StyledProperty<Severity> StatusProperty =
        AvaloniaProperty.Register<CheckCell, Severity>(nameof(Status));

    public static readonly StyledProperty<string?> TipProperty =
        AvaloniaProperty.Register<CheckCell, string?>(nameof(Tip));

    private Border? _markBox;
    private TextBlock? _markText;

    public CheckCell()
    {
        AvaloniaXamlLoader.Load(this);
        _markBox = this.FindControl<Border>("MarkBox");
        _markText = this.FindControl<TextBlock>("MarkText");
        Paint();
    }

    public Severity Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string? Tip
    {
        get => GetValue(TipProperty);
        set => SetValue(TipProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StatusProperty || change.Property == TipProperty)
            Paint();
    }

    private void Paint()
    {
        if (_markText is null || _markBox is null)
            return;
        var failed = Status != Severity.Ok;
        _markText.Text = failed ? "≠" : "ОК";
        _markText.Foreground = new SolidColorBrush(Color.Parse(SeverityPalette.Ink));
        _markBox.Background = failed ? SeverityBrushConverter.Brush(Status) : Brushes.Transparent;
        ToolTip.SetTip(_markBox, string.IsNullOrWhiteSpace(Tip) ? null : Tip);
    }
}
