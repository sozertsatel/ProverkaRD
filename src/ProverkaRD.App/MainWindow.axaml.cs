using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ProverkaRD.Engine;
using ProverkaRD.Engine.Parsing;

namespace ProverkaRD;

public partial class MainWindow : Window
{
    private readonly WorkspaceModel _model = new();
    private readonly DispatcherTimer _elapsedTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _work;
    private double _inputHeight = 240;
    private AppSettings _settings = AppSettings.Load();

    public MainWindow()
    {
        _elapsedTimer.Tick += (_, _) => _model.RaiseElapsed();
        _model.Options = _settings.ToOptions();
        DataContext = _model;
        AvaloniaXamlLoader.Load(this);
        Title = AppInfo.WindowTitle;
        var productTitle = this.FindControl<TextBlock>("ProductTitle");
        if (productTitle is not null)
            productTitle.Text = ProductBrand.Name;
        var versionText = this.FindControl<TextBlock>("VersionText");
        if (versionText is not null)
            versionText.Text = AppInfo.Version;
        _model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(WorkspaceModel.HasSelectedAct))
                SyncActSplit();
            if (args.PropertyName is nameof(WorkspaceModel.ListsOpen))
                SyncInputSplit();
            if (args.PropertyName is nameof(WorkspaceModel.ShowProgress))
            {
                if (_model.ShowProgress)
                    _elapsedTimer.Start();
                else
                    _elapsedTimer.Stop();
            }
        };
        RestoreLedgers();
        Opened += (_, _) =>
        {
            if (_settings.CheckUpdatesOnStart)
                _ = UpdateFlow.RunAsync(this, interactive: false);
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

    private async void OnAddRdFiles(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await PickFiles("Комплекты РД", true, "*.pdf", "*.docx");
        if (files.Count > 0)
            await LoadRd(files);
    }

    private async void OnAddActFiles(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await AddFiles(_model.AddAct, "Акты сдачи-приёмки", true, "*.docx");

    private async void OnAddRdFolder(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folder = await PickFolder("Папка с комплектами РД");
        if (folder is not null)
            await LoadRd([folder]);
    }

    private async void OnAddActFolder(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folder = await PickFolder("Папка с актами");
        if (folder is not null)
            _model.AddAct(folder);
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _work?.Cancel();

    private void OnClearRd(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _model.ClearRd();
    private void OnClearActs(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _model.ClearActs();

    private async void OnRdDrop(object? sender, DragEventArgs e)
    {
        if (_model.Busy)
            return;
        var paths = DroppedPaths(e);
        if (paths.Count > 0)
            await LoadRd(paths);
    }

    private void OnActDrop(object? sender, DragEventArgs e)
    {
        if (_model.Busy)
            return;
        DropInto(e, _model.AddAct);
    }

    private void OnExcelDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnVerdDrop(object? sender, DragEventArgs e) =>
        DropWorkbook(e, path => _model.VerdPath = path, "ВЭРД");

    private void OnVesrDrop(object? sender, DragEventArgs e) =>
        DropWorkbook(e, path => _model.VesrPath = path, "ВЭСР");

    private void OnToggleLists(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _model.ListsOpen = !_model.ListsOpen;

    private void OnInputContext(object? sender, ContextRequestedEventArgs e)
    {
        var node = FindNode(e.Source);
        if (node is null || node.Kind is not ("act" or "file") || string.IsNullOrWhiteSpace(node.Path))
        {
            e.Handled = true;
            return;
        }

        var item = new MenuItem { Header = "Удалить" };
        item.Click += (_, _) => _model.RemoveOutlineNode(node.Kind, node.Path);
        var menu = new ContextMenu { Items = { item } };
        var target = e.Source as Control ?? this;
        menu.Open(target);
        e.Handled = true;
    }

    private static InputNode? FindNode(object? source)
    {
        var current = source as StyledElement;
        while (current is not null)
        {
            if (current.DataContext is InputNode node)
                return node;
            current = current.Parent as StyledElement;
        }

        return null;
    }

    private void SyncInputSplit()
    {
        var grid = this.FindControl<Grid>("InputSplit");
        if (grid is null || grid.RowDefinitions.Count < 3)
            return;
        if (!_model.ListsOpen)
        {
            if (grid.RowDefinitions[0].Height.GridUnitType == GridUnitType.Pixel && grid.RowDefinitions[0].Height.Value >= 120)
                _inputHeight = grid.RowDefinitions[0].Height.Value;
            grid.RowDefinitions[0].Height = GridLength.Auto;
            grid.RowDefinitions[0].MinHeight = 0;
            grid.RowDefinitions[1].Height = new GridLength(0);
            return;
        }

        grid.RowDefinitions[1].Height = new GridLength(8);
        grid.RowDefinitions[0].MinHeight = 160;
        grid.RowDefinitions[0].Height = new GridLength(_inputHeight < 160 ? 240 : _inputHeight);
    }

    private void OnFilterColumn(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string key)
            return;
        e.Handled = true;
        var values = _model.DistinctColumnValues(key);
        var active = _model.ColumnFilter(key);
        var boxes = new List<(string Value, CheckBox Box)>();
        var list = new StackPanel { Spacing = 2 };
        foreach (var value in values)
        {
            var box = new CheckBox
            {
                Content = value.Length == 0 ? "(пусто)" : value,
                IsChecked = active is null || active.Contains(value),
            };
            boxes.Add((value, box));
            list.Children.Add(box);
        }

        if (values.Count == 0)
            list.Children.Add(new TextBlock { Text = "Нет значений", Foreground = Brushes.Gray });

        var root = new StackPanel { Spacing = 8, Margin = new Thickness(10), MinWidth = 200 };
        root.Children.Add(new ScrollViewer { MaxHeight = 280, Content = list });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var all = new Button { Content = "Все" };
        var apply = new Button { Content = "Применить" };
        var reset = new Button { Content = "Сбросить" };
        Flyout? flyout = null;
        all.Click += (_, _) =>
        {
            foreach (var (_, box) in boxes)
                box.IsChecked = true;
        };
        reset.Click += (_, _) =>
        {
            _model.ClearColumnFilter(key);
            flyout?.Hide();
        };
        apply.Click += (_, _) =>
        {
            var picked = boxes.Where(item => item.Box.IsChecked == true).Select(item => item.Value).ToList();
            _model.SetColumnFilter(key, picked, values.Count);
            flyout?.Hide();
        };
        buttons.Children.Add(all);
        buttons.Children.Add(apply);
        buttons.Children.Add(reset);
        root.Children.Add(buttons);
        flyout = new Flyout { Content = root, Placement = PlacementMode.Bottom };
        flyout.ShowAt(button);
    }

    private void SyncActSplit()
    {
        var grid = this.FindControl<Grid>("ActSplit");
        if (grid is null || grid.RowDefinitions.Count < 3)
            return;
        if (!_model.HasSelectedAct)
        {
            grid.RowDefinitions[1].Height = new GridLength(0);
            grid.RowDefinitions[2].Height = new GridLength(0);
            return;
        }

        var bottom = grid.RowDefinitions[2].Height;
        var collapsed = bottom.GridUnitType != GridUnitType.Star && bottom.Value < 40;
        if (collapsed)
        {
            grid.RowDefinitions[1].Height = new GridLength(8);
            grid.RowDefinitions[2].Height = new GridLength(220);
        }
    }

    private async void OnCheck(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_model.CanRun)
            return;
        var warning = LedgerGate.Warning(_model.VerdPath, _model.VesrPath);
        if (warning.Length > 0)
        {
            _model.RejectLedgers(warning);
            return;
        }

        var token = BeginWork();
        var generation = _model.BeginCheck();
        var input = _model.BuildInput();
        var progress = new Progress<CheckProgress>(step =>
        {
            if (generation == _model.ProgressGeneration)
                _model.ReportProgress(step);
        });
        try
        {
            var report = await Task.Run(() => new CheckingEngine().Run(input, progress, token), token);
            _model.Apply(report);
            RunLog.Summary(report, input);
        }
        catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested)
        {
            _model.MarkCancelled("Проверка остановлена.");
        }
        catch (Exception ex)
        {
            RunLog.Error("Проверка", ex);
            _model.Fail(ex.Message);
        }
        finally
        {
            _model.Busy = false;
            _model.HideProgress();
        }
    }

    private CancellationToken BeginWork()
    {
        _work?.Cancel();
        _work?.Dispose();
        _work = new CancellationTokenSource();
        return _work.Token;
    }

    private async Task LoadRd(IReadOnlyList<string> paths)
    {
        if (_model.Busy || paths.Count == 0)
            return;
        var token = BeginWork();
        var generation = _model.BeginLoad();
        var batch = new RdLoadBatch();
        var progress = new Progress<CheckProgress>(step =>
        {
            if (generation == _model.ProgressGeneration)
                _model.ReportProgress(step);
        });
        var cancelled = false;
        try
        {
            await Task.Run(() => FillRdBatch(paths, batch, progress, token), token);
            _model.CommitRd(batch.Folders, batch.Files);
            if (batch.EmptyFolder && batch.Files.Count == 0)
                _model.MarkEmptyFolder("В папке нет комплектов РД (PDF или DOCX).");
        }
        catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested)
        {
            cancelled = true;
            _model.CommitRd(batch.Folders, batch.Files);
            _model.MarkCancelled("Загрузка остановлена. Уже добавленные файлы сохранены.");
        }
        catch (Exception ex)
        {
            RunLog.Error("Загрузка комплектов", ex);
            _model.Fail(ex.Message);
        }
        finally
        {
            _model.Busy = false;
            if (!cancelled)
                _model.HideProgress();
        }
    }

    private static void FillRdBatch(IReadOnlyList<string> sources, RdLoadBatch batch, IProgress<CheckProgress> progress, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(source))
                continue;
            var io = SafePath.ForIo(source);
            if (Directory.Exists(io))
            {
                batch.Folders.Add(Path.GetFullPath(source));
                batch.SawFolder = true;
                var before = files.Count;
                IEnumerable<string> found;
                try
                {
                    found = Directory.EnumerateFiles(io, "*", SearchOption.AllDirectories);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var file in found)
                    ConsiderRd(file, files);
                if (files.Count == before)
                    batch.EmptyFolder = true;
            }
            else
            {
                ConsiderRd(io, files);
            }
        }

        if (batch.SawFolder && files.Count > 0)
            batch.EmptyFolder = false;

        var total = files.Count;
        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = files[index];
            progress.Report(new CheckProgress(
                CheckStages.LoadKits,
                index + 1,
                total,
                Path.GetFileName(path),
                total == 0 ? 1 : (index + 1) / (double)total));
            var hasSignature = false;
            try
            {
                hasSignature = SignatureParser.FindSignature(path) is not null;
            }
            catch (Exception ex)
            {
                RunLog.Error("Подпись рядом", ex);
            }

            batch.Files.Add((path, hasSignature));
        }
    }

    private static void ConsiderRd(string path, List<string> files)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith("~$", StringComparison.Ordinal) || name.EndsWith(".sig", StringComparison.OrdinalIgnoreCase))
            return;
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
            return;
        if (RdFileRules.IsAuxiliary(path))
            return;
        if (files.Any(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase)))
            return;
        files.Add(path);
    }

    private static List<string> DroppedPaths(DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
            return [];
        return files.Select(file => file.TryGetLocalPath()).Where(path => path is not null).Cast<string>().ToList();
    }

    private sealed class RdLoadBatch
    {
        public List<string> Folders { get; } = [];
        public List<(string Path, bool HasSignature)> Files { get; } = [];
        public bool SawFolder { get; set; }
        public bool EmptyFolder { get; set; }
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
            SuggestedFileName = StageLabel.ReportFileName(_model.Report.Stage, DateTime.Now),
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

    private void DropWorkbook(DragEventArgs e, Action<string> set, string title)
    {
        if (_model.Busy)
            return;
        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
            return;
        var paths = files.Select(file => file.TryGetLocalPath()).Where(path => path is not null).Cast<string>().ToList();
        if (paths.Count == 0)
            return;
        if (paths.Any(Directory.Exists))
        {
            _model.StatusText = title + ": перетащите файл .xlsx, а не папку.";
            return;
        }

        var workbook = paths.FirstOrDefault(path => path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase));
        if (workbook is null)
        {
            _model.StatusText = title + ": нужен файл .xlsx.";
            return;
        }

        set(workbook);
        _model.StatusText = title + ": " + Path.GetFileName(workbook);
    }

    private void RestoreLedgers()
    {
        LedgerSession.Paths? saved;
        try
        {
            saved = LedgerSession.TakeAfterUpdate(AppSettings.Folder, LedgerHandoff.RestoreRequested);
        }
        catch
        {
            return;
        }

        if (saved is null)
            return;
        if (!string.IsNullOrWhiteSpace(saved.VerdPath))
            _model.VerdPath = saved.VerdPath;
        if (!string.IsNullOrWhiteSpace(saved.VesrPath))
            _model.VesrPath = saved.VesrPath;
        if (_model.VerdPath is not null || _model.VesrPath is not null)
            _model.StatusText = "Ведомости восстановлены после обновления.";
    }
}

