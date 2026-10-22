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
        _groupDuplicates.IsChecked = settings.GroupDuplicateFindings;
        _updates.IsChecked = settings.CheckUpdatesOnStart;
        _ignored.Text = string.Join(Environment.NewLine, settings.IgnoredSigners);
    }

    private async void OnAbout(object? sender, RoutedEventArgs e)
    {
        Save();
        await new AboutWindow().ShowDialog(this);
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
        _settings.IgnoredSigners = (_ignored.Text ?? "")
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _settings.Save();
    }
}
