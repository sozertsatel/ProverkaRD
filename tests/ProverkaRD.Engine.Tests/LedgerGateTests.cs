namespace ProverkaRD.Engine.Tests;

public class LedgerGateTests
{
    [Fact]
    public void Check_names_each_missing_ledger()
    {
        Assert.Equal("", LedgerGate.Warning(@"C:\ведомости\база.xlsx", @"C:\ведомости\стоимость.xlsx"));
        Assert.Equal(["Не загружена ВЭСР"], LedgerGate.Missing(@"C:\ведомости\база.xlsx", "  "));
        Assert.Equal("Не загружена ВЭРД.", LedgerGate.Warning(null, @"C:\ведомости\стоимость.xlsx"));
        Assert.Equal("Не загружена ВЭРД. Не загружена ВЭСР.", LedgerGate.Warning("", null));
    }

    [Fact]
    public void Update_restart_restores_ledger_paths_once()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-ledgers");
        try
        {
            var verd = Path.Combine(dir.FullName, "база.xlsx");
            var vesr = Path.Combine(dir.FullName, "стоимость.xlsx");
            File.WriteAllText(Path.Combine(dir.FullName, "ledgers.json"), """{"VerdPath":"устаревший"}""");
            Assert.Null(LedgerSession.TakeAfterUpdate(dir.FullName, restoreRequested: false));

            LedgerSession.Save(dir.FullName, verd, vesr);
            var restored = LedgerSession.TakeAfterUpdate(dir.FullName, restoreRequested: false);
            Assert.NotNull(restored);
            Assert.Equal(verd, restored.VerdPath);
            Assert.Equal(vesr, restored.VesrPath);
            Assert.Null(LedgerSession.TakeAfterUpdate(dir.FullName, restoreRequested: false));

            LedgerSession.Save(dir.FullName, verd, vesr);
            File.Delete(Path.Combine(dir.FullName, "restore-after-update"));
            var byArgument = LedgerSession.TakeAfterUpdate(dir.FullName, restoreRequested: true);
            Assert.Equal(verd, byArgument?.VerdPath);
            Assert.Equal(vesr, byArgument?.VesrPath);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
