namespace ProverkaRD.Engine;

public static class FindingSources
{
    public static (string InAct, string InRd, string InVerd, string InVesr) Place(string check, string valueA, string valueB)
    {
        switch (check)
        {
            case "Стоимость без НДС":
            case "Стоимость с НДС":
            case "Сумма НДС":
            case "Цена за единицу":
            case "Единица измерения":
            case "Количество":
                return (valueA, "", "", valueB);
            case "Состав комплектов":
                return (valueA, "", valueB, "");
            case "Фамилия без подписи":
            case "Фамилии в комплекте":
            case "Электронная подпись":
            case "Подпись не относится к файлу":
            case "Срок сертификата":
            case "Время подписания":
            case "Подписант не найден в документе":
            case "Номер элемента на титуле РД":
            case "Посторонний файл":
                return ("", valueA.Length > 0 ? valueA : valueB, "", "");
            case "Арифметика акта":
            case "Итог таблицы стоимости":
            case "Код ДГП внутри акта":
            case "Номер элемента акта":
            case "Стоимость":
                return (Join("", valueA, "", valueB), "", "", "");
            case "Элемент в ВЭРД":
            case "Шифр в ВЭРД":
            case "Код ДГП ВЭРД в ВЭСР":
                return ("", "", valueA, valueB);
            case "Код ДГП в ВЭСР":
                return (valueA, "", "", valueB);
        }

        if (check.Contains("файла и титула", StringComparison.OrdinalIgnoreCase))
            return ("", Join("файл", valueA, "титул", valueB), "", "");

        var slots = new List<(int Index, string Slot)>();
        Add(slots, check, "акт", "act");
        AddRd(slots, check);
        Add(slots, check, "ВЭРД", "verd");
        Add(slots, check, "ВЭСР", "vesr");
        slots.Sort((x, y) => x.Index.CompareTo(y.Index));

        var distinct = new List<string>();
        foreach (var slot in slots.Select(s => s.Slot))
        {
            if (!distinct.Contains(slot))
                distinct.Add(slot);
        }

        string act = "", rd = "", verd = "", vesr = "";
        void Put(string slot, string value)
        {
            switch (slot)
            {
                case "act": act = value; break;
                case "rd": rd = value; break;
                case "verd": verd = value; break;
                case "vesr": vesr = value; break;
            }
        }

        if (distinct.Count >= 1)
            Put(distinct[0], valueA);
        if (distinct.Count >= 2)
            Put(distinct[1], valueB);
        else if (valueB.Length > 0 && distinct.Count == 1)
            Put(distinct[0], Join("", valueA, "", valueB));
        else if (distinct.Count == 0)
            act = Join("", valueA, "", valueB);

        return (act, rd, verd, vesr);
    }

    private static void Add(List<(int Index, string Slot)> slots, string check, string token, string slot)
    {
        var index = check.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
            slots.Add((index, slot));
    }

    private static void AddRd(List<(int Index, string Slot)> slots, string check)
    {
        for (var i = 0; i < check.Length - 1; i++)
        {
            var pair = check.Substring(i, 2);
            if (!pair.Equals("РД", StringComparison.OrdinalIgnoreCase))
                continue;
            if (i >= 2 && check.Substring(i - 2, 2).Equals("ВЭ", StringComparison.OrdinalIgnoreCase))
                continue;
            slots.Add((i, "rd"));
            return;
        }
    }

    private static string Join(string leftLabel, string left, string rightLabel, string right)
    {
        string Side(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "—")
                return "";
            return label.Length == 0 ? value : label + ": " + value;
        }

        return string.Join(" / ", new[] { Side(leftLabel, left), Side(rightLabel, right) }.Where(s => s.Length > 0));
    }
}
