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

    /// <summary>
    /// Письмо и любой другой файл, в имени и на титуле которого нет шифра РД.
    /// Это не комплект: подпись и «вне актов» для него не проверяются.
    /// </summary>
    public static bool IsStray(RdDocument rd) =>
        string.IsNullOrWhiteSpace(rd.CipherFromFileName)
        && string.IsNullOrWhiteSpace(rd.CipherFromText);
}
