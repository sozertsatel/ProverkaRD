namespace ProverkaRD.Engine;

public static class FileProblems
{
    public static bool IsLocked(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is IOException io)
            {
                var code = io.HResult & 0xFFFF;
                if (code is 32 or 33)
                    return true;
            }

            var message = current.Message ?? "";
            if (message.Contains("being used by another", StringComparison.OrdinalIgnoreCase)
                || message.Contains("used by another process", StringComparison.OrdinalIgnoreCase)
                || message.Contains("sharing violation", StringComparison.OrdinalIgnoreCase)
                || message.Contains("занят другим", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool IsLockedMessage(string? message) =>
        !string.IsNullOrWhiteSpace(message)
        && message.Contains("занят другой программой", StringComparison.OrdinalIgnoreCase);

    public static string Describe(string path, Exception ex)
    {
        var name = SafePath.FileName(path);
        if (IsLocked(ex))
            return "Файл занят другой программой: " + name + ". Закройте его и повторите проверку.";
        return "Не удалось прочитать " + name + ": " + ex.Message;
    }
}
