using ClosedXML.Excel;
using ProverkaRD.Engine;

namespace ProverkaRD.Engine.Parsing;

/// <summary>Текст пропуска для ячейки суммы ВЭСР: значение и причина.</summary>
internal static class MoneyCellNote
{
    internal readonly record struct Unparsed(bool IsError, string Text);

    internal static string Failure(IXLCell cell)
    {
        var shown = Shown(cell);
        var reason = IsError(cell) ? "ошибка Excel" : "текст";
        return "Ячейка не разобрана как сумма: «" + shown + "», " + reason + ".";
    }

    internal static string Blank(IXLCell cell) =>
        "Пропущена пустая ячейка " + cell.Address + ".";

    internal static Unparsed? DescribeUnparsed(IXLCell cell)
    {
        if (cell.IsEmpty())
            return null;
        if (IsError(cell))
            return new Unparsed(true, "Ячейка " + cell.Address + ": «" + Shown(cell) + "», ошибка Excel, пропущена.");
        if (IsBlankValue(cell))
            return new Unparsed(false, "Ячейка " + cell.Address + " пустая, пропущена.");
        try
        {
            if (MoneyDecimal.ReadMoney(cell) is not null)
                return null;
        }
        catch (Exception ex)
        {
            var shown = Shown(cell);
            var body = shown.Length == 0 ? ex.Message : "«" + shown + "», " + ex.Message;
            return new Unparsed(IsError(cell), "Ячейка " + cell.Address + ": " + body + ", пропущена.");
        }

        if (IsError(cell))
            return new Unparsed(true, "Ячейка " + cell.Address + ": «" + Shown(cell) + "», ошибка Excel, пропущена.");
        return new Unparsed(false, "Ячейка " + cell.Address + ": «" + Shown(cell) + "», текст, пропущена.");
    }

    internal static bool IsBlankValue(IXLCell cell)
    {
        if (cell.DataType == XLDataType.Error)
            return false;
        var shown = Raw(cell);
        return shown.Length == 0 || shown is "-" or "—" or "–" or ".";
    }

    internal static bool IsError(IXLCell cell) =>
        cell.DataType == XLDataType.Error || LooksLikeError(Raw(cell));

    internal static string Shown(IXLCell cell)
    {
        if (cell.DataType == XLDataType.Error)
            return Russian(cell.GetError());
        var raw = Raw(cell);
        if (LooksLikeError(raw))
            return RussianText(raw);
        return raw.Length <= 300 ? raw : raw[..300] + "…";
    }

    private static string Raw(IXLCell cell)
    {
        try
        {
            return TextMatch.StripInvisible(cell.GetFormattedString()).Replace('\u00A0', ' ').Trim();
        }
        catch
        {
            return "";
        }
    }

    private static bool LooksLikeError(string text)
    {
        if (text.Length == 0 || text[0] != '#')
            return false;
        return text.Contains('!') || text.Contains('?') || text.Contains('/');
    }

    private static string Russian(XLError error) => error switch
    {
        XLError.IncompatibleValue => "#ЗНАЧ!",
        XLError.DivisionByZero => "#ДЕЛ/0!",
        XLError.CellReference => "#ССЫЛКА!",
        XLError.NameNotRecognized => "#ИМЯ?",
        XLError.NullValue => "#ПУСТО!",
        XLError.NumberInvalid => "#ЧИСЛО!",
        XLError.NoValueAvailable => "#Н/Д",
        _ => "#ЗНАЧ!",
    };

    private static string RussianText(string text) => text.Trim().ToUpperInvariant() switch
    {
        "#VALUE!" or "#ЗНАЧ!" => "#ЗНАЧ!",
        "#DIV/0!" or "#ДЕЛ/0!" => "#ДЕЛ/0!",
        "#REF!" or "#ССЫЛКА!" => "#ССЫЛКА!",
        "#NAME?" or "#ИМЯ?" => "#ИМЯ?",
        "#NULL!" or "#ПУСТО!" => "#ПУСТО!",
        "#NUM!" or "#ЧИСЛО!" => "#ЧИСЛО!",
        "#N/A" or "#Н/Д" => "#Н/Д",
        _ => text.Trim(),
    };
}
