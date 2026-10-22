using ProverkaRD.Engine;

namespace ProverkaRD.Engine.Parsing;

/// <summary>
/// Excel и Word держат файл с правом записи. Чтение с FileShare.Read
/// тогда получает «файл занят другим процессом» и ведомость или акт остаются пустыми.
/// </summary>
static class SharedRead
{
    public static byte[] Bytes(string path)
    {
        using var stream = new FileStream(
            SafePath.ForIo(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static MemoryStream Stream(string path) => new(Bytes(path), writable: false);
}
