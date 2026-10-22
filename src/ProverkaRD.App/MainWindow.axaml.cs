using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ProverkaRD.Engine;
using ProverkaRD.Engine.Parsing;

namespace ProverkaRD;

public partial class MainWindow : Window
{
    private readonly WorkspaceModel _model = new();
    private readonly ListBox _rdList;
    private readonly ListBox _actList;
    private AppSettings _settings = AppSettings.Load();

    public MainWindow()
    {
        _model.Options = _settings.ToOptions();
        DataContext = _model;
        AvaloniaXamlLoader.Load(this);
        Title = AppInfo.WindowTitle;
        var versionText = this.FindControl<TextBlock>("VersionText");
        if (versionText is not null)
            versionText.Text = AppInfo.Version;
        _rdList = this.FindControl<ListBox>("RdList") ?? throw new InvalidOperationException("Не найден список комплектов.");
        _actList = this.FindControl<ListBox>("ActList") ?? throw new InvalidOperationException("Не найден список актов.");
        Opened += (_, _) =>
        {
            if (_settings.CheckUpdatesOnStart)
                _ = RunUpdateCheck(interactive: false);
        };
    }

    private async void OnLoadVerd(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var file = await PickFile("Ведомость ВЭРД", "*.xlsx");
        if (file is not null)
            _model.VerdPath = file;
    }

    private async void OnLoadVesr(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var file = await PickFile("Ведомость ВЭСР", "*.xlsx");
        if (file is not null)
            _model.VesrPath = file;
    }

    private async void OnAddRdFiles(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await AddFiles(_model.AddRd, "Комплекты РД", true, "*.pdf", "*.docx");

    private async void OnAddActFiles(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await AddFiles(_model.AddAct, "Акты сдачи-приёмки", true, "*.docx");

    private async void OnAddRdFolder(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folder = await PickFolder("Папка с комплектами РД");
        if (folder is not null)
            _model.AddRd(folder);
    }

    private async void OnAddActFolder(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folder = await PickFolder("Папка с актами");
        if (folder is not null)
            _model.AddAct(folder);
    }

    private void OnRemoveRd(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _model.RemoveRd(_rdList.SelectedItems?.Cast<InputFile>().ToList() ?? []);

    private void OnRemoveAct(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _model.RemoveAct(_actList.SelectedItems?.Cast<InputFile>().ToList() ?? []);

    private void OnClearRd(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _model.RdFiles.Clear();
    private void OnClearActs(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _model.ActFiles.Clear();

    private void OnRdDrop(object? sender, DragEventArgs e) => DropInto(e, _model.AddRd);
    private void OnActDrop(object? sender, DragEventArgs e) => DropInto(e, _model.AddAct);

    private async void OnCheck(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_model.CanRun)
            return;
        if (_model.VerdPath is null && _model.VesrPath is null && _model.ActFiles.Count == 0 && _model.RdFiles.Count == 0)
        {
            _model.StatusText = "Нечего проверять: загрузите ВЭРД, ВЭСР, акты или комплекты.";
            return;
        }

        _model.Busy = true;
        _model.StatusText = "Идёт проверка. Ведомость ВЭСР большая, это занимает несколько секунд.";
        var input = _model.BuildInput();
        try
        {
            var report = await Task.Run(() => new CheckingEngine().Run(input));
            _model.Apply(report);
        }
        catch (Exception ex)
        {
            _model.StatusText = "Проверка остановилась: " + ex.Message;
        }
        finally
        {
            _model.Busy = false;
        }
    }

    private async void OnExport(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_model.Report is null)
        {
            _model.StatusText = "Сначала нажмите «Проверить».";
            return;
        }

        var top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Куда сохранить отчёт",
            SuggestedFileName = "Проверка_РД.xlsx",
            DefaultExtension = "xlsx",
            FileTypeChoices = [new FilePickerFileType("Книга Excel") { Patterns = ["*.xlsx"] }],
        });
        var path = file?.TryGetLocalPath();
        if (path is null)
            return;
        try
        {
            ReportWriter.Write(_model.Report, path, AppInfo.Version);
            _model.StatusText = "Отчёт сохранён: " + path;
        }
        catch (Exception ex)
        {
            _model.StatusText = "Не удалось записать отчёт: " + ex.Message;
        }
    }

    private async Task<string?> PickFile(string title, params string[] patterns)
    {
        var files = await PickFiles(title, false, patterns);
        return files.FirstOrDefault();
    }

    private async Task AddFiles(Action<string> add, string title, bool multiple, params string[] patterns)
    {
        foreach (var file in await PickFiles(title, multiple, patterns))
            add(file);
    }

    private async Task<IReadOnlyList<string>> PickFiles(string title, bool multiple, params string[] patterns)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
            return [];
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = multiple,
            FileTypeFilter = [new FilePickerFileType(title) { Patterns = patterns }],
        });
        return files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>().ToList();
    }

    private async Task<string?> PickFolder(string title)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
            return null;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private async void OnSettings(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await new SettingsWindow(_settings).ShowDialog(this);
        _settings = AppSettings.Load();
        _model.Options = _settings.ToOptions();
    }

    private async Task RunUpdateCheck(bool interactive)
    {
        var result = await UpdateChecker.CheckAsync();
        if (result.Kind == UpdateCheckKind.Offline)
            return;
        if (!interactive && result.Kind == UpdateCheckKind.UpToDate)
            return;
        if (!interactive && result.Kind == UpdateCheckKind.Unavailable)
        {
            _model.StatusText = result.Message;
            return;
        }
        if (result.Kind == UpdateCheckKind.UpToDate)
        {
            await ShowUpdateDialog(result.Message, offerPage: false);
            return;
        }

        await ShowUpdateDialog(result.Message, offerPage: true);
    }

    private async Task ShowUpdateDialog(string message, bool offerPage)
    {
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
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
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = Brushes.White,
            Content = panel,
        };
        if (offerPage)
        {
            var open = new Button { Content = "Открыть страницу релизов", Classes = { "primary" } };
            open.Click += async (_, _) =>
            {
                var launcher = TopLevel.GetTopLevel(this)?.Launcher;
                if (launcher is not null)
                    await launcher.LaunchUriAsync(new Uri(AppInfo.ReleasesPage));
            };
            buttons.Children.Add(open);
        }

        var close = new Button { Content = "Закрыть" };
        close.Click += (_, _) => dialog.Close();
        buttons.Children.Add(close);
        panel.Children.Add(buttons);
        await dialog.ShowDialog(this);
    }

    private static void DropInto(DragEventArgs e, Action<string> add)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
            return;
        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (path is not null)
                add(path);
        }
    }
}