public sealed class InputFile
{
    public required string Path { get; init; }
    public required string Caption { get; init; }
    public bool HasSignature { get; init; }
}

public sealed class InputNode
{
    public required string Title { get; init; }
    public required string Kind { get; init; }
    public string Path { get; init; } = "";
    public bool Problem { get; init; }
    public bool Expand { get; init; }
    public string Mark { get; init; } = "";
    public ObservableCollection<InputNode> Children { get; init; } = [];
}

public sealed class WorkspaceModel : INotifyPropertyChanged
{
    private string? _verdPath;
    private string? _vesrPath;
    private bool _busy;
    private string _status = "Загрузите ВЭРД и ВЭСР, добавьте акты и комплекты, затем нажмите «Проверить».";
    private string _filter = "";
    private string _stateKind = "idle";
    private string _stateTitle = "Ожидание";
    private string _stateDetail = "Загрузите ВЭРД и ВЭСР, добавьте акты и комплекты, затем нажмите «Проверить».";
    private string _resultLine = "";
    private string _resultDetails = "";
    private bool _hasResult;
    private bool _listsOpen = true;
    private double _progressValue;
    private string _progressText = "";
    private bool _showProgress;
    private int _progressGeneration;
    private readonly System.Diagnostics.Stopwatch _elapsedClock = new();
    private CheckReport? _report;
    private ActResult? _selectedAct;
    private int _outlineGeneration;
    private bool _outlineQuiet;
    private string _outlineSummary = "Акты и комплекты не добавлены";
    private readonly HashSet<string> _hiddenOutline = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _columnFilters = new(StringComparer.Ordinal);

