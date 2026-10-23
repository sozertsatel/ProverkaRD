namespace ProverkaRD.Engine;

/// <summary>
/// Обычная проверка и «Переустановить» сами качают сборку только если тег новее.
/// Ту же версию качает только отдельная кнопка «Всё равно переустановить».
/// </summary>
public static class UpdateOffer
{
    public static bool ShouldInstall(bool remoteIsNewer, bool force)
    {
        _ = force;
        return remoteIsNewer;
    }

    public static bool ShouldReinstallCurrent(bool confirmed) => confirmed;
}
