using System.Globalization;
using System.Text.RegularExpressions;
using ProverkaRD.Engine.Parsing;

namespace ProverkaRD.Engine;

public sealed partial class CheckingEngine
{
    private const decimal VatRate = 0.22m;
    private readonly List<Finding> _findings = [];
    private bool _rdFilesLoaded;
    private string _currentElement = "";
    private CheckOptions _options = new();

    public CheckReport Run(CheckInput input)
    {
        _findings.Clear();
        _currentElement = "";
        _options = input.Options ?? new CheckOptions();
        var report = new CheckReport
        {
            VerdFileName = NameOf(input.VerdPath),
            VesrFileName = NameOf(input.VesrPath),
        };

        VerdBook? verd = null;
        if (string.IsNullOrWhiteSpace(input.VerdPath))
            report.Problems.Add("ВЭРД не загружена.");
        else if (!File.Exists(input.VerdPath))
            report.Problems.Add("Файл ВЭРД не найден: " + input.VerdPath);
        else
        {
            verd = VerdParser.Parse(input.VerdPath);
            report.Problems.AddRange(verd.Errors);
        }

        VesrBook? vesr = null;
        if (string.IsNullOrWhiteSpace(input.VesrPath))
            report.Problems.Add("ВЭСР не загружена.");
        else if (!File.Exists(input.VesrPath))
            report.Problems.Add("Файл ВЭСР не найден: " + input.VesrPath);
        else
        {
            vesr = VesrParser.Parse(input.VesrPath);
            report.Problems.AddRange(vesr.Errors);
        }

        foreach (var problem in report.Problems)
            Add("—", "—", "Исходные данные", problem, "", Severity.Critical, problem);

        var verdByNumber = new Dictionary<string, List<VerdElement>>(StringComparer.Ordinal);
        var verdByCipher = new Dictionary<string, List<VerdHit>>(StringComparer.Ordinal);
        if (verd is not null)
        {
            foreach (var element in verd.Elements)
            {
                var key = TextMatch.CodeKey(element.Number);
                if (!verdByNumber.TryGetValue(key, out var list))
                    verdByNumber[key] = list = [];
                list.Add(element);
                foreach (var kit in element.Kits)
                {
                    var cipherKey = TextMatch.CodeKey(kit.Cipher);
                    if (cipherKey.Length == 0)
                        continue;
                    if (!verdByCipher.TryGetValue(cipherKey, out var hits))
                        verdByCipher[cipherKey] = hits = [];
                    hits.Add(new VerdHit(element, kit));
                }
            }
        }

        var acts = new List<ActDocument>();
        foreach (var path in DistinctFiles(input.ActPaths))
        {
            var act = ActParser.Parse(path);
            acts.Add(act);
            foreach (var error in act.Errors)
                Add(act.DisplayName, "—", "Чтение акта", error, "", Severity.Critical, error);
        }

        var rdFiles = new List<RdDocument>();
        foreach (var path in DistinctFiles(input.RdPaths))
        {
            if (path.EndsWith(".sig", StringComparison.OrdinalIgnoreCase))
                continue;
            if (RdFileRules.IsAuxiliary(path))
                continue;
            var rd = RdParser.Parse(path);
            rdFiles.Add(rd);
            foreach (var error in rd.Errors)
                Add(rd.FileName, rd.Cipher, "Чтение комплекта", error, "", Severity.Critical, error);
        }
        _rdFilesLoaded = rdFiles.Count > 0;

        var rdByCipher = new Dictionary<string, List<RdDocument>>(StringComparer.Ordinal);
        foreach (var rd in rdFiles)
        {
            foreach (var cipher in new[] { rd.Cipher, rd.CipherFromFileName, rd.CipherFromText })
            {
                var key = TextMatch.CodeKey(cipher);
                if (key.Length == 0)
                    continue;
                if (!rdByCipher.TryGetValue(key, out var list))
                    rdByCipher[key] = list = [];
                if (!list.Contains(rd))
                    list.Add(rd);
            }
        }

        var usedRd = new HashSet<RdDocument>();
        foreach (var act in acts.OrderBy(a => ElementSortKey(ResolvedPreview(a)), IntArrayComparer.Instance))
            CheckAct(act, verdByNumber, vesr, rdByCipher, usedRd, report);

        foreach (var rd in rdFiles.Where(r => !usedRd.Contains(r)))
            CheckLooseRd(rd, verdByCipher, acts.Count > 0, report);

        var grouped = FindingGrouper.Apply(_findings, _options);
        foreach (var act in report.Acts)
        {
            var score = ActScore.Score(act, grouped, report.Kits);
            act.Overall = score.Overall;
            act.CriticalCount = score.Critical;
            act.FormattingCount = score.Formatting;
            act.WarningCount = score.Warnings;
        }

        report.Findings.AddRange(grouped
            .OrderBy(f => ElementOrder.Key(ElementOrder.First(f.ElementNo)), IntArrayComparer.Instance)
            .ThenByDescending(f => f.Severity)
            .ThenBy(f => f.CheckName, StringComparer.CurrentCultureIgnoreCase));
        return report;
    }

