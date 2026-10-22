using ProverkaRD.Engine;

namespace ProverkaRD;

/// <summary>Журнал в %LOCALAPPDATA%\ProverkaRD\logs. Тексты документов не пишутся, только имена файлов.</summary>
public static class RunLog
{
    private const long MaxBytes = 1_000_000;

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProverkaRD",
        "logs");

    public static string FilePath => Path.Combine(Folder, "proverkard.log");

    public static void Line(string text)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            Roll();
            File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + text + Environment.NewLine);
        }
        catch
        {
            // Журнал не должен останавливать проверку.
        }
    }

    public static void Error(string context, Exception ex)
    {
        Line(context + ": " + ex.GetType().Name + ": " + ex.Message);
        if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            Line(ex.StackTrace);
    }

    public static void Summary(CheckReport report, CheckInput input)
    {
        var names = new List<string>();
        void Take(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                names.Add(SafePath.FileName(path));
        }

        Take(input.VerdPath);
        Take(input.VesrPath);
        foreach (var path in input.ActPaths)
            Take(path);
        foreach (var path in input.RdPaths)
            Take(path);
        Line("Проверка. Файлы: " + string.Join(", ", names)
             + ". Актов: " + report.Acts.Count
             + ". Замечаний: " + report.Findings.Count
             + ". Пропусков: " + report.Notices.Count
             + ". Занятых файлов: " + report.LockedFiles + ".");
    }

    public static void Open()
    {
        Directory.CreateDirectory(Folder);
        if (!File.Exists(FilePath))
            File.WriteAllText(FilePath, "");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(FilePath) { UseShellExecute = true });
    }

    private static void Roll()
    {
        if (!File.Exists(FilePath))
            return;
        if (new FileInfo(FilePath).Length < MaxBytes)
            return;
        var previous = Path.Combine(Folder, "proverkard.1.log");
        if (File.Exists(previous))
            File.Delete(previous);
        File.Move(FilePath, previous);
    }
}
