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
        var versionText = this.FindControl<TextBlock>("VersionText");
        if (versionText is not null)
            versionText.Text = AppInfo.Version;
        _rdList = this.FindControl<ListBox>("RdList") ?? throw new InvalidOperationException("Не найден список комплектов.");
        _actList = this.FindControl<ListBox>("ActList") ?? throw new InvalidOperationException("Не найден список актов.");
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

    private async void OnCheck(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_model.CanRun)
            return;
        if (_model.VerdPath is null && _model.VesrPath is null && _model.ActFiles.Count == 0 && _model.RdFiles.Count == 0)
        {
            _model.StatusText = "Нечего проверять: загрузите ВЭРД, ВЭСР, акты или комплекты.";
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
    private bool _actsOpen = true;
    private bool _rdOpen = true;
    private double _progressValue;
    private string _progressText = "";
    private bool _showProgress;
    private CheckReport? _report;
    private ActResult? _selectedAct;

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

    public string ResultLine => _resultLine;
    public string ResultDetails => _resultDetails;
    public bool HasResult => _hasResult;
    public bool HasDetails => _hasResult && _resultDetails.Length > 0;
    public bool ShowStateDetail => !string.IsNullOrWhiteSpace(_stateDetail);

    public bool ActsOpen
    {
        get => _actsOpen;
        set { _actsOpen = value; Raise(); }
    }

    public bool RdOpen
    {
        get => _rdOpen;
        set { _rdOpen = value; Raise(); }
    }

    public ActResult? SelectedAct
    {
        get => _selectedAct;
        set
        {
            _selectedAct = value;
            Raise();
            Raise(nameof(HasSelectedAct));
            Raise(nameof(SelectedActTitle));
            Raise(nameof(SelectedRemarksEmpty));
            FillRemarks();
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
        Raise(nameof(HasDetails));
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
        Raise(nameof(HasDetails));
        if (report.LockedFiles > 0)
            SetState("locked", "Файл занят другой программой", "Закройте файл в Excel или Word и повторите проверку.");
        else if (report.VerdUnreadable || report.VesrUnreadable)
            SetState("unreadable", "Ведомость не читается", FirstProblem(report, "ВЭРД или ВЭСР не удалось прочитать."));
        else if (report.VerdMissing || report.VesrMissing)
            SetState("missing", "Нет ведомости", MissingText(report));
        else
            SetState("finished", "Проверка завершена", "");
        StatusText = _stateTitle;
        ActsOpen = false;
        RdOpen = false;
        SelectedAct = null;
        Raise(nameof(CanExport));
        Refilter();
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
                     .OrderBy(a => Rank(a.Overall))
                     .ThenBy(a => a.ElementNo, Comparer<string>.Create(ElementOrder.Compare)))
            FilteredActs.Add(act);
        foreach (var kit in _report.Kits
                     .Where(k => Hits(needle, k.Cipher, k.ActName, k.ElementNo, k.Surnames, k.Signers, k.ActKitName, k.RdElementName))
                     .OrderBy(k => Rank(k.Overall))
                     .ThenBy(k => k.ElementNo, Comparer<string>.Create(ElementOrder.Compare)))
            FilteredKits.Add(kit);
        foreach (var finding in _report.Findings
                     .Where(f => Hits(needle, f.Act, f.Kit, f.ElementNo, f.CheckName, f.ValueA, f.ValueB, f.InAct, f.InRd, f.InVerd, f.InVesr, f.Places, f.ErrorWhere))
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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