    public ObservableCollection<InputFile> RdFiles { get; } = [];
    public ObservableCollection<InputFile> ActFiles { get; } = [];
    public List<string> RdFolders { get; } = [];
    public ObservableCollection<ActResult> FilteredActs { get; } = [];
    public ObservableCollection<KitResult> FilteredKits { get; } = [];
    public ObservableCollection<Finding> FilteredFindings { get; } = [];
    public ObservableCollection<Notice> FilteredNotices { get; } = [];
    public ObservableCollection<Finding> SelectedRemarks { get; } = [];

    public WorkspaceModel()
    {
        RdFiles.CollectionChanged += (_, _) =>
        {
            Raise(nameof(RdEmpty));
            Raise(nameof(RdCountText));
            Raise(nameof(ListsCaption));
            if (!_outlineQuiet)
                ScheduleOutline();
        };
        ActFiles.CollectionChanged += (_, _) =>
        {
            Raise(nameof(ActEmpty));
            Raise(nameof(ActCountText));
            Raise(nameof(ListsCaption));
            if (!_outlineQuiet)
                ScheduleOutline();
        };
    }

    public ObservableCollection<InputNode> InputNodes { get; } = [];
    public string OutlineSummary => _outlineSummary;
    public bool OutlineEmpty => InputNodes.Count == 0;
    public string ActCountText => InputOutline.CountPhrase(ActFiles.Count, "файл", "файла", "файлов");
    public string RdCountText => InputOutline.CountPhrase(RdFiles.Count, "файл", "файла", "файлов");

