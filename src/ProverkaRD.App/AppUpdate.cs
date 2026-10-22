using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ProverkaRD.Engine;

namespace ProverkaRD;

public enum InstallKind
{
    Restarted,
    Script,
    Manual,
    Failed,
}

public sealed record InstallOutcome(InstallKind Kind, string Message);

/// <summary>Скачивание ProverkaRD.exe, проверка SHA256 и замена запущенного файла.</summary>
public static class AppUpdate
{
    private static readonly HttpClient Files = CreateFiles();

    public static void DeleteBackup()
    {
        var exe = CurrentExecutable();
        if (exe is null)
            return;
        try
        {
            var backup = exe + ".old";
            if (File.Exists(backup))
                File.Delete(backup);
        }
        catch
        {
            // Файл ещё занят предыдущим процессом — следующая попытка будет при следующем запуске.
        }
    }

    public static async Task<string> DownloadAsync(
        UpdateCheckResult release,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(release.ExeUrl))
            throw new InvalidOperationException("В релизе нет файла ProverkaRD.exe.");

        var path = Path.Combine(Path.GetTempPath(), "ProverkaRD-" + Guid.NewGuid().ToString("N") + ".exe");
        using var request = new HttpRequestMessage(HttpMethod.Get, release.ExeUrl);
        request.Headers.Accept.ParseAdd("application/octet-stream");
        using var response = await Files.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? (release.ExeSize > 0 ? release.ExeSize : (long?)null);
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = File.Create(path))
        {
            var buffer = new byte[81920];
            long read = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                read += count;
                if (total is > 0)
                    progress?.Report(Math.Min(1, read / (double)total.Value));
            }

            if (release.ExeSize > 0 && read != release.ExeSize)
                throw new IOException($"Размер скачанного файла {read} байт, в релизе указано {release.ExeSize}.");
        }

        if (!string.IsNullOrWhiteSpace(release.HashUrl))
        {
            using var hashRequest = new HttpRequestMessage(HttpMethod.Get, release.HashUrl);
            hashRequest.Headers.Accept.ParseAdd("text/plain");
            using var hashResponse = await Files.SendAsync(hashRequest, cancellationToken);
            hashResponse.EnsureSuccessStatusCode();
            var hashText = await hashResponse.Content.ReadAsStringAsync(cancellationToken);
            await using var file = File.OpenRead(path);
            if (!ReleaseCatalog.HashMatches(file, hashText))
                throw new IOException("Контрольная сумма SHA256 не совпала с ProverkaRD.exe.sha256.");
        }

        progress?.Report(1);
        return path;
    }

    public static InstallOutcome Install(string downloadedExe)
    {
        var exe = CurrentExecutable();
        if (exe is null || !File.Exists(downloadedExe))
        {
            return new InstallOutcome(
                InstallKind.Manual,
                "Не удалось определить файл ProverkaRD.exe. Скачайте его со страницы релиза.");
        }

        if (!CanWrite(exe))
            return StartScript(exe, downloadedExe);

        var backup = exe + ".old";
        try
        {
            if (File.Exists(backup))
                File.Delete(backup);
            File.Move(exe, backup);
            File.Copy(downloadedExe, exe, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryRestore(backup, exe);
            return StartScript(exe, downloadedExe);
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            return new InstallOutcome(InstallKind.Failed, "Файл заменён, но запустить новую версию не удалось: " + ex.Message);
        }

        Shutdown();
        return new InstallOutcome(InstallKind.Restarted, "Новая версия запущена.");
    }

    private static InstallOutcome StartScript(string exe, string downloadedExe)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new InstallOutcome(
                InstallKind.Manual,
                "Заменить запущенный файл автоматически можно в Windows. Скачайте ProverkaRD.exe со страницы релиза.");
        }

        try
        {
            var script = Path.Combine(Path.GetTempPath(), "proverkard-update-" + Environment.ProcessId + ".cmd");
            var backupName = Path.GetFileName(exe) + ".old";
            var text = string.Join("\r\n",
            [
                "@echo off",
                "ping 127.0.0.1 -n 3 >nul",
                "set /a n=0",
                ":retry",
                "set /a n+=1",
                "if %n% GTR 30 exit /b 1",
                "del /f /q \"" + exe + ".old\" >nul 2>&1",
                "rename \"" + exe + "\" \"" + backupName + "\"",
                "if errorlevel 1 (",
                "ping 127.0.0.1 -n 2 >nul",
                "goto retry",
                ")",
                "copy /y \"" + downloadedExe + "\" \"" + exe + "\"",
                "if errorlevel 1 goto retry",
                "start \"\" \"" + exe + "\"",
                "del \"%~f0\"",
            ]);
            File.WriteAllText(script, text);
            Process.Start(new ProcessStartInfo(script)
            {
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            Shutdown();
            return new InstallOutcome(InstallKind.Script, "Обновление продолжится после закрытия программы.");
        }
        catch (Exception ex)
        {
            return new InstallOutcome(
                InstallKind.Manual,
                "Нет прав записать новую версию в папку программы (" + ex.Message + "). Скачайте ProverkaRD.exe со страницы релиза.");
        }
    }

    private static void TryRestore(string backup, string exe)
    {
        try
        {
            if (!File.Exists(exe) && File.Exists(backup))
                File.Move(backup, exe);
        }
        catch
        {
            // Исходный файл остаётся под именем .old, если вернуть его не удалось.
        }
    }

    private static bool CanWrite(string exe)
    {
        try
        {
            var probe = Path.Combine(Path.GetDirectoryName(exe)!, ".proverkard-write-" + Environment.ProcessId);
            File.WriteAllText(probe, "1");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? CurrentExecutable()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
            return null;
        return Path.GetFileName(path).Equals("ProverkaRD.exe", StringComparison.OrdinalIgnoreCase)
            ? path
            : null;
    }

    private static HttpClient CreateFiles()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ProverkaRD");
        return client;
    }

    private static void Shutdown()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            Dispatcher.UIThread.Post(() => desktop.Shutdown());
    }
}