    private void CheckAct(
        ActDocument act,
        Dictionary<string, List<VerdElement>> verdByNumber,
        VesrBook? vesr,
        Dictionary<string, List<RdDocument>> rdByCipher,
        HashSet<RdDocument> usedRd,
        CheckReport report)
    {
        var label = act.DisplayName;
        _currentElement = act.ElementNoFromFileName ?? act.ElementNoFromHeading ?? "";
        var sources = new List<(string Label, string Value)>();
        if (!string.IsNullOrWhiteSpace(act.ElementNoFromFileName))
            sources.Add(("имя файла", act.ElementNoFromFileName));
        if (!string.IsNullOrWhiteSpace(act.ElementNoFromHeading))
            sources.Add(("строка «АКТ №»", act.ElementNoFromHeading));
        foreach (var bracket in act.Kits.Select(k => k.ElementNo).Where(s => s.Length > 0).Distinct())
            sources.Add(("№ тома", bracket));

        var distinctNumbers = sources.Select(s => TextMatch.CodeKey(s.Value)).Distinct().ToList();
        if (distinctNumbers.Count > 1)
        {
            Add(label, "—", "Номер элемента акта",
                string.Join("; ", sources.Select(s => s.Label + ": " + s.Value)),
                "",
                Severity.Critical,
                "Номер элемента в имени файла, в строке «АКТ №» и в скобках столбца «№ тома» должен быть одним и тем же.");
        }
        else if (sources.Count == 0)
        {
            Add(label, "—", "Номер элемента акта", "", "", Severity.Critical,
                "Не удалось определить номер элемента: нет «РД 7.xx» в имени файла, нет строки «АКТ №» и нет номера в скобках «№ тома».");
        }

        var resolved = sources.Select(s => s.Value).FirstOrDefault(v => verdByNumber.ContainsKey(TextMatch.CodeKey(v)))
                       ?? sources.FirstOrDefault().Value
                       ?? "";
        if (resolved.Length > 0)
            _currentElement = resolved;

        VerdElement? element = null;
        if (resolved.Length > 0)
        {
            if (!verdByNumber.TryGetValue(TextMatch.CodeKey(resolved), out var elements) || elements.Count == 0)
            {
                Add(label, "—", "Элемент в ВЭРД", resolved, "", Severity.Critical,
                    "Элемент " + resolved + " в ведомости ВЭРД не найден. Сверка с ВЭРД по этому акту невозможна.");
            }
            else
            {
                element = elements[0];
                if (elements.Count > 1)
                {
                    Add(label, "—", "Элемент в ВЭРД", resolved, elements.Count.ToString(CultureInfo.InvariantCulture),
                        Severity.Critical,
                        "В ВЭРД несколько элементов с номером " + resolved + ". В сверке взята первая строка.");
                }
            }
        }

        var actCodes = act.Kits.Select(k => k.DgpCode)
            .Concat(act.CostLines.Select(l => l.DgpCode))
            .Where(c => c.Length > 0)
            .Distinct()
            .ToList();
        var codeKeys = actCodes.Select(TextMatch.CodeKey).Distinct().ToList();
        if (codeKeys.Count > 1)
        {
            Add(label, "—", "Код ДГП внутри акта", string.Join("; ", actCodes), "", Severity.Critical,
                "В таблице комплектов и в таблице стоимости указаны разные коды ДГП.");
        }

        var actCode = act.Kits.Select(k => k.DgpCode).FirstOrDefault(c => c.Length > 0)
                      ?? act.CostLines.Select(l => l.DgpCode).FirstOrDefault(c => c.Length > 0)
                      ?? "";
        var verdCode = element?.DgpCode ?? "";
        var codeStatus = Severity.Ok;
        if (element is not null)
            codeStatus = TextCheck(label, "—", "Код ДГП: акт и ВЭРД", actCode, verdCode,
                "Сравнивается код из столбца «№ тома» акта и столбец G «№ Элемента по ДГП» ВЭРД для элемента " + element.Number + ".");
        else if (actCode.Length == 0)
            codeStatus = Severity.Critical;

        VesrRow? vesrByAct = null;
        if (vesr is not null && actCode.Length > 0)
        {
            if (!vesr.ByCode.TryGetValue(TextMatch.CodeKey(actCode), out vesrByAct))
            {
                codeStatus = SeverityText.Worst(codeStatus, Severity.Critical);
                Add(label, "—", "Код ДГП в ВЭСР", actCode, "", Severity.Critical,
                    "Код ДГП из акта не найден в ВЭСР. Поиск выполняется только по полному коду, без обрезки до родителя. Точка в конце кода не учитывается.");
            }
        }
        else if (vesr is null)
        {
            codeStatus = SeverityText.Worst(codeStatus, Severity.Critical);
        }

        if (element is not null && verdCode.Length > 0
            && TextMatch.CodeKey(verdCode) != TextMatch.CodeKey(actCode)
            && vesr is not null
            && !vesr.ByCode.ContainsKey(TextMatch.CodeKey(verdCode)))
        {
            Add(label, "—", "Код ДГП ВЭРД в ВЭСР", verdCode, "", Severity.Critical,
                "Код из ВЭРД тоже отсутствует в ВЭСР.");
        }

        var detailLines = act.CostLines.Where(l => !l.IsTotal).ToList();
        var totalLine = act.CostLines.FirstOrDefault(l => l.IsTotal);
        var costLine = detailLines.FirstOrDefault();
        var actCostName = costLine?.Name ?? "";
        var actElementName = ElementTitle(actCostName);

        var nameStatus = element is null
            ? Severity.Critical
            : TextCheck(label, "—", "Наименование элемента: акт и ВЭРД", actElementName, element.Name,
                "Наименование элемента берётся из таблицы стоимости акта (после «Рабочая документация.») и сравнивается со столбцом B ВЭРД.");

        var costNameStatus = Severity.Ok;
        if (vesrByAct is null)
            costNameStatus = actCostName.Length == 0 ? Severity.Critical : Severity.Critical;
        else
            costNameStatus = TextCheck(label, "—", "Наименование стоимости: акт и ВЭСР", actCostName, vesrByAct.Name,
                "Полное наименование из таблицы стоимости акта сравнивается со столбцом B ВЭСР по коду ДГП акта.");

        if (costLine is null && totalLine is null)
        {
            Add(label, "—", "Стоимость", "", "", Severity.Critical,
                "В акте не найдена таблица стоимости (строка с суммой без НДС, НДС и суммой с НДС).");
        }

        if (detailLines.Count > 1 && totalLine?.Net is not null)
        {
            var sum = detailLines.Sum(l => l.Net ?? 0m);
            if (!TextMatch.MoneyEquals(sum, totalLine.Net.Value))
            {
                Add(label, "—", "Итог таблицы стоимости", TextMatch.FormatMoney(sum), TextMatch.FormatMoney(totalLine.Net.Value),
                    Severity.Critical, "Сумма строк таблицы стоимости не равна строке «Итого».");
            }
        }

        decimal? actNet = totalLine?.Net ?? Sum(detailLines.Select(l => l.Net));
        decimal? actVat = totalLine?.Vat ?? Sum(detailLines.Select(l => l.Vat));
        decimal? actGross = totalLine?.Gross ?? Sum(detailLines.Select(l => l.Gross));
        if (actNet is null && detailLines.Count > 0)
            actNet = detailLines.Select(l => l.Net).FirstOrDefault(v => v is not null);
        if (actVat is null)
            actVat = detailLines.Select(l => l.Vat).FirstOrDefault(v => v is not null);
        if (actGross is null)
            actGross = detailLines.Select(l => l.Gross).FirstOrDefault(v => v is not null);

        var netStatus = Severity.Ok;
        decimal? vesrNet = vesrByAct?.TotalNet;
        if (actNet is null)
        {
            netStatus = Severity.Critical;
            Add(label, "—", "Стоимость без НДС", "", vesrNet is null ? "" : TextMatch.FormatMoney(vesrNet.Value),
                Severity.Critical, "В акте нет суммы без НДС.");
        }
        else if (vesrNet is null)
        {
            netStatus = Severity.Critical;
            Add(label, "—", "Стоимость без НДС", TextMatch.FormatMoney(actNet.Value), "",
                Severity.Critical, "Не с чем сравнить сумму без НДС: строка ВЭСР по коду акта не найдена.");
        }
        else if (!TextMatch.MoneyEquals(actNet.Value, vesrNet.Value))
        {
            netStatus = Severity.Critical;
            var delta = actNet.Value - vesrNet.Value;
            Add(label, "—", "Стоимость без НДС", TextMatch.FormatMoney(actNet.Value), TextMatch.FormatMoney(vesrNet.Value),
                Severity.Critical,
                "Сумма без НДС в акте не равна столбцу F ВЭСР по коду " + actCode + ". Разница " + TextMatch.FormatMoney(delta) + " руб. Допуск сверки 0,01 руб.");
        }

        decimal? recalc = vesrNet is null ? null : decimal.Round(vesrNet.Value * (1m + VatRate), 2, MidpointRounding.AwayFromZero);
        var grossStatus = Severity.Ok;
        if (actGross is null)
        {
            grossStatus = Severity.Critical;
            Add(label, "—", "Стоимость с НДС", "", recalc is null ? "" : TextMatch.FormatMoney(recalc.Value),
                Severity.Critical, "В акте нет суммы с НДС.");
        }
        else if (recalc is null)
        {
            grossStatus = Severity.Critical;
        }
        else if (!TextMatch.MoneyEquals(actGross.Value, recalc.Value))
        {
            grossStatus = Severity.Critical;
            Add(label, "—", "Стоимость с НДС", TextMatch.FormatMoney(actGross.Value), TextMatch.FormatMoney(recalc.Value),
                Severity.Critical,
                "Сумма с НДС в акте не равна пересчёту ВЭСР × 1,22 (НДС 22%). Допуск 0,01 руб.");
        }

        NoteInternalArithmetic(label, actNet, actVat, actGross);

        if (costLine is not null && vesrByAct is not null)
        {
            if (costLine.Unit.Length > 0 || vesrByAct.Unit.Length > 0)
                TextCheck(label, "—", "Единица измерения", costLine.Unit, vesrByAct.Unit,
                    "Единица измерения таблицы стоимости акта и столбец C ВЭСР.");
            if (costLine.Quantity is decimal qty && vesrByAct.Quantity is decimal vesrQty
                && !TextMatch.MoneyEquals(qty, vesrQty))
            {
                Add(label, "—", "Количество", qty.ToString(CultureInfo.InvariantCulture), vesrQty.ToString(CultureInfo.InvariantCulture),
                    Severity.Critical, "Количество в акте не равно количеству в ВЭСР.");
            }
        }

        var completeness = CheckCompleteness(label, act, element, actElementName, rdByCipher, usedRd, report, out var completenessText);

        var overall = SeverityText.Worst(nameStatus, costNameStatus, codeStatus, netStatus, grossStatus, completeness);
        // Внутренняя арифметика уже попала в замечания и должна красить итог.
        if (_findings.Any(f => f.Act == label && f.CheckName == "Арифметика акта" && f.Severity == Severity.Critical))
            overall = Severity.Critical;

        report.Acts.Add(new ActResult
        {
            FileName = label,
            ElementNo = resolved,
            ActElementName = OneLine(actElementName),
            VerdElementName = OneLine(element?.Name),
            NameStatus = nameStatus,
            ActCostName = OneLine(actCostName),
            VesrCostName = OneLine(vesrByAct?.Name),
            CostNameStatus = vesrByAct is null && actCostName.Length > 0 ? Severity.Critical : costNameStatus,
            ActCode = actCode,
            VerdCode = verdCode,
            CodeFoundInVesr = vesrByAct is not null,
            CodeStatus = codeStatus,
            ActNet = actNet,
            VesrNet = vesrNet,
            NetStatus = netStatus,
            ActVat = actVat,
            ActGross = actGross,
            GrossRecalculated = recalc,
            GrossStatus = grossStatus,
            ActKitCount = act.Kits.Count,
            VerdKitCount = element?.Kits.Count ?? 0,
            CompletenessText = completenessText,
            CompletenessStatus = completeness,
            Overall = overall,
        });
    }