public sealed class InputFile
{
    public required string Path { get; init; }
    public required string Caption { get; init; }
}

public sealed class WorkspaceModel : INotifyPropertyChanged
{
    private string? _verdPath;
    private string? _vesrPath;
    private bool _busy;
    private string _status = "Загрузите ВЭРД и ВЭСР, добавьте акты и комплекты, затем нажмите «Проверить».";
    private string _filter = "";
    private string _summary = "Результатов пока нет.";
    private CheckReport? _report;

    public ObservableCollection<InputFile> RdFiles { get; } = [];
    public ObservableCollection<InputFile> ActFiles { get; } = [];
    public ObservableCollection<ActResult> FilteredActs { get; } = [];
    public ObservableCollection<KitResult> FilteredKits { get; } = [];
    public ObservableCollection<Finding> FilteredFindings { get; } = [];

    public WorkspaceModel()
    {
        RdFiles.CollectionChanged += (_, _) => Raise(nameof(RdEmpty));
        ActFiles.CollectionChanged += (_, _) => Raise(nameof(ActEmpty));
    }

    public string? VerdPath
    {
        get => _verdPath;
        set
        {
            _verdPath = value;
            Raise(nameof(VerdCaption));
        }
    }

    public string? VesrPath
    {
        get => _vesrPath;
        set
        {
            _vesrPath = value;
            Raise(nameof(VesrCaption));
        }
    }

    public string VerdCaption => _verdPath is null ? "файл не выбран" : Path.GetFileName(_verdPath);
    public string VesrCaption => _vesrPath is null ? "файл не выбран" : Path.GetFileName(_vesrPath);
    public bool RdEmpty => RdFiles.Count == 0;
    public bool ActEmpty => ActFiles.Count == 0;
    public bool Busy
    {
        get => _busy;
        set
        {
            _busy = value;
            Raise();
            Raise(nameof(CanRun));
            Raise(nameof(CanExport));
        }
    }

