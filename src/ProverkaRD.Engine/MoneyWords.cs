using System.Globalization;

namespace ProverkaRD.Engine;

/// <summary>
/// Сумма прописью в акте: «четыреста восемьдесят три тысячи семьдесят три» → 483073.
/// Падеж «тысяча / тысячи / тысяч» на число не влияет.
/// </summary>
public static class MoneyWords
{
    private static readonly Dictionary<string, long> Values = Build();

    public static bool TryParse(string? words, out long rubles)
    {
        rubles = 0;
        if (string.IsNullOrWhiteSpace(words))
            return false;

        var text = words.Replace('ё', 'е').Replace('Ё', 'Е').ToLower(CultureInfo.GetCultureInfo("ru-RU"));
        var parts = text.Split([' ', '\t', '\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        long total = 0;
        long group = 0;
        foreach (var part in parts)
        {
            if (!Values.TryGetValue(part, out var value))
                return false;
            if (value is 1_000 or 1_000_000 or 1_000_000_000)
            {
                if (group == 0)
                    group = 1;
                try
                {
                    total = checked(total + group * value);
                }
                catch (OverflowException)
                {
                    return false;
                }

                group = 0;
                continue;
            }

            try
            {
                group = checked(group + value);
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        try
        {
            rubles = checked(total + group);
        }
        catch (OverflowException)
        {
            return false;
        }

        return true;
    }

    private static Dictionary<string, long> Build()
    {
        var map = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["ноль"] = 0,
            ["один"] = 1,
            ["одна"] = 1,
            ["одно"] = 1,
            ["два"] = 2,
            ["две"] = 2,
            ["три"] = 3,
            ["четыре"] = 4,
            ["пять"] = 5,
            ["шесть"] = 6,
            ["семь"] = 7,
            ["восемь"] = 8,
            ["девять"] = 9,
            ["десять"] = 10,
            ["одиннадцать"] = 11,
            ["двенадцать"] = 12,
            ["тринадцать"] = 13,
            ["четырнадцать"] = 14,
            ["пятнадцать"] = 15,
            ["шестнадцать"] = 16,
            ["семнадцать"] = 17,
            ["восемнадцать"] = 18,
            ["девятнадцать"] = 19,
            ["двадцать"] = 20,
            ["тридцать"] = 30,
            ["сорок"] = 40,
            ["пятьдесят"] = 50,
            ["шестьдесят"] = 60,
            ["семьдесят"] = 70,
            ["восемьдесят"] = 80,
            ["девяносто"] = 90,
            ["сто"] = 100,
            ["двести"] = 200,
            ["триста"] = 300,
            ["четыреста"] = 400,
            ["пятьсот"] = 500,
            ["шестьсот"] = 600,
            ["семьсот"] = 700,
            ["восемьсот"] = 800,
            ["девятьсот"] = 900,
            ["тысяча"] = 1_000,
            ["тысячи"] = 1_000,
            ["тысяч"] = 1_000,
            ["миллион"] = 1_000_000,
            ["миллиона"] = 1_000_000,
            ["миллионов"] = 1_000_000,
            ["миллиард"] = 1_000_000_000,
            ["миллиарда"] = 1_000_000_000,
            ["миллиардов"] = 1_000_000_000,
        };
        return map;
    }
}
