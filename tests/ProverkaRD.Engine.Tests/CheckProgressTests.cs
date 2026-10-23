namespace ProverkaRD.Engine.Tests;

public class CheckProgressTests
{
    [Fact]
    public void Stage_text_names_the_file_only_while_sets_are_loading()
    {
        Assert.Equal(
            "Загрузка комплектов: 23 из 51 — том.pdf",
            CheckProgressText.Format(CheckStages.LoadKits, 23, 51, "том.pdf"));
        Assert.Equal(
            "Проверка подписей: 12 из 54",
            CheckProgressText.Format(CheckStages.Signatures, 12, 54, "том.pdf"));
        Assert.Equal("Сверка с ВЭРД/ВЭСР", CheckProgressText.Format(CheckStages.Ledgers, 1, 3, "ведомость.xlsx"));
        Assert.Equal("Формирование отчёта", CheckProgressText.Format(CheckStages.Report, 1, 1, ""));
        Assert.Equal("прошло 0:00", CheckProgressText.Elapsed(TimeSpan.Zero));
        Assert.Equal("прошло 0:42", CheckProgressText.Elapsed(TimeSpan.FromSeconds(42)));
        Assert.Equal("прошло 1:05", CheckProgressText.Elapsed(TimeSpan.FromSeconds(65)));
        Assert.Equal("прошло 1:02:03", CheckProgressText.Elapsed(new TimeSpan(1, 2, 3)));
    }

    [Fact]
    public void Run_reports_load_signatures_ledgers_and_the_report_in_order()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-progress");
        try
        {
            var letter = Path.Combine(dir.FullName, "письмо.pdf");
            var kit = Path.Combine(dir.FullName, "000-00-0000-А-НС1.pdf");
            File.WriteAllBytes(letter, []);
            File.WriteAllBytes(kit, []);

            var steps = new List<CheckProgress>();
            var report = new CheckingEngine().Run(new CheckInput
            {
                RdPaths = [letter, kit],
            }, new SyncProgress(steps));

            Assert.NotNull(report);
            var stages = steps.Select(step => step.Stage).Distinct().ToList();
            Assert.Equal(
                [CheckStages.LoadKits, CheckStages.Signatures, CheckStages.Ledgers, CheckStages.Report],
                stages);

            var loads = steps.Where(step => step.Stage == CheckStages.LoadKits).ToList();
            Assert.Equal("Загрузка комплектов: 1 из 2 — письмо.pdf", loads[0].Text);
            Assert.Equal("Загрузка комплектов: 2 из 2 — 000-00-0000-А-НС1.pdf", loads[1].Text);
            Assert.True(loads[0].Overall < 1);

            var signatures = steps.Where(step => step.Stage == CheckStages.Signatures).ToList();
            Assert.Equal("Проверка подписей: 1 из 1", Assert.Single(signatures).Text);
            Assert.Equal("000-00-0000-А-НС1.pdf", signatures[0].FileName);

            Assert.Contains(steps, step => step.Text == "Сверка с ВЭРД/ВЭСР");
            Assert.Equal("Формирование отчёта", steps[^1].Text);
            Assert.Equal(1, steps[^1].Overall, 3);
            Assert.All(steps.Zip(steps.Skip(1)), pair => Assert.True(pair.Second.Overall + 0.000001 >= pair.First.Overall));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Run_stops_when_cancellation_is_requested()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var steps = new List<CheckProgress>();
        Assert.Throws<OperationCanceledException>(() => new CheckingEngine().Run(
            new CheckInput(),
            new SyncProgress(steps),
            cancel.Token));
        Assert.Empty(steps);
    }

    private sealed class SyncProgress(List<CheckProgress> steps) : IProgress<CheckProgress>
    {
        public void Report(CheckProgress value) => steps.Add(value);
    }
}