public static class UpdateFlow
{
    public static async Task RunAsync(Window owner, bool interactive)
    {
        UpdateCheckResult result;
        try
        {
            result = await UpdateChecker.CheckAsync();
        }
        catch (Exception ex)
        {
            if (interactive)
                await Tell(owner, "Не удалось проверить обновления: " + ex.Message, AppInfo.ReleasesPage);
            return;
        }

        var page = string.IsNullOrWhiteSpace(result.PageUrl) ? AppInfo.ReleasesPage : result.PageUrl;
        if (result.Kind == UpdateCheckKind.Offline)
        {
            if (interactive)
                await Tell(owner, "Нет сети, проверить обновление не удалось.", page);
            return;
        }

        if (result.Kind == UpdateCheckKind.Unavailable)
        {
            if (interactive)
                await Tell(owner, result.Message, page);
            return;
        }

        if (result.Kind == UpdateCheckKind.UpToDate)
        {
            if (interactive)
                await Tell(owner, result.Message, null);
            return;
        }

        if (result.Kind != UpdateCheckKind.Newer)
            return;

        var notes = string.IsNullOrWhiteSpace(result.Notes) ? "Описание релиза не заполнено." : result.Notes.Trim();
        var update = await Ask(
            owner,
            "Доступна версия " + result.RemoteTag + ". Что нового:",
            notes,
            "Обновить",
            "Позже");
        if (!update)
            return;

        if (string.IsNullOrWhiteSpace(result.ExeUrl))
        {
            await Tell(owner, "В релизе " + result.RemoteTag + " нет файла ProverkaRD.exe.", page);
            return;
        }

        string? downloaded = null;
        var keepDownload = false;
        try
        {
            downloaded = await DownloadWithProgress(owner, result);
            var outcome = AppUpdate.Install(downloaded);
            keepDownload = outcome.Kind == InstallKind.Script;
            if (outcome.Kind is InstallKind.Manual or InstallKind.Failed)
                await Tell(owner, outcome.Message, page);
        }
        catch (Exception ex)
        {
            await Tell(owner, "Не удалось скачать обновление: " + ex.Message, page);
        }
        finally
        {
            if (!keepDownload)
                TryDelete(downloaded);
        }
    }

    private static async Task<string> DownloadWithProgress(Window owner, UpdateCheckResult result)
    {
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 18 };
        var label = new TextBlock { Text = "Скачиваем ProverkaRD.exe…", TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 12 };
        panel.Children.Add(label);
        panel.Children.Add(bar);
        var window = new Window
        {
            Title = "Обновление",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = Brushes.White,
            Content = panel,
        };
        using var cancel = new CancellationTokenSource();
        window.Closing += (_, _) => cancel.Cancel();
        var progress = new Progress<double>(value =>
        {
            bar.Value = value * 100;
            label.Text = value >= 1 ? "Проверяем файл…" : "Скачиваем ProverkaRD.exe… " + Math.Round(value * 100) + "%";
        });

        var download = AppUpdate.DownloadAsync(result, progress, cancel.Token);
        window.Opened += async (_, _) =>
        {
            try
            {
                await download;
            }
            catch
            {
                // Ошибку заберёт ожидание download снаружи.
            }
            if (window.IsVisible)
                window.Close();
        };
        await window.ShowDialog(owner);
        return await download;
    }

    private static async Task<bool> Ask(Window owner, string title, string notes, string yes, string no)
    {
        var chosen = false;
        var panel = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#E2E7EE")),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(8),
            MaxHeight = 240,
            Child = new ScrollViewer
            {
                Padding = new Avalonia.Thickness(12),
                Content = new SelectableTextBlock { Text = notes, TextWrapping = TextWrapping.Wrap },
            },
        });
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var dialog = new Window
        {
            Title = "Обновление",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = Brushes.White,
            Content = panel,
        };
        var accept = new Button { Content = yes, Classes = { "accent" } };
        accept.Click += (_, _) =>
        {
            chosen = true;
            dialog.Close();
        };
        var later = new Button { Content = no };
        later.Click += (_, _) => dialog.Close();
        buttons.Children.Add(accept);
        buttons.Children.Add(later);
        panel.Children.Add(buttons);
        await dialog.ShowDialog(owner);
        return chosen;
    }

    private static async Task Tell(Window owner, string message, string? page)
    {
        var panel = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var dialog = new Window
        {
            Title = "Обновление",
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = Brushes.White,
            Content = panel,
        };
        if (!string.IsNullOrWhiteSpace(page))
        {
            var open = new Button { Content = "Открыть страницу релиза", Classes = { "primary" } };
            open.Click += async (_, _) =>
            {
                var launcher = TopLevel.GetTopLevel(owner)?.Launcher;
                if (launcher is not null)
                    await launcher.LaunchUriAsync(new Uri(page));
            };
            buttons.Children.Add(open);
        }

        var close = new Button { Content = "Закрыть" };
        close.Click += (_, _) => dialog.Close();
        buttons.Children.Add(close);
        panel.Children.Add(buttons);
        await dialog.ShowDialog(owner);
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Временный файл не мешает следующей проверке.
        }
    }
}
