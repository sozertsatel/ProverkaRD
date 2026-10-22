using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ProverkaRD;

public partial class AboutWindow : Window
{
    private readonly TextBlock _status;
    private readonly Button _releases;

    public AboutWindow()
    {
        AvaloniaXamlLoader.Load(this);
        var version = this.FindControl<TextBlock>("VersionText");
        if (version is not null)
            version.Text = "Версия " + AppInfo.Version;
        var description = this.FindControl<TextBlock>("DescriptionText");
        if (description is not null)
            description.Text = AppInfo.Description;
        var changelog = this.FindControl<SelectableTextBlock>("ChangelogText");
        if (changelog is not null)
            changelog.Text = AppInfo.Changelog();
        _status = this.FindControl<TextBlock>("UpdateStatus")!;
        _releases = this.FindControl<Button>("OpenReleasesButton")!;
    }

    private async void OnOpenRepo(object? sender, RoutedEventArgs e) =>
        await Open(AppInfo.RepoUrl);

    private async void OnOpenReleases(object? sender, RoutedEventArgs e) =>
        await Open(AppInfo.ReleasesPage);

    private async void OnCheckUpdates(object? sender, RoutedEventArgs e)
    {
        _status.Text = "Проверяем последний релиз…";
        var result = await UpdateChecker.CheckAsync();
        _status.Text = result.Kind == UpdateCheckKind.Offline
            ? "Нет сети, проверить обновление не удалось."
            : result.Message;
        _releases.IsVisible = result.Kind is UpdateCheckKind.Newer or UpdateCheckKind.Unavailable;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private async Task Open(string url)
    {
        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is not null)
            await launcher.LaunchUriAsync(new Uri(url));
    }
}