    private Severity CheckCompleteness(
        string label,
        ActDocument act,
        VerdElement? element,
        string actElementName,
        Dictionary<string, List<RdDocument>> rdByCipher,
        HashSet<RdDocument> usedRd,
        CheckReport report,
        out string text)
    {
        var statuses = new List<Severity>();
        var actKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kit in act.Kits)
        {
            var key = TextMatch.CodeKey(kit.Cipher);
            if (!actKeys.Add(key))
            {
                statuses.Add(Severity.Critical);
                Add(label, kit.Cipher, "Состав комплектов", kit.Cipher, "", Severity.Critical,
                    "Шифр повторяется в таблице комплектов акта.");
            }
        }

        var verdKits = element?.Kits ?? [];
        var verdKeys = verdKits.Select(k => TextMatch.CodeKey(k.Cipher)).Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);
        var missing = verdKits.Where(k => !actKeys.Contains(TextMatch.CodeKey(k.Cipher))).Select(k => k.Cipher).ToList();
        var extra = act.Kits.Where(k => !verdKeys.Contains(TextMatch.CodeKey(k.Cipher))).Select(k => k.Cipher).ToList();
        if (element is null)
            statuses.Add(Severity.Critical);
        if (missing.Count > 0 || extra.Count > 0)
        {
            statuses.Add(Severity.Critical);
            Add(label, "—", "Состав комплектов",
                extra.Count == 0 ? "—" : string.Join(", ", extra),
                missing.Count == 0 ? "—" : string.Join(", ", missing),
                Severity.Critical,
                "Шифры комплектов акта и ВЭРД должны совпадать один к одному. "
                + (extra.Count > 0 ? "Есть в акте, нет в ВЭРД: " + string.Join(", ", extra) + ". " : "")
                + (missing.Count > 0 ? "Есть в ВЭРД, нет в акте: " + string.Join(", ", missing) + "." : ""));
        }

        var verdByKey = verdKits.GroupBy(k => TextMatch.CodeKey(k.Cipher)).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var nameProblems = 0;
        var nameFormatting = 0;
        foreach (var kit in act.Kits)
        {
            verdByKey.TryGetValue(TextMatch.CodeKey(kit.Cipher), out var verdKit);
            var nameStatus = verdKit is null
                ? Severity.Critical
                : TextCheck(label, kit.Cipher, "Наименование комплекта: акт и ВЭРД", kit.Name, verdKit.Name,
                    "Столбец «Наименование документа» акта и столбец E «Наименование документации» ВЭРД.");
            if (nameStatus == Severity.Critical)
                nameProblems++;
            else if (nameStatus == Severity.Formatting)
                nameFormatting++;
            statuses.Add(nameStatus == Severity.Critical && verdKit is null && extra.Contains(kit.Cipher)
                ? Severity.Ok
                : nameStatus);

            RdDocument? rd = null;
            if (rdByCipher.TryGetValue(TextMatch.CodeKey(kit.Cipher), out var files))
            {
                rd = files[0];
                usedRd.Add(rd);
            }
            else if (_rdFilesLoaded)
            {
                Add(label, kit.Cipher, "Файл комплекта РД", kit.Cipher, "", Severity.Warning,
                    "Комплект указан в акте, но файл РД не загружен. Замечание появляется только если хотя бы один комплект уже приложен.");
            }

            var row = BuildKitRow(label, element, kit.DgpCode.Length > 0 ? kit.DgpCode : element?.DgpCode ?? "", kit, verdKit, rd, actElementName);
            report.Kits.Add(row);
        }

        foreach (var verdKit in verdKits.Where(k => !actKeys.Contains(TextMatch.CodeKey(k.Cipher))))
        {
            report.Kits.Add(BuildKitRow(label, element, element?.DgpCode ?? "", null, verdKit, null, null));
        }

        var parts = new List<string>
        {
            "в акте " + act.Kits.Count + ", в ВЭРД " + verdKits.Count,
        };
        if (missing.Count > 0)
            parts.Add("нет в акте: " + string.Join(", ", missing));
        if (extra.Count > 0)
            parts.Add("лишние в акте: " + string.Join(", ", extra));
        if (nameProblems > 0)
            parts.Add("наименования различаются: " + nameProblems);
        else if (nameFormatting > 0)
            parts.Add("оформление наименований: " + nameFormatting);
        else if (missing.Count == 0 && extra.Count == 0 && element is not null)
            parts.Add("шифры и наименования совпадают");
        text = string.Join("; ", parts);
        return SeverityText.Worst(statuses);
    }

    private void CheckLooseRd(RdDocument rd, Dictionary<string, List<VerdHit>> verdByCipher, bool actsLoaded, CheckReport report)
    {
        VerdHit? hit = null;
        foreach (var cipher in new[] { rd.Cipher, rd.CipherFromText, rd.CipherFromFileName })
        {
            if (verdByCipher.TryGetValue(TextMatch.CodeKey(cipher), out var hits) && hits.Count > 0)
            {
                hit = hits[0];
                break;
            }
        }

        _currentElement = hit?.Element.Number ?? rd.ElementNo ?? "";
        if (actsLoaded)
        {
            Add("—", rd.Cipher.Length > 0 ? rd.Cipher : rd.FileName, "Комплект вне актов", rd.FileName, "", Severity.Warning,
                "Файл РД загружен, но его шифр не встречается ни в одном акте.");
        }

        var element = hit?.Element;
        var kit = hit?.Kit;
        if (hit is null && rd.Cipher.Length > 0)
        {
            Add("—", rd.Cipher, "Шифр в ВЭРД", rd.Cipher, "", Severity.Critical,
                "Шифр загруженного комплекта не найден в ВЭРД.");
        }

        report.Kits.Add(BuildKitRow("—", element, element?.DgpCode ?? "", null, kit, rd, null));
    }

    private KitResult BuildKitRow(
        string actLabel,
        VerdElement? element,
        string dgp,
        ActKit? actKit,
        VerdKit? verdKit,
        RdDocument? rd,
        string? actElementName)
    {
        var cipher = actKit?.Cipher ?? verdKit?.Cipher ?? rd?.Cipher ?? "";
        var nameStatus = Severity.Ok;
        if (actKit is not null && verdKit is not null)
            nameStatus = TextMatch.Compare(actKit.Name, verdKit.Name);
        else if (actKit is not null && verdKit is null)
            nameStatus = Severity.Critical;
        else if (actKit is null && verdKit is not null)
            nameStatus = Severity.Critical;

        var rdNameStatus = Severity.Ok;
        var rdElementStatus = Severity.Ok;
        var surnameStatus = Severity.Ok;
        var presence = rd is null
            ? (_rdFilesLoaded ? "файл не загружен" : "файл не прикладывался")
            : actKit is null ? "нет в актах" : "загружен";

        if (rd is not null)
        {
            if (!string.IsNullOrWhiteSpace(rd.CipherFromText) && !string.IsNullOrWhiteSpace(rd.CipherFromFileName))
            {
                var cipherStatus = TextCheck(actLabel, cipher, "Шифр файла и титула", rd.CipherFromFileName, rd.CipherFromText,
                    "Шифр в имени файла и шифр, прочитанный с титульного листа.");
                rdNameStatus = SeverityText.Worst(rdNameStatus, cipherStatus);
            }

            if (verdKit is not null)
                rdNameStatus = SeverityText.Worst(rdNameStatus,
                    TextCheck(actLabel, cipher, "Наименование комплекта: РД и ВЭРД", rd.KitName, verdKit.Name,
                        "Наименование на титуле (строки после «РАБОЧАЯ ДОКУМЕНТАЦИЯ» до шифра) и столбец E ВЭРД."));
            if (actKit is not null)
                rdNameStatus = SeverityText.Worst(rdNameStatus,
                    TextCheck(actLabel, cipher, "Наименование комплекта: РД и акт", rd.KitName, actKit.Name,
                        "Наименование на титуле комплекта и наименование в таблице комплектов акта."));

            if (element is not null)
            {
                if (rd.ElementNo.Length == 0)
                {
                    rdElementStatus = Severity.Critical;
                    Add(actLabel, cipher, "Номер элемента на титуле РД", "", element.Number, Severity.Critical,
                        "На титуле не найдена строка «Этап 7.xx …». Ожидался элемент " + element.Number + ".");
                }
                else
                {
                    rdElementStatus = TextCheck(actLabel, cipher, "Номер элемента: РД и ВЭРД", rd.ElementNo, element.Number,
                        "Номер этапа с титульного листа и номер элемента ВЭРД, к которому относится шифр.");
                }

                rdElementStatus = SeverityText.Worst(rdElementStatus,
                    TextCheck(actLabel, cipher, "Наименование элемента: РД и ВЭРД", rd.ElementName, element.Name,
                        "Наименование после «Этап 7.xx» на титуле и столбец B ВЭРД. Опечатка вроде «соружения» / «сооружения» — критичное расхождение."));
            }

            if (!string.IsNullOrWhiteSpace(actElementName))
                rdElementStatus = SeverityText.Worst(rdElementStatus,
                    TextCheck(actLabel, cipher, "Наименование элемента: РД и акт", rd.ElementName, actElementName,
                        "Наименование элемента на титуле РД и наименование элемента в акте."));

            surnameStatus = CheckSurnames(actLabel, cipher, rd);
        }

        var overall = SeverityText.Worst(nameStatus, rdNameStatus, rdElementStatus, surnameStatus);
        return new KitResult
        {
            ElementNo = element?.Number ?? rd?.ElementNo ?? "",
            ActName = actLabel,
            Cipher = cipher,
            DgpCode = dgp,
            ActKitName = OneLine(actKit?.Name),
            VerdKitName = OneLine(verdKit?.Name),
            NameStatus = actKit is null && verdKit is null ? Severity.Ok : nameStatus,
            PaperCopies = actKit?.PaperCopies ?? "",
            ElectronicCopies = actKit?.ElectronicCopies ?? "",
            RdFileName = rd?.FileName ?? "",
            RdCipher = rd?.CipherFromText ?? "",
            RdKitName = OneLine(rd?.KitName),
            RdNameStatus = rd is null ? Severity.Ok : rdNameStatus,
            RdElementNo = rd?.ElementNo ?? "",
            RdElementName = OneLine(rd?.ElementName),
            RdElementStatus = rd is null ? Severity.Ok : rdElementStatus,
            Surnames = rd is null ? "" : string.Join(", ", rd.Surnames),
            Signers = rd is null ? "" : string.Join(", ", rd.Signers),
            SurnameStatus = rd is null ? Severity.Ok : surnameStatus,
            Presence = presence,
            Overall = overall,
        };
    }

    private Severity CheckSurnames(string actLabel, string cipher, RdDocument rd)
    {
        if (rd.SignaturePath is null)
        {
            Add(actLabel, cipher, "Электронная подпись", rd.FileName, "", Severity.Critical,
                "Рядом с файлом комплекта нет отсоединённой подписи «" + rd.FileName + ".sig». Это критично.");
            return Severity.Critical;
        }

        if (!string.IsNullOrWhiteSpace(rd.SignatureError))
        {
            Add(actLabel, cipher, "Электронная подпись", rd.SignaturePath, rd.SignatureError, Severity.Critical,
                "Файл подписи найден, но сертификаты подписантов прочитать не удалось.");
            return Severity.Critical;
        }

        var document = rd.Surnames.Where(s => !SurnameStem.IsIgnored(s, _options.IgnoredSigners)).ToList();
        var signers = rd.Signers.Where(s => !SurnameStem.IsIgnored(s, _options.IgnoredSigners)).ToList();
        var status = Severity.Ok;
        if (document.Count == 0 && rd.Surnames.Count == 0)
        {
            Add(actLabel, cipher, "Фамилии в комплекте", "", string.Join(", ", rd.Signers), Severity.Critical,
                "");
            status = Severity.Critical;
        }

        foreach (var surname in document)
        {
            if (signers.Any(s => SurnameStem.Same(s, surname)))
                continue;
            status = Severity.Critical;
            Add(actLabel, cipher, "Фамилия без подписи", surname, string.Join(", ", rd.Signers), Severity.Critical, "");
        }

        foreach (var signer in signers)
        {
            if (document.Any(s => SurnameStem.Same(s, signer)))
                continue;
            status = SeverityText.Worst(status, Severity.Warning);
            Add(actLabel, cipher, FindingGrouper.SignerMissing, signer, string.Join(", ", rd.Surnames), Severity.Warning, "");
        }

        return status;
    }

    private void NoteInternalArithmetic(string label, decimal? net, decimal? vat, decimal? gross)
    {
        if (net is null || gross is null)
            return;
        var messages = new List<string>();
        if (vat is not null && !TextMatch.MoneyEquals(net.Value + vat.Value, gross.Value))
        {
            messages.Add("без НДС " + TextMatch.FormatMoney(net.Value)
                         + " + НДС " + TextMatch.FormatMoney(vat.Value)
                         + " = " + TextMatch.FormatMoney(net.Value + vat.Value)
                         + ", а сумма с НДС в акте " + TextMatch.FormatMoney(gross.Value));
        }

        var fromNet = decimal.Round(net.Value * (1m + VatRate), 2, MidpointRounding.AwayFromZero);
        if (!TextMatch.MoneyEquals(gross.Value, fromNet))
        {
            messages.Add("без НДС × 1,22 = " + TextMatch.FormatMoney(fromNet)
                         + ", а сумма с НДС в акте " + TextMatch.FormatMoney(gross.Value));
        }

        if (messages.Count == 0)
            return;
        Add(label, "—", "Арифметика акта",
            net is null ? "" : TextMatch.FormatMoney(net.Value),
            TextMatch.FormatMoney(gross.Value),
            Severity.Critical,
            "Суммы внутри акта не сходятся: " + string.Join("; ", messages) + ". Ставка НДС в проверке — 22%, допуск 0,01 руб.");
    }

    private Severity TextCheck(string act, string kit, string check, string? left, string? right, string what)
    {
        var severity = TextMatch.Compare(left, right);
        if (severity == Severity.Ok)
            return severity;
        Add(act, kit, check, OneLine(left), OneLine(right), severity, "");
        _ = what;
        return severity;
    }

    private void Add(string act, string kit, string check, string valueA, string valueB, Severity severity, string explanation)
    {
        var (inAct, inRd, inVerd, inVesr) = FindingSources.Place(check, valueA, valueB);
        _findings.Add(new Finding(
            act,
            kit,
            check,
            valueA,
            valueB,
            severity,
            explanation,
            _currentElement,
            inAct,
            inRd,
            inVerd,
            inVesr));
    }

    private static string ElementTitle(string costName)
    {
        var match = WorkingDocumentationPrefix().Match(costName.Trim());
        return match.Success ? costName.Trim()[match.Length..].Trim() : costName.Trim();
    }

    private static decimal? Sum(IEnumerable<decimal?> values)
    {
        decimal? sum = null;
        foreach (var value in values)
        {
            if (value is null)
                continue;
            sum = (sum ?? 0m) + value.Value;
        }
        return sum;
    }

    private static string OneLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return TextMatch.CollapseLines(value);
    }

    private static string ResolvedPreview(ActDocument act) =>
        act.ElementNoFromFileName ?? act.ElementNoFromHeading ?? act.Kits.Select(k => k.ElementNo).FirstOrDefault(s => s.Length > 0) ?? act.DisplayName;

    private static int[] ElementSortKey(string value)
    {
        var match = ElementSortPattern().Match(value);
        var source = match.Success ? match.Value : value;
        return source.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part.Trim().TrimEnd('.'), out var number) ? number : int.MaxValue)
            .DefaultIfEmpty(int.MaxValue)
            .ToArray();
    }

    private static string? NameOf(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFileName(path);

    private static IEnumerable<string> DistinctFiles(IReadOnlyList<string> paths) =>
        paths.Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"^рабочая\s+документация\.\s*", RegexOptions.IgnoreCase)]
    private static partial Regex WorkingDocumentationPrefix();

    [GeneratedRegex(@"\d+\.\d+")]
    private static partial Regex ElementSortPattern();

    private readonly record struct VerdHit(VerdElement Element, VerdKit Kit);

    private sealed class IntArrayComparer : IComparer<int[]>
    {
        public static readonly IntArrayComparer Instance = new();
        public int Compare(int[]? x, int[]? y)
        {
            x ??= [];
            y ??= [];
            var n = Math.Min(x.Length, y.Length);
            for (var i = 0; i < n; i++)
            {
                var c = x[i].CompareTo(y[i]);
                if (c != 0)
                    return c;
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
