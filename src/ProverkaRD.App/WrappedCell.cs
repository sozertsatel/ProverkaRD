using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ProverkaRD;

/// <summary>Текст ячейки с переносом, высотой строки по содержимому и полной подсказкой.</summary>
public class WrappedCell : TextBlock
{
    private DataGridCell? _cell;

    public WrappedCell()
    {
        TextWrapping = TextWrapping.Wrap;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(4, 3, 4, 3);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
            UpdateTip();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _cell = this.FindAncestorOfType<DataGridCell>();
        if (_cell is not null)
            _cell.SizeChanged += OnCellSize;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_cell is not null)
            _cell.SizeChanged -= OnCellSize;
        _cell = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width;
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 1)
            width = CellWidth();
        width = Math.Max(24, width - 12);
        return base.MeasureOverride(new Size(width, double.PositiveInfinity));
    }

    private void OnCellSize(object? sender, SizeChangedEventArgs e) => InvalidateMeasure();

    private double CellWidth()
    {
        var width = (_cell ?? this.FindAncestorOfType<DataGridCell>())?.Bounds.Width ?? 0;
        return width > 1 ? width : 160;
    }

    private void UpdateTip()
    {
        var text = Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            ToolTip.SetTip(this, null);
            return;
        }

        ToolTip.SetTip(this, new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560,
        });
    }
}
