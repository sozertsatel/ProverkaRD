namespace ProverkaRD;

/// <summary>Пути ведомостей текущего окна. Их забирает перезапуск после обновления.</summary>
public static class LedgerHandoff
{
    public static string? Verd { get; set; }
    public static string? Vesr { get; set; }
    public static bool RestoreRequested { get; set; }
}