    public bool CanRun => !Busy;
    public bool CanExport => !Busy && _report is not null;
    public CheckReport? Report => _report;

    public string StatusText
    {
        get => _status;
        set { _status = value; Raise(); }
    }

    public string SummaryText
    {
        get => _summary;
        private set { _summary = value; Raise(); }
    }

    public string FilterText
    {
        get => _filter;
        set
        {
            _filter = value;
            Raise();
            Refilter();
        }
    }

    public void AddRd(string path) => AddPath(path, RdFiles, [".pdf", ".docx"], withSignature: true);
    public void AddAct(string path) => AddPath(path, ActFiles, [".docx"], withSignature: false);

    public void RemoveRd(IReadOnlyList<InputFile> items)
    {
        foreach (var item in items)
            RdFiles.Remove(item);
    }

    public void RemoveAct(IReadOnlyList<InputFile> items)
    {
        foreach (var item in items)
            ActFiles.Remove(item);
    }

    public CheckOptions Options { get; set; } = new();

    public CheckInput BuildInput() => new()
    {
        VerdPath = VerdPath,
        VesrPath = VesrPath,
        ActPaths = ActFiles.Select(f => f.Path).ToList(),
        RdPaths = RdFiles.Select(f => f.Path).ToList(),
        Options = Options,
    };

    public void Apply(CheckReport report)
    {
        _report = report;
        var critical = report.Findings.Count(f => f.Severity == Severity.Critical);
        var formatting = report.Findings.Count(f => f.Severity == Severity.Formatting);
        var warnings = report.Findings.Count(f => f.Severity == Severity.Warning);
        var ok = report.Acts.Count(a => a.Overall == Severity.Ok);
        SummaryText = $"Актов: {report.Acts.Count} · критично: {critical} · оформление: {formatting} · предупреждения: {warnings} · актов без замечаний: {ok}";
        StatusText = report.Problems.Count > 0
            ? "Проверка завершена. " + report.Problems[0]
            : "Проверка завершена. Цвет строки — худший статус. Подробности на листе «Замечания» и в файле Excel.";
        Raise(nameof(CanExport));
        Refilter();
    }

    private void Refilter()
    {
        FilteredActs.Clear();
        FilteredKits.Clear();
        FilteredFindings.Clear();
        if (_report is null)
            return;
        var needle = _filter.Trim();
        foreach (var act in _report.Acts.Where(a => Hits(needle, a.FileName, a.ElementNo, a.ActCode, a.CompletenessText, a.ActElementName)))
            FilteredActs.Add(act);
        foreach (var kit in _report.Kits.Where(k => Hits(needle, k.Cipher, k.ActName, k.ElementNo, k.Surnames, k.Signers, k.ActKitName, k.RdElementName)))
            FilteredKits.Add(kit);
        foreach (var finding in _report.Findings.Where(f => Hits(needle, f.Act, f.Kit, f.ElementNo, f.CheckName, f.ValueA, f.ValueB, f.InAct, f.InRd, f.InVerd, f.InVesr, f.Places)))
            FilteredFindings.Add(finding);
    }

    private static bool Hits(string needle, params string?[] parts)
    {
        if (needle.Length == 0)
            return true;
        return parts.Any(p => p?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true);
    }

    private void AddPath(string path, ObservableCollection<InputFile> target, string[] extensions, bool withSignature)
    {
        if (Directory.Exists(path))
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                TryAddFile(file, target, extensions, withSignature);
            return;
        }
        TryAddFile(path, target, extensions, withSignature);
    }

    private static void TryAddFile(string path, ObservableCollection<InputFile> target, string[] extensions, bool withSignature)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith("~$", StringComparison.Ordinal) || name.EndsWith(".sig", StringComparison.OrdinalIgnoreCase))
            return;
        if (!extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            return;
        if (target.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
            return;
        var caption = name;
        if (RdFileRules.IsAuxiliary(path))
            return;
        if (withSignature)
        {
            var sig = SignatureParser.FindSignature(path);
            caption += sig is null ? "  ·  нет .sig" : "  ·  подпись рядом";
        }
        target.Add(new InputFile { Path = path, Caption = caption });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
