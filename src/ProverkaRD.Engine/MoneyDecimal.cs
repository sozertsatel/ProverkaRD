using System.Globalization;
using ClosedXML.Excel;

namespace ProverkaRD.Engine;

/// <summary>
/// Суммы хранятся только как decimal. В xlsx число — это double;
/// в decimal оно переводится сразу, до округления до копейки.
/// </summary>
public static class MoneyDecimal
{
    public static decimal Kopecks(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Перевод числа Excel без хранения double дальше границы ячейки.
    /// G15 убирает двоичный шум (0.1+0.2), затем сумма округляется до копейки.
    /// </summary>
    public static decimal FromExcelDouble(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new FormatException("Число Excel не является суммой.");
        var text = value.ToString("G15", CultureInfo.InvariantCulture);
        var parsed = decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        return Kopecks(parsed);
    }

    public static string ExcelNumberText(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "";
        return value.ToString("G15", CultureInfo.InvariantCulture);
    }

    public static decimal? ReadMoney(IXLCell cell)
    {
        if (cell.IsEmpty() || cell.DataType == XLDataType.Error)
            return null;
        if (cell.DataType == XLDataType.Number)
            return FromExcelDouble(cell.GetDouble());
        return TextMatch.ParseMoney(cell.GetFormattedString());
    }

    /// <summary>Количество и прочие неденежные числа: decimal без округления до копейки.</summary>
    public static decimal? ReadQuantity(IXLCell cell)
    {
        if (cell.IsEmpty() || cell.DataType == XLDataType.Error)
            return null;
        if (cell.DataType == XLDataType.Number)
        {
            var text = ExcelNumberText(cell.GetDouble());
            return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        return TextMatch.ParseDecimal(cell.GetFormattedString());
    }
}
