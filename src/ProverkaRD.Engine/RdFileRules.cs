namespace ProverkaRD.Engine;

/// <summary>Вспомогательные файлы ГСН не являются комплектами РД.</summary>
public static class RdFileRules
{
    public static bool IsAuxiliary(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var name = Path.GetFileName(path);
        return name.Contains(".СВОР", StringComparison.OrdinalIgnoreCase)
               || name.Contains("_ИЗМ", StringComparison.OrdinalIgnoreCase);
    }
}
