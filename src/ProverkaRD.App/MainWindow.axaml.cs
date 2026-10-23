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
    private readonly ListBox _rdList;
    private readonly ListBox _actList;
    private AppSettings _settings = AppSettings.Load();

    public MainWindow()
    {
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
        _rdList = this.FindControl<ListBox>("RdList") ?? throw new InvalidOperationException("Не найден список комплектов.");
        _actList = this.FindControl<ListBox>("ActList") ?? throw new InvalidOperationException("Не найден список актов.");
        _model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(WorkspaceModel.HasSelectedAct))
                SyncActSplit();
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

    private void OnClearRd(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _model.ClearRd();
    private void OnClearActs(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _model.ActFiles.Clear();

    private void OnRdDrop(object? sender, DragEventArgs e)
    {
        if (_model.Busy)
            return;
        DropInto(e, _model.AddRd);
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

        _model.BeginCheck();
        var input = _model.BuildInput();
        var progress = new Progress<CheckProgress>(step =>
            Dispatcher.UIThread.Post(() => _model.ReportProgress(step)));
        try
        {
            var report = await Task.Run(() => new CheckingEngine().Run(input, progress));
            _model.Apply(report);
            RunLog.Summary(report, input);
        }
        catch (Exception ex)
        {
            RunLog.Error("Проверка", ex);
            _model.Fail(ex.Message);
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
    private CheckReport? _report;
    private ActResult? _selectedAct;
    private bool _showPairing;
    private int _pairingGeneration;
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
            Raise(nameof(ListsCaption));
            SchedulePairing();
        };
        ActFiles.CollectionChanged += (_, _) =>
        {
            Raise(nameof(ActEmpty));
            Raise(nameof(ListsCaption));
            SchedulePairing();
        };
    }

    public ObservableCollection<PairingLine> PairingLines { get; } = [];
    public bool ShowPairing => _showPairing;

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
        (_listsOpen ? "▼" : "▶") + "   Акты: " + ActFiles.Count + "   ·   Комплекты: " + RdFiles.Count;

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

    public void BeginCheck()
    {
        Busy = true;
        _showProgress = true;
        _progressValue = 0;
        _progressText = "";
        _hasResult = false;
        Raise(nameof(HasResult));
        Raise(nameof(ShowCounters));
        Raise(nameof(ResultLine));
        SetState("checking", "Идёт проверка", "Подготовка списка файлов.");
        Raise(nameof(ShowProgress));
        Raise(nameof(ProgressValue));
        Raise(nameof(ProgressText));
    }

    public void ReportProgress(CheckProgress step)
    {
        _showProgress = true;
        _progressValue = step.Total <= 0 ? 0 : (double)step.Done / step.Total;
        _progressText = step.Done + " из " + step.Total + ": " + step.FileName;
        SetState("checking", "Идёт проверка", _progressText);
        Raise(nameof(ShowProgress));
        Raise(nameof(ProgressValue));
        Raise(nameof(ProgressText));
    }

    public void Fail(string message)
    {
        _showProgress = false;
        SetState("failed", "Проверка не выполнена", message);
        StatusText = "Проверка не выполнена: " + message;
        Raise(nameof(ShowProgress));
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

    public void AddRd(string path)
    {
        var folder = Directory.Exists(path);
        if (folder)
        {
            var full = Path.GetFullPath(path);
            if (!RdFolders.Any(item => string.Equals(item, full, StringComparison.OrdinalIgnoreCase)))
                RdFolders.Add(full);
        }

        var added = AddPath(path, RdFiles, [".pdf", ".docx"], withSignature: true);
        if (folder && added == 0)
            MarkEmptyFolder("В папке нет комплектов РД (PDF или DOCX). Файлы .СВОР и _ИЗМ в список не попадают.");
    }

    public void ClearRd()
    {
        RdFiles.Clear();
        RdFolders.Clear();
    }
    public void AddAct(string path)
    {
        var folder = Directory.Exists(path);
        var added = AddPath(path, ActFiles, [".docx"], withSignature: false);
        if (folder && added == 0)
            MarkEmptyFolder("В папке нет актов (DOCX).");
    }

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
        _showProgress = false;
        Raise(nameof(ShowProgress));
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

    private void SchedulePairing()
    {
        var generation = Interlocked.Increment(ref _pairingGeneration);
        var actPaths = ActFiles.Select(file => file.Path).ToList();
        var rdPaths = RdFiles.Select(file => file.Path).ToList();
        _ = Task.Run(async () =>
        {
            await Task.Delay(250);
            if (generation != Volatile.Read(ref _pairingGeneration))
                return;
            ActSetPreview.Result? preview = null;
            if (actPaths.Count > 0 || rdPaths.Count > 0)
            {
                try
                {
                    preview = BuildPairing(actPaths, rdPaths);
                }
                catch (Exception ex)
                {
                    RunLog.Error("Акт ↔ комплекты", ex);
                }
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (generation != Volatile.Read(ref _pairingGeneration))
                    return;
                ApplyPairing(preview);
            });
        });
    }

    private static ActSetPreview.Result BuildPairing(List<string> actPaths, List<string> rdPaths)
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

        return ActSetPreview.Build(acts, rdPaths);
    }

    private void ApplyPairing(ActSetPreview.Result? preview)
    {
        PairingLines.Clear();
        if (preview is not null)
        {
            foreach (var line in preview.Lines)
                PairingLines.Add(new PairingLine { Text = line.Text, Missing = line.Missing });
            foreach (var extra in preview.Unmatched)
                PairingLines.Add(new PairingLine { Text = extra, Missing = true });
        }

        var show = PairingLines.Count > 0;
        if (show == _showPairing)
            return;
        _showPairing = show;
        Raise(nameof(ShowPairing));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class PairingLine
{
    public required string Text { get; init; }
    public bool Missing { get; init; }
}
