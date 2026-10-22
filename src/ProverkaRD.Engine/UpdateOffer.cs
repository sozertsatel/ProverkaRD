namespace ProverkaRD.Engine;

/// <summary>
/// Обычная проверка ставит сборку только если тег новее.
/// Принудительное обновление скачивает последний релиз и при той же версии.
/// </summary>
public static class UpdateOffer
{
    public static bool ShouldInstall(bool remoteIsNewer, bool force) => force || remoteIsNewer;
}
