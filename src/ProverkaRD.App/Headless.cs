using ProverkaRD.Engine;

namespace ProverkaRD;

static class Headless
{
    public static int Run(string[] args)
    {
        string? verd = null;
        string? vesr = null;
        string? output = null;
        var acts = new List<string>();
        var rd = new List<string>();

        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg)
                {
                    case "--check":
                        break;
                    case "--help":
                    case "-h":
                        PrintHelp();
                        return 0;
                    case "--verd":
                        verd = Next(args, ref i, arg);
                        break;
                    case "--vesr":
                        vesr = Next(args, ref i, arg);
                        break;
                    case "--out":
                        output = Next(args, ref i, arg);
                        break;
                    case "--act":
                        acts.Add(Next(args, ref i, arg));
                        break;
                    case "--acts":
                        acts.AddRange(Expand(Next(args, ref i, arg), ".docx"));
                        break;
                    case "--rd":
                        rd.Add(Next(args, ref i, arg));
                        break;
                    case "--rds":
                        rd.AddRange(Expand(Next(args, ref i, arg), ".pdf", ".docx"));
                        break;
                    default:
                        Console.Error.WriteLine("Неизвестный аргумент: " + arg);
                        PrintHelp();
                        return 2;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            Console.Error.WriteLine("Укажите --out путь к файлу отчёта.");
            return 2;
        }

        var report = new CheckingEngine().Run(new CheckInput
        {
            VerdPath = verd,
            VesrPath = vesr,
            ActPaths = acts,
            RdPaths = rd,
        });
        ReportWriter.Write(report, output, AppInfo.Version);

        var critical = report.Findings.Count(f => f.Severity == Severity.Critical);
        var formatting = report.Findings.Count(f => f.Severity == Severity.Formatting);
        var warnings = report.Findings.Count(f => f.Severity == Severity.Warning);
        Console.WriteLine("Отчёт записан: " + output);
        Console.WriteLine($"Актов: {report.Acts.Count}. Комплектов: {report.Kits.Count}. Критично: {critical}. Оформление: {formatting}. Предупреждения: {warnings}.");
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Проверка РД " + AppInfo.Version + " — сверка акта, ВЭРД, ВЭСР и комплектов.");
        Console.WriteLine("ProverkaRD --check --verd etap.xlsx --vesr vesr.xlsx --acts папка_актов --rds папка_рд --out отчёт.xlsx");
    }

    private static string Next(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
            throw new InvalidOperationException("Нет значения у параметра " + name);
        return args[++index];
    }

    private static IEnumerable<string> Expand(string path, params string[] extensions)
    {
        if (File.Exists(path))
            return [path];
        if (!Directory.Exists(path))
            throw new FileNotFoundException("Не найден файл или папка.", path);
        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Where(file => extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                           && !Path.GetFileName(file).StartsWith("~$", StringComparison.Ordinal)
                           && !RdFileRules.IsAuxiliary(file));
    }
}
