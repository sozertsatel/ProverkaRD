namespace ProverkaRD.Engine.Parsing;

/// <summary>
/// Основная надпись ГОСТ Р 21.101, форма 3, в правом нижнем углу листа.
/// Размеры граф — как в typst-gost/stamp-eskd-spds, src/tables-spds.typ, form3:
/// столбцы 10+10+10+10+15+10+70+15+15+20 мм (185 мм), 11 строк по 5 мм (55 мм).
/// Роль (Разраб., Пров., ГИП, Н.контр., Утв.) — столбцы 0–1, 0–20 мм.
/// Фамилия — столбцы 2–3, 20–40 мм. Рамка листа отступает от края на 5 мм.
/// </summary>
internal static class StampForm3
{
    internal readonly record struct StampWord(string Text, double Left, double Bottom, double Right, double Top);

    private const double Mm = 72.0 / 25.4;
    private const double WidthMm = 185;
    private const double HeightMm = 55;
    private const double RowMm = 5;
    private const int Rows = 11;
    private const double RoleRightMm = 20;
    private const double SurnameLeftMm = 20;
    private const double SurnameRightMm = 40;
    private const double SlackMm = 1.2;
    private static readonly double[] RightInsetsMm = [5, 0, 10, 20];
    private static readonly double[] BottomInsetsMm = [5, 0, 10, 20];

    internal static List<string> ReadCells(IReadOnlyList<StampWord> words, double pageWidth, double pageHeight)
    {
        if (words.Count == 0 || pageWidth < 40 || pageHeight < 40)
            return [];

        var cells = new List<string>();
        foreach (var cell in BestGrid(words, pageWidth))
            Add(cells, cell);
        foreach (var cell in ByRoleOffset(words))
            Add(cells, cell);
        return cells;
    }

    private static List<string> BestGrid(IReadOnlyList<StampWord> words, double pageWidth)
    {
        List<string> best = [];
        var score = 0;
        foreach (var right in RightInsetsMm)
        {
            foreach (var bottom in BottomInsetsMm)
            {
                var cells = GridAt(words, pageWidth - right * Mm, bottom * Mm);
                if (cells.Count > score)
                {
                    score = cells.Count;
                    best = cells;
                }
            }
        }

        return best;
    }

    private static List<string> GridAt(IReadOnlyList<StampWord> words, double stampRight, double stampBottom)
    {
        var stampLeft = stampRight - WidthMm * Mm;
        var stampTop = stampBottom + HeightMm * Mm;
        var slack = SlackMm * Mm;
        var cells = new List<string>();
        for (var row = 0; row < Rows; row++)
        {
            var top = stampTop - row * RowMm * Mm;
            var bottom = top - RowMm * Mm;
            var role = Join(words.Where(word => CenterIn(word, stampLeft, stampLeft + RoleRightMm * Mm, bottom, top, slack)));
            if (!RdParser.IsRoleLine(role))
                continue;
            var surnameLeft = stampLeft + SurnameLeftMm * Mm;
            var surnameRight = stampLeft + (SurnameRightMm + 3) * Mm;
            var surname = Join(words.Where(word => CenterIn(word, surnameLeft, surnameRight, bottom, top, slack)));
            if (surname.Length > 0)
                cells.Add(surname);
        }

        return cells;
    }

    /// <summary>
    /// Если штамп сдвинут относительно поля 5 мм, строка роли сама задаёт левый край графы.
    /// Фамилия ищется в тех же 20–40 мм от этого края, в нижней полосе листа.
    /// </summary>
    private static List<string> ByRoleOffset(IReadOnlyList<StampWord> words)
    {
        var band = words.Where(word => word.Bottom <= (HeightMm + 40) * Mm).ToList();
        var cells = new List<string>();
        foreach (var line in Lines(band))
        {
            var text = Join(line);
            if (!RdParser.IsRoleLine(text))
                continue;
            var origin = line.Min(word => word.Left);
            var from = origin + 12 * Mm;
            var to = origin + 48 * Mm;
            var surname = Join(line.Where(word =>
            {
                var center = (word.Left + word.Right) / 2;
                return center >= from && center <= to;
            }));
            if (surname.Length > 0)
                cells.Add(surname);
        }

        return cells;
    }

    private static List<List<StampWord>> Lines(List<StampWord> words)
    {
        var ordered = words.OrderByDescending(word => word.Bottom).ToList();
        var lines = new List<List<StampWord>>();
        foreach (var word in ordered)
        {
            var line = lines.FirstOrDefault(existing => Math.Abs(existing[0].Bottom - word.Bottom) <= 3.5);
            if (line is null)
                lines.Add([word]);
            else
                line.Add(word);
        }

        return lines;
    }

    private static bool CenterIn(StampWord word, double left, double right, double bottom, double top, double slack)
    {
        var x = (word.Left + word.Right) / 2;
        var y = (word.Bottom + word.Top) / 2;
        return x >= left - slack && x <= right + slack && y >= bottom - slack && y <= top + slack;
    }

    private static string Join(IEnumerable<StampWord> words) =>
        string.Join(" ", words.OrderBy(word => word.Left).Select(word => word.Text.Trim()).Where(text => text.Length > 0));

    private static void Add(List<string> cells, string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return;
        if (cells.Any(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
            return;
        cells.Add(trimmed);
    }
}