    public string? VerdPath
    {
        get => _verdPath;
        set
        {
            _verdPath = value;
            LedgerHandoff.Verd = value;
            RememberLedgers();
            Raise(nameof(VerdCaption));
        }
    }

    public string? VesrPath
    {
        get => _vesrPath;
        set
        {
            _vesrPath = value;
            LedgerHandoff.Vesr = value;
            RememberLedgers();
            Raise(nameof(VesrCaption));
        }
    }

    private static void RememberLedgers()
    {
        try
        {
            LedgerSession.Write(AppSettings.Folder, LedgerHandoff.Verd, LedgerHandoff.Vesr);
        }
        catch
        {
            // Пути остаются в окне, даже если файл сессии не записался.
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

    public string ResultLine => _resultLine;
    public string ResultDetails => _resultDetails;
    public bool HasResult => _hasResult;
    public bool ShowCounters => _hasResult && _resultDetails.Length > 0;
    public bool ShowStateDetail => !string.IsNullOrWhiteSpace(_stateDetail);

    public bool ListsOpen
    {
        get => _listsOpen;
        set
        {
            _listsOpen = value;
            Raise();
            Raise(nameof(ListsCaption));
        }
    }

    public string ListsCaption =>
        (_listsOpen ? "▼" : "▶") + "   " + (_listsOpen || _outlineSummary.Length == 0
            ? "Акты: " + ActFiles.Count + "   ·   Комплекты: " + RdFiles.Count
            : _outlineSummary);

    public ActResult? SelectedAct
    {
        get => _selectedAct;
        set
        {
            _selectedAct = value;
            Raise();
            Raise(nameof(HasSelectedAct));
            Raise(nameof(SelectedActTitle));
            Raise(nameof(SelectedKitsText));
            Raise(nameof(SelectedRemarksEmpty));
            FillRemarks();
        }
    }

    public string SelectedKitsText
    {
        get
        {
            if (_selectedAct is null || _report is null)
                return "";
            var kits = _report.Kits.Where(kit => ActScore.Concerns(_selectedAct, kit)).ToList();
            if (kits.Count == 0)
                return "Комплектов по этому акту нет.";
            var lines = kits.Select(kit =>
            {
                var cipher = string.IsNullOrWhiteSpace(kit.Cipher) ? "без шифра" : kit.Cipher;
                var file = string.IsNullOrWhiteSpace(kit.RdFileName) ? kit.Presence : kit.RdFileName;
                return string.IsNullOrWhiteSpace(file) ? cipher : cipher + " · " + file;
            });
            return "Комплекты (" + kits.Count + "): " + string.Join("; ", lines);
        }
    }

    public bool HasSelectedAct => _selectedAct is not null;
    public bool SelectedRemarksEmpty => _selectedAct is not null && SelectedRemarks.Count == 0;
    public string SelectedActTitle => _selectedAct is null
        ? ""
        : "Замечания" + (_selectedAct.ElementNo.Length > 0 ? " · " + _selectedAct.ElementNo : "") + " · " + _selectedAct.FileName;

    public string StateKind => _stateKind;
    public string StateTitle => _stateTitle;
    public string StateDetail => _stateDetail;
    public double ProgressValue => _progressValue;
    public string ProgressText => _progressText;
    public bool ShowProgress => _showProgress;
    public string ElapsedText => _showProgress ? CheckProgressText.Elapsed(_elapsedClock.Elapsed) : "";
    public int ProgressGeneration => _progressGeneration;

    public void RaiseElapsed()
    {
        if (_showProgress)
            Raise(nameof(ElapsedText));
    }

    public int BeginLoad()
    {
        _progressGeneration++;
        Busy = true;
        _showProgress = true;
        _progressValue = 0;
        _progressText = "Загрузка комплектов";
        _elapsedClock.Restart();
        SetState("checking", "Загрузка комплектов", "");
        Raise(nameof(ShowProgress));
        Raise(nameof(ProgressValue));
        Raise(nameof(ProgressText));
        Raise(nameof(ElapsedText));
        return _progressGeneration;
    }

    public int BeginCheck()
    {
        _progressGeneration++;
        Busy = true;
        _showProgress = true;
        _progressValue = 0;
        _progressText = "Подготовка списка файлов.";
        _hasResult = false;
        _elapsedClock.Restart();
        Raise(nameof(HasResult));
        Raise(nameof(ShowCounters));
        Raise(nameof(ResultLine));
        SetState("checking", "Идёт проверка", "");
        Raise(nameof(ShowProgress));
        Raise(nameof(ProgressValue));
        Raise(nameof(ProgressText));
        Raise(nameof(ElapsedText));
        return _progressGeneration;
    }

    public void ReportProgress(CheckProgress step)
    {
        _showProgress = true;
        _progressValue = double.IsFinite(step.Overall) ? Math.Clamp(step.Overall, 0, 1) : 0;
        _progressText = step.Text;
        var title = step.Stage == CheckStages.LoadKits ? "Загрузка комплектов" : "Идёт проверка";
        SetState("checking", title, "");
        Raise(nameof(ShowProgress));
        Raise(nameof(ProgressValue));
        Raise(nameof(ProgressText));
        Raise(nameof(ElapsedText));
    }

    public void HideProgress()
    {
        var checking = _stateKind == "checking";
        DismissProgress();
        Raise(nameof(ShowProgress));
        Raise(nameof(ElapsedText));
        if (!checking)
            return;
        if (_report is not null)
            SetState("finished", "Проверка завершена", "");
        else
            SetState("idle", "Ожидание", "Загрузите ВЭРД и ВЭСР, добавьте акты и комплекты, затем нажмите «Проверить».");
        StatusText = _stateTitle;
    }

    public void MarkCancelled(string detail)
    {
        DismissProgress();
        Busy = false;
        SetState("idle", "Отменено", detail);
        StatusText = "Отменено.";
        Raise(nameof(ShowProgress));
        Raise(nameof(ElapsedText));
    }

    private void DismissProgress()
    {
        _progressGeneration++;
        _showProgress = false;
        _elapsedClock.Stop();
    }

    public void Fail(string message)
    {
        DismissProgress();
        SetState("failed", "Проверка не выполнена", message);
        StatusText = "Проверка не выполнена: " + message;
        Raise(nameof(ShowProgress));
        Raise(nameof(ElapsedText));
    }

    public void MarkEmptyFolder(string detail)
    {
        SetState("empty", "Папка пуста", detail);
        StatusText = detail;
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

    public void CommitRd(IReadOnlyList<string> folders, IReadOnlyList<(string Path, bool HasSignature)> files)
    {
        foreach (var folder in folders)
        {
            if (!RdFolders.Any(item => string.Equals(item, folder, StringComparison.OrdinalIgnoreCase)))
                RdFolders.Add(folder);
        }

        var added = 0;
        foreach (var file in files)
        {
            if (TryAddPrepared(file.Path, file.HasSignature))
                added++;
        }

        if (folders.Count > 0 && added == 0)
            ScheduleOutline();
    }

    public void ClearRd()
    {
        _hiddenOutline.Clear();
        RdFolders.Clear();
        Quiet(() => RdFiles.Clear());
        RefreshOutlineNow();
    }

    public void ClearActs()
    {
        Quiet(() => ActFiles.Clear());
        RefreshOutlineNow();
    }

    private void Quiet(Action change)
    {
        _outlineQuiet = true;
        try
        {
            change();
        }
        finally
        {
            _outlineQuiet = false;
        }
    }

    public void RemoveOutlineNode(string kind, string path)
    {
        if (kind == "act")
        {
            foreach (var item in ActFiles.Where(file => SamePath(file.Path, path)).ToList())
                ActFiles.Remove(item);
            return;
        }

        if (kind != "file" || string.IsNullOrWhiteSpace(path))
            return;
        _hiddenOutline.Add(path);
        var hits = RdFiles.Where(file => SamePath(file.Path, path)).ToList();
        if (hits.Count == 0)
        {
            ScheduleOutline();
            return;
        }

        foreach (var item in hits)
            RdFiles.Remove(item);
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    public void AddAct(string path)
    {
        var folder = Directory.Exists(path);
        var added = AddPath(path, ActFiles, [".docx"], withSignature: false);
        if (folder && added == 0)
            MarkEmptyFolder("В папке нет актов (DOCX).");
    }

    public CheckOptions Options { get; set; } = new();

    public CheckInput BuildInput() => new()
    {
        VerdPath = VerdPath,
        VesrPath = VesrPath,
        ActPaths = ActFiles.Select(f => f.Path).ToList(),
        RdPaths = RdFiles.Select(f => f.Path).ToList(),
        RdFolders = RdFolders.ToList(),
        Options = Options,
    };

    public void Apply(CheckReport report)
    {
        _report = report;
        var critical = report.Findings.Count(f => f.Severity == Severity.Critical);
        var formatting = report.Findings.Count(f => f.Severity == Severity.Formatting);
        var warnings = report.Findings.Count(f => f.Severity == Severity.Warning);
        var ok = report.Acts.Count(a => a.Overall == Severity.Ok);
        _resultLine = $"Критично {critical} · Оформление {formatting} · Предупреждений {warnings} · Без замечаний {ok}";
        _resultDetails = report.Load.Text ?? "";
        _hasResult = true;
        DismissProgress();
        Raise(nameof(ShowProgress));
        Raise(nameof(ElapsedText));
        Raise(nameof(ResultLine));
        Raise(nameof(ResultDetails));
        Raise(nameof(HasResult));
        Raise(nameof(ShowCounters));
        if (report.LockedFiles > 0)
            SetState("locked", "Файл занят другой программой", "Закройте файл в Excel или Word и повторите проверку.");
        else if (report.VerdUnreadable || report.VesrUnreadable)
            SetState("unreadable", "Ведомость не читается", FirstProblem(report, "ВЭРД или ВЭСР не удалось прочитать."));
        else if (report.VerdMissing || report.VesrMissing)
            SetState("missing", "Нет ведомости", MissingText(report));
        else
            SetState("finished", "Проверка завершена", "");
        StatusText = _stateTitle;
        ListsOpen = false;
        _columnFilters.Clear();
        SelectedAct = null;
        Raise(nameof(CanExport));
        Refilter();
    }

    public void RejectLedgers(string warning)
    {
        SetState("missing", warning, "");
        StatusText = warning;
    }

    private static string MissingText(CheckReport report)
    {
        if (report.VerdMissing && report.VesrMissing)
            return "ВЭРД и ВЭСР не загружены.";
        return report.VerdMissing ? "ВЭРД не загружена." : "ВЭСР не загружена.";
    }

    private static string FirstProblem(CheckReport report, string fallback) =>
        report.Problems.FirstOrDefault(problem => problem.Contains("Не удалось", StringComparison.Ordinal) || problem.Contains("занят", StringComparison.Ordinal))
        ?? report.Problems.FirstOrDefault()
        ?? fallback;

    private void SetState(string kind, string title, string detail)
    {
        _stateKind = kind;
        _stateTitle = title;
        _stateDetail = detail;
        Raise(nameof(StateKind));
        Raise(nameof(StateTitle));
        Raise(nameof(StateDetail));
        Raise(nameof(ShowStateDetail));
    }

    private void Refilter()
    {
        FilteredActs.Clear();
        FilteredKits.Clear();
        FilteredFindings.Clear();
        FilteredNotices.Clear();
        if (_report is null)
            return;
        var needle = _filter.Trim();
        foreach (var act in _report.Acts
                     .Where(a => Hits(needle, a.FileName, a.ElementNo, a.ActCode, a.CompletenessText, a.ActElementName))
                     .Where(a => PassesColumn("act-overall", SeverityText.ToRu(a.Overall)) && PassesColumn("act-element", a.ElementNo))
                     .OrderBy(a => Rank(a.Overall))
                     .ThenBy(a => a.ElementNo, Comparer<string>.Create(ElementOrder.Compare)))
            FilteredActs.Add(act);
        foreach (var kit in _report.Kits
                     .Where(k => Hits(needle, k.Cipher, k.ActName, k.ElementNo, k.Surnames, k.Signers, k.ActKitName, k.RdElementName))
                     .Where(k => PassesColumn("kit-overall", SeverityText.ToRu(k.Overall)) && PassesColumn("kit-element", k.ElementNo))
                     .OrderBy(k => Rank(k.Overall))
                     .ThenBy(k => k.ElementNo, Comparer<string>.Create(ElementOrder.Compare)))
            FilteredKits.Add(kit);
        foreach (var finding in _report.Findings
                     .Where(f => Hits(needle, f.Act, f.Kit, f.ElementNo, f.CheckName, f.ValueA, f.ValueB, f.InAct, f.InRd, f.InVerd, f.InVesr, f.Places, f.ErrorWhere))
                     .Where(f => PassesColumn("finding-element", f.ElementNo) && PassesColumn("finding-where", f.ErrorWhere))
                     .OrderBy(f => Rank(f.Severity))
                     .ThenBy(f => f.ElementNo, Comparer<string>.Create(ElementOrder.Compare)))
            FilteredFindings.Add(finding);
        foreach (var notice in _report.Notices.Where(n => Hits(needle, n.Source, n.Place, n.Message)))
            FilteredNotices.Add(notice);
    }

    private void FillRemarks()
    {
        SelectedRemarks.Clear();
        if (_selectedAct is null || _report is null)
        {
            Raise(nameof(SelectedRemarksEmpty));
            return;
        }

        foreach (var finding in _report.Findings
                     .Where(item => ActScore.Concerns(_selectedAct, item))
                     .OrderBy(item => Rank(item.Severity))
                     .ThenBy(item => item.CheckName, StringComparer.CurrentCultureIgnoreCase))
            SelectedRemarks.Add(finding);
        Raise(nameof(SelectedRemarksEmpty));
    }

    private static int Rank(Severity severity) => severity switch
    {
        Severity.Critical => 0,
        Severity.Formatting => 1,
        Severity.Warning => 2,
        _ => 3,
    };

    private static bool Hits(string needle, params string?[] parts)
    {
        if (needle.Length == 0)
            return true;
        return parts.Any(p => p?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true);
    }

    private int AddPath(string path, ObservableCollection<InputFile> target, string[] extensions, bool withSignature)
    {
        var before = target.Count;
        var io = SafePath.ForIo(path);
        if (Directory.Exists(io))
        {
            foreach (var file in Directory.EnumerateFiles(io, "*", SearchOption.AllDirectories))
                TryAddFile(file, target, extensions, withSignature);
            return target.Count - before;
        }
        TryAddFile(io, target, extensions, withSignature);
        return target.Count - before;
    }

    private void TryAddFile(string path, ObservableCollection<InputFile> target, string[] extensions, bool withSignature)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith("~$", StringComparison.Ordinal) || name.EndsWith(".sig", StringComparison.OrdinalIgnoreCase))
            return;
        if (!extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            return;
        if (target.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
            return;
        if (RdFileRules.IsAuxiliary(path))
            return;
        _hiddenOutline.Remove(path);
        var hasSignature = false;
        if (withSignature)
            hasSignature = SignatureParser.FindSignature(path) is not null;
        target.Add(new InputFile { Path = path, Caption = name, HasSignature = hasSignature });
    }

    private bool TryAddPrepared(string path, bool hasSignature)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith("~$", StringComparison.Ordinal) || name.EndsWith(".sig", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Path.GetExtension(path), ".docx", StringComparison.OrdinalIgnoreCase))
            return false;
        if (RdFiles.Any(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (RdFileRules.IsAuxiliary(path))
            return false;
        _hiddenOutline.Remove(path);
        RdFiles.Add(new InputFile { Path = path, Caption = name, HasSignature = hasSignature });
        return true;
    }

    public IReadOnlyList<string> DistinctColumnValues(string key)
    {
        if (_report is null)
            return [];
        IEnumerable<string?> values = key switch
        {
            "act-overall" => _report.Acts.Select(act => SeverityText.ToRu(act.Overall)),
            "act-element" => _report.Acts.Select(act => act.ElementNo),
            "kit-overall" => _report.Kits.Select(kit => SeverityText.ToRu(kit.Overall)),
            "kit-element" => _report.Kits.Select(kit => kit.ElementNo),
            "finding-element" => _report.Findings.Select(finding => finding.ElementNo),
            "finding-where" => _report.Findings.Select(finding => finding.ErrorWhere),
            _ => [],
        };
        return values
            .Select(value => value?.Trim() ?? "")
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlySet<string>? ColumnFilter(string key) =>
        _columnFilters.TryGetValue(key, out var selected) ? selected : null;

    public void SetColumnFilter(string key, IReadOnlyCollection<string> selected, int totalDistinct)
    {
        if (totalDistinct == 0 || selected.Count >= totalDistinct)
            _columnFilters.Remove(key);
        else
            _columnFilters[key] = new HashSet<string>(selected, StringComparer.CurrentCultureIgnoreCase);
        Refilter();
    }

    public void ClearColumnFilter(string key)
    {
        if (_columnFilters.Remove(key))
            Refilter();
    }

    private bool PassesColumn(string key, string? value)
    {
        if (!_columnFilters.TryGetValue(key, out var selected))
            return true;
        return selected.Contains(value?.Trim() ?? "");
    }

    private void ScheduleOutline()
    {
        var generation = Interlocked.Increment(ref _outlineGeneration);
        var actPaths = ActFiles.Select(file => file.Path).ToList();
        var files = CollectOutlineFiles();
        _ = Task.Run(async () =>
        {
            await Task.Delay(250);
            if (generation != Volatile.Read(ref _outlineGeneration))
                return;
            InputOutline.Model? model = null;
            if (actPaths.Count > 0 || files.Count > 0)
            {
                try
                {
                    model = BuildOutline(actPaths, files);
                }
                catch (Exception ex)
                {
                    RunLog.Error("Список файлов", ex);
                }
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (generation != Volatile.Read(ref _outlineGeneration))
                    return;
                ApplyOutline(model);
            });
        });
    }

    private List<InputOutline.FileEntry> CollectOutlineFiles() =>
        InputOutline.Discover(
            RdFiles.Select(file => new InputOutline.FileEntry(file.Path, file.HasSignature)),
            RdFolders,
            _hiddenOutline).ToList();

    private void RefreshOutlineNow()
    {
        var generation = Interlocked.Increment(ref _outlineGeneration);
        var actPaths = ActFiles.Select(file => file.Path).ToList();
        var files = CollectOutlineFiles();
        InputOutline.Model? model = null;
        if (actPaths.Count > 0 || files.Count > 0)
        {
            try
            {
                model = BuildOutline(actPaths, files);
            }
            catch (Exception ex)
            {
                RunLog.Error("Список файлов", ex);
            }
        }

        if (generation != Volatile.Read(ref _outlineGeneration))
            return;
        ApplyOutline(model);
    }

    private static InputOutline.Model BuildOutline(List<string> actPaths, List<InputOutline.FileEntry> files)
    {
        var acts = new List<ActDocument>(actPaths.Count);
        foreach (var path in actPaths)
        {
            try
            {
                acts.Add(ActParser.Parse(path));
            }
            catch (Exception)
            {
                acts.Add(new ActDocument { FilePath = path });
            }
        }

        return InputOutline.Build(acts, files);
    }

    private void ApplyOutline(InputOutline.Model? model)
    {
        InputNodes.Clear();
        if (model is not null)
        {
            foreach (var node in model.Nodes)
                InputNodes.Add(MapNode(node));
        }

        var summary = model?.Summary ?? "Акты и комплекты не добавлены";
        if (!string.Equals(summary, _outlineSummary, StringComparison.Ordinal))
        {
            _outlineSummary = summary;
            Raise(nameof(OutlineSummary));
            Raise(nameof(ListsCaption));
        }

        Raise(nameof(OutlineEmpty));
    }

    private static InputNode MapNode(InputOutline.Node node)
    {
        var mapped = new InputNode
        {
            Title = node.Title,
            Kind = node.Kind,
            Path = node.Path,
            Problem = node.Problem,
            Expand = node.Expand,
            Mark = node.Kind == "act" && !node.Problem ? "✓" : "",
            Children = [],
        };
        foreach (var child in node.Children)
            mapped.Children.Add(MapNode(child));
        return mapped;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
