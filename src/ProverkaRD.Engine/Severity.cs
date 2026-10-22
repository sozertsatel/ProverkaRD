namespace ProverkaRD.Engine;

public enum Severity
{
    Ok = 0,
    Formatting = 1,
    Warning = 2,
    Critical = 3,
}

public static class SeverityText
{
    public static string ToRu(Severity severity) => severity switch
    {
        Severity.Ok => "Совпадает",
        Severity.Formatting => "Ошибка оформления",
        Severity.Warning => "Предупреждение",
        Severity.Critical => "Критично",
        _ => "",
    };

    public static Severity Worst(IEnumerable<Severity> items)
    {
        var worst = Severity.Ok;
        foreach (var item in items)
        {
            if (item > worst)
                worst = item;
        }
        return worst;
    }

    public static Severity Worst(params Severity[] items) => Worst((IEnumerable<Severity>)items);
}
