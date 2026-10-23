namespace ProverkaRD.Engine.Tests;

public class InputOutlineTests
{
    [Fact]
    public void Tree_groups_pdfs_by_act_and_puts_the_rest_under_without_act()
    {
        var root = Path.Combine(Path.GetTempPath(), "proverkard-outline");
        var act = new ActDocument
        {
            FilePath = Path.Combine(root, "РД 7.29.docx"),
            ElementNoFromFileName = "7.29",
            Kits = [new ActKit { Cipher = "000-00-0000-А-НС1", ElementNo = "7.29" }],
        };
        var empty = new ActDocument
        {
            FilePath = Path.Combine(root, "РД 7.30.docx"),
            ElementNoFromFileName = "7.30",
        };
        var other = new ActDocument
        {
            FilePath = Path.Combine(root, "РД 8.01.docx"),
            ElementNoFromFileName = "8.01",
            Kits = [new ActKit { Cipher = "000-00-0000-А-НС2", ElementNo = "8.01" }],
        };

        var model = InputOutline.Build(
            [other, empty, act],
            [
                new InputOutline.FileEntry(Path.Combine(root, "7.29", "том.pdf"), true),
                new InputOutline.FileEntry(Path.Combine(root, "7.29", "ещё.pdf"), false),
                new InputOutline.FileEntry(Path.Combine(root, "7.29", "том.СВОР.pdf"), false),
                new InputOutline.FileEntry(Path.Combine(root, "7.29", "лист_ИЗМ.pdf"), true),
                new InputOutline.FileEntry(Path.Combine(root, "разное", "000-00-0000-А-НС1.pdf"), true),
                new InputOutline.FileEntry(Path.Combine(root, "8.01", "000-00-0000-А-НС1.pdf"), false),
                new InputOutline.FileEntry(Path.Combine(root, "7.40", "чужой.pdf"), false),
                new InputOutline.FileEntry(Path.Combine(root, "разное", "письмо.pdf"), false),
                new InputOutline.FileEntry(Path.Combine(root, "7.29", "пояснение.docx"), false),
            ]);

        Assert.Equal("3 акта, 1 без комплекта, 2 файла без акта", model.Summary);
        Assert.Equal(4, model.Nodes.Count);

        var first = model.Nodes[0];
        Assert.Equal("act", first.Kind);
        Assert.Equal("7.29 · РД 7.29 · 4 PDF", first.Title);
        Assert.False(first.Problem);
        Assert.False(first.Expand);
        Assert.Contains(first.Children, node => node.Title == "том.pdf · подпись рядом");
        Assert.Contains(first.Children, node => node.Title == "ещё.pdf · нет .sig");
        Assert.Contains(first.Children, node => node.Title == "том.СВОР.pdf · СВОР");
        Assert.Contains(first.Children, node => node.Title == "лист_ИЗМ.pdf · ИЗМ");
        Assert.Contains(first.Children, node => node.Title == "пояснение.docx · нет .sig");
        Assert.Contains(first.Children, node => node.Title == "000-00-0000-А-НС1.pdf · подпись рядом" && node.Path.Contains("разное"));
        Assert.Contains(first.Children, node => node.Path.Contains("8.01") && node.Title.Contains("нет .sig"));

        var missing = model.Nodes[1];
        Assert.Equal("7.30 · РД 7.30 · комплекты не добавлены", missing.Title);
        Assert.True(missing.Problem);
        Assert.True(missing.Expand);
        Assert.Empty(missing.Children);

        var second = model.Nodes[2];
        Assert.StartsWith("8.01 · ", second.Title);
        Assert.Contains("1 PDF", second.Title);
        Assert.False(second.Problem);

        var orphan = model.Nodes[3];
        Assert.Equal(InputOutline.OrphanTitle, orphan.Title);
        Assert.Equal("group", orphan.Kind);
        Assert.True(orphan.Problem);
        Assert.True(orphan.Expand);
        Assert.Equal(2, orphan.Children.Count);
        Assert.Contains(orphan.Children, node => node.Title.StartsWith("чужой.pdf", StringComparison.Ordinal));
        Assert.Contains(orphan.Children, node => node.Title.StartsWith("письмо.pdf", StringComparison.Ordinal));
        Assert.DoesNotContain(orphan.Children, node => node.Title.Contains("том.pdf", StringComparison.Ordinal) || node.Title.Contains("НС1", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(17, 0, 1, "17 актов, все с комплектами ✓, 1 файл без акта")]
    [InlineData(1, 0, 0, "1 акт, все с комплектами ✓")]
    [InlineData(2, 1, 2, "2 акта, 1 без комплекта, 2 файла без акта")]
    [InlineData(5, 5, 5, "5 актов, 5 без комплектов, 5 файлов без акта")]
    [InlineData(21, 0, 0, "21 акт, все с комплектами ✓")]
    [InlineData(22, 0, 11, "22 акта, все с комплектами ✓, 11 файлов без акта")]
    [InlineData(0, 0, 0, "Акты и комплекты не добавлены")]
    [InlineData(0, 0, 1, "Акты не добавлены, 1 файл без акта")]
    public void Summary_counts_acts_and_files_without_an_act(int acts, int withoutSets, int unmatched, string expected) =>
        Assert.Equal(expected, InputOutline.Summarize(acts, withoutSets, unmatched));
}
