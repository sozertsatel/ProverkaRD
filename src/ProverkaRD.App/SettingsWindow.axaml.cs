using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ProverkaRD;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly CheckBox _groupDuplicates;
    private readonly CheckBox _updates;
    private readonly TextBox _ignored;
    private readonly TextBox _vat;

    public SettingsWindow() : this(AppSettings.Load())
    {
    }

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        AvaloniaXamlLoader.Load(this);
        _groupDuplicates = this.FindControl<CheckBox>("GroupDuplicatesBox")!;
        _updates = this.FindControl<CheckBox>("UpdatesBox")!;
        _ignored = this.FindControl<TextBox>("IgnoredBox")!;
        _vat = this.FindControl<TextBox>("VatBox")!;
        _groupDuplicates.IsChecked = settings.GroupDuplicateFindings;
        _updates.IsChecked = settings.CheckUpdatesOnStart;
        _ignored.Text = string.Join(Environment.NewLine, settings.IgnoredSigners);
        _vat.Text = settings.VatPercent.ToString("0.##", CultureInfo.GetCultureInfo("ru-RU"));
    }

    private async void OnAbout(object? sender, RoutedEventArgs e)
    {
        Save();
        await new AboutWindow().ShowDialog(this);
    }

    private async void OnForceUpdate(object? sender, RoutedEventArgs e)
    {
        Save();
        await UpdateFlow.RunAsync(this, interactive: true, force: true);
    }

    private void OnOpenLog(object? sender, RoutedEventArgs e)
    {
        try
        {
            RunLog.Open();
        }
        catch (Exception ex)
        {
            RunLog.Error("Открыть журнал", ex);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Save();
        Close();
    }

    private void Save()
    {
        _settings.GroupDuplicateFindings = _groupDuplicates.IsChecked == true;
        _settings.CheckUpdatesOnStart = _updates.IsChecked == true;
        _settings.VatPercent = ReadVat(_vat.Text);
        _settings.IgnoredSigners = (_ignored.Text ?? "")
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _settings.Save();
    }

    private static decimal ReadVat(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 22m;
        var normalized = text.Trim().Replace(" ", "").Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            || value < 0m || value > 100m)
            return 22m;
        return value;
    }
}
