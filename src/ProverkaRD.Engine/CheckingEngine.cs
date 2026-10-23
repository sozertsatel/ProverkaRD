using System.Globalization;
using System.Text.RegularExpressions;
using ProverkaRD.Engine.Parsing;

namespace ProverkaRD.Engine;

public sealed partial class CheckingEngine
{
    private readonly List<Finding> _findings = [];
    private readonly List<RdDocument> _stray = [];
    private bool _rdFilesLoaded;
    private string _currentElement = "";
    private string _currentActPath = "";
    private CheckOptions _options = new();

    public CheckReport Run(CheckInput input, IProgress<CheckProgress>? progress = null)
    {
        _findings.Clear();
        _stray.Clear();
        _currentElement = "";
        _currentActPath = "";
        _options = input.Options ?? new CheckOptions();
        var report = new CheckReport
        {
            VerdFileName = NameOf(input.VerdPath),
            VesrFileName = NameOf(input.VesrPath),
        };
        var total = CountSteps(input);
        var done = 0;
        void Tick(string name)
        {
            done++;
            progress?.Report(new CheckProgress(Math.Min(done, Math.Max(total, 1)), Math.Max(total, 1), SafePath.FileName(name)));
        }

        VerdBook? verd = null;
        if (string.IsNullOrWhiteSpace(input.VerdPath))
        {
            report.VerdMissing = true;
            report.Problems.Add("ВЭРД не загружена.");
        }
        else if (!File.Exists(SafePath.ForIo(input.VerdPath)))
        {
            report.VerdUnreadable = true;
            report.Problems.Add("Файл ВЭРД не найден: " + SafePath.FileName(input.VerdPath));
            Tick(input.VerdPath);
        }
        else
        {
            Tick(input.VerdPath);
            verd = VerdParser.Parse(input.VerdPath);
            report.Problems.AddRange(verd.Errors);
            Absorb(report, "ВЭРД", verd.Notices);
            if (verd.Errors.Any(FileProblems.IsLockedMessage))
                report.LockedFiles++;
            else if (verd.Errors.Count > 0)
                report.VerdUnreadable = true;
        }

        VesrBook? vesr = null;
        if (string.IsNullOrWhiteSpace(input.VesrPath))
        {
            report.VesrMissing = true;
            report.Problems.Add("ВЭСР не загружена.");
        }
        else if (!File.Exists(SafePath.ForIo(input.VesrPath)))
        {
            report.VesrUnreadable = true;
            report.Problems.Add("Файл ВЭСР не найден: " + SafePath.FileName(input.VesrPath));
            Tick(input.VesrPath);
        }
        else
        {
            Tick(input.VesrPath);
            vesr = VesrParser.Parse(input.VesrPath);
            report.Problems.AddRange(vesr.Errors);
            Absorb(report, "ВЭСР", vesr.Notices);
            if (vesr.Errors.Any(FileProblems.IsLockedMessage))
                report.LockedFiles++;
            else if (vesr.Errors.Count > 0)
                report.VesrUnreadable = true;
        }

        foreach (var problem in report.Problems)
        {
            Add("—", "—", "Исходные данные", problem, "", Severity.Critical, problem);
            Remember(report, "Исходные данные", problem, problem);
        }

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
            Tick(path);
            var act = ActParser.Parse(path);
            acts.Add(act);
            foreach (var error in act.Errors)
            {
                Add(act.DisplayName, "—", "Чтение акта", error, "", Severity.Critical, error);
                Remember(report, "Акт", act.FilePath, error);
            }
            if (act.Errors.Any(FileProblems.IsLockedMessage))
                report.LockedFiles++;
            Absorb(report, "Акт", act.Notices);
        }

        var rdFiles = new List<RdDocument>();
        foreach (var path in DistinctFiles(input.RdPaths))
        {
            Tick(path);
            if (Directory.Exists(SafePath.ForIo(path)))
            {
                Remember(report, "РД", path, "Это папка. Комплекты из неё читаются по списку файлов.");
                continue;
            }
            if (path.EndsWith(".sig", StringComparison.OrdinalIgnoreCase))
            {
                Remember(report, "РД", path, "Файл подписи не является комплектом. Он учитывается при проверке ЭЦП.");
                continue;
            }
            if (RdFileRules.IsAuxiliary(path))
            {
                Remember(report, "РД", path, "Пропущен вспомогательный файл .СВОР/_ИЗМ.");
                continue;
            }
            var rd = RdParser.Parse(path);
            if (RdFileRules.IsStray(rd))
            {
                _stray.Add(rd);
                Add("—", "—", "Посторонний файл", rd.FileName, "", Severity.Warning, "");
                foreach (var error in rd.Errors)
                    Remember(report, "РД", rd.FileName, error);
                if (rd.Errors.Any(FileProblems.IsLockedMessage))
                    report.LockedFiles++;
                Absorb(report, "РД", rd.Notices);
                continue;
            }
            rdFiles.Add(rd);
            foreach (var error in rd.Errors)
            {
                Add(rd.FileName, rd.Cipher, "Чтение комплекта", error, "", Severity.Critical, error);
                Remember(report, "РД", rd.FilePath, error);
            }
            if (rd.Errors.Any(FileProblems.IsLockedMessage))
                report.LockedFiles++;
            Absorb(report, "РД", rd.Notices);
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
        foreach (var row in BuildRegistry(report, rdFiles))
            report.Registry.Add(row);
        report.Load = BuildLoadStats(report, input, acts, rdFiles, usedRd);
        return report;
    }

    private decimal VatFraction => _options.VatPercent / 100m;

    private string VatPercentText => _options.VatPercent.ToString("0.##", CultureInfo.GetCultureInfo("ru-RU"));

    private void CheckAct(
        ActDocument act,
        Dictionary<string, List<VerdElement>> verdByNumber,
        VesrBook? vesr,
        Dictionary<string, List<RdDocument>> rdByCipher,
        HashSet<RdDocument> usedRd,
        CheckReport report)
    {
        var label = act.DisplayName;
        _currentActPath = act.FilePath;
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
            codeStatus = EmitField(label, "—", "Код ДГП", actCode, "", verdCode, "");
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
            : TextMatch.Compare(actElementName, element.Name);
        if (element is not null && act.Kits.Count == 0 && nameStatus != Severity.Ok)
            EmitField(label, "—", "Наименование элемента", actElementName, "", element.Name, "");

        var costNameStatus = Severity.Critical;
        if (vesrByAct is not null)
            costNameStatus = EmitField(label, "—", "Наименование стоимости", actCostName, "", "", vesrByAct.Name);

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
        else if (!TextMatch.SameKopecks(actNet.Value, vesrNet.Value))
        {
            netStatus = Severity.Critical;
            var delta = actNet.Value - vesrNet.Value;
            Add(label, "—", "Стоимость без НДС", TextMatch.FormatMoney(actNet.Value), TextMatch.FormatMoney(vesrNet.Value),
                Severity.Critical,
                "Сумма без НДС в акте не равна столбцу «Всего» ВЭСР по коду " + actCode + ". Разница " + TextMatch.FormatMoney(delta) + " руб.");
        }

        decimal? recalc = vesrNet is null ? null : decimal.Round(vesrNet.Value * (1m + VatFraction), 2, MidpointRounding.AwayFromZero);
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
        else if (!TextMatch.SameKopecks(actGross.Value, recalc.Value))
        {
            grossStatus = Severity.Critical;
            Add(label, "—", "Стоимость с НДС", TextMatch.FormatMoney(actGross.Value), TextMatch.FormatMoney(recalc.Value),
                Severity.Critical,
                "Сумма с НДС в акте не равна пересчёту «Всего» ВЭСР × (1 + " + VatPercentText + "%).");
        }

        decimal? vatCalc = vesrNet is null ? null : decimal.Round(vesrNet.Value * VatFraction, 2, MidpointRounding.AwayFromZero);
        var vatStatus = Severity.Ok;
        if (vesrNet is not null && actVat is null)
        {
            vatStatus = Severity.Critical;
            Add(label, "—", "Сумма НДС", "", TextMatch.FormatMoney(vatCalc!.Value),
                Severity.Critical, "В акте нет суммы НДС.");
        }
        else if (vesrNet is not null && actVat is not null && !TextMatch.SameKopecks(actVat.Value, vatCalc!.Value))
        {
            vatStatus = Severity.Critical;
            Add(label, "—", "Сумма НДС", TextMatch.FormatMoney(actVat.Value), TextMatch.FormatMoney(vatCalc.Value),
                Severity.Critical,
                "НДС в акте не равен «Всего» ВЭСР × " + VatPercentText + "%.");
        }

        var unitStatus = Severity.Ok;
        decimal? vesrUnit = vesrByAct?.UnitPrice;
        if (vesrByAct is not null && actNet is not null)
        {
            if (vesrUnit is null)
            {
                unitStatus = Severity.Critical;
                Add(label, "—", "Цена за единицу", TextMatch.FormatMoney(actNet.Value), "",
                    Severity.Critical, "В ВЭСР нет цены за единицу по коду " + actCode + ".");
            }
            else if (!TextMatch.SameKopecks(actNet.Value, vesrUnit.Value))
            {
                unitStatus = Severity.Critical;
                Add(label, "—", "Цена за единицу", TextMatch.FormatMoney(actNet.Value), TextMatch.FormatMoney(vesrUnit.Value),
                    Severity.Critical, "Цена за единицу в ВЭСР не равна стоимости без НДС в акте.");
            }
        }

        NoteInternalArithmetic(label, actNet, actVat, actGross);

        if (costLine is not null && vesrByAct is not null)
        {
            if (costLine.Unit.Length > 0 || vesrByAct.Unit.Length > 0)
                EmitField(label, "—", "Единица измерения", costLine.Unit, "", "", vesrByAct.Unit);
            if (costLine.Quantity is decimal qty && vesrByAct.Quantity is decimal vesrQty
                && !TextMatch.MoneyEquals(qty, vesrQty))
            {
                Add(label, "—", "Количество", qty.ToString(CultureInfo.InvariantCulture), vesrQty.ToString(CultureInfo.InvariantCulture),
                    Severity.Critical, "Количество в акте не равно количеству в ВЭСР.");
            }
        }

        var completeness = CheckCompleteness(label, act, element, actElementName, rdByCipher, usedRd, report, out var completenessText);

        var overall = SeverityText.Worst(nameStatus, costNameStatus, codeStatus, netStatus, vatStatus, grossStatus, unitStatus, completeness);
        // Внутренняя арифметика уже попала в замечания и должна красить итог.
        if (_findings.Any(f => f.Act == label && f.CheckName == "Арифметика акта" && f.Severity == Severity.Critical))
            overall = Severity.Critical;

        report.Acts.Add(new ActResult
        {
            FileName = label,
            SourcePath = act.FilePath,
            AdvanceNet = act.AdvanceNet,
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
            VatRecalculated = vatCalc,
            VatStatus = vatStatus,
            ActGross = actGross,
            GrossRecalculated = recalc,
            GrossStatus = grossStatus,
            VesrUnitPrice = vesrUnit,
            UnitPriceStatus = unitStatus,
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
            RdDocument? rd = null;
            if (rdByCipher.TryGetValue(TextMatch.CodeKey(kit.Cipher), out var files))
            {
                rd = files[0];
                usedRd.Add(rd);
            }

            if (verdKit is not null || rd is not null)
            {
                var compared = CompareMany(kit.Name, verdKit?.Name ?? "", rd?.KitName ?? "");
                if (compared == Severity.Critical)
                    nameProblems++;
                else if (compared == Severity.Formatting)
                    nameFormatting++;
                statuses.Add(compared);
            }

            var row = BuildKitRow(label, act.FilePath, act.AdvanceNet, element, kit.DgpCode.Length > 0 ? kit.DgpCode : element?.DgpCode ?? "", kit, verdKit, rd, actElementName);
            report.Kits.Add(row);
        }

        foreach (var verdKit in verdKits.Where(k => !actKeys.Contains(TextMatch.CodeKey(k.Cipher))))
        {
            report.Kits.Add(BuildKitRow(label, act.FilePath, act.AdvanceNet, element, element?.DgpCode ?? "", null, verdKit, null, null));
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
        if (nameFormatting > 0)
            parts.Add("оформление наименований: " + nameFormatting);
        if (nameProblems == 0 && nameFormatting == 0 && missing.Count == 0 && extra.Count == 0 && element is not null)
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

        _currentActPath = "";
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

        report.Kits.Add(BuildKitRow("—", "", null, element, element?.DgpCode ?? "", null, kit, rd, null));
    }

    private KitResult BuildKitRow(
        string actLabel,
        string actPath,
        decimal? advance,
        VerdElement? element,
        string dgp,
        ActKit? actKit,
        VerdKit? verdKit,
        RdDocument? rd,
        string? actElementName)
    {
        var cipher = actKit?.Cipher ?? verdKit?.Cipher ?? rd?.Cipher ?? "";
        var actCipher = actKit?.Cipher ?? "";
        var verdCipher = verdKit?.Cipher ?? "";
        var titleCipher = rd?.CipherFromText ?? "";
        var fileCipher = rd?.CipherFromFileName ?? "";
        var shownTitle = titleCipher;
        if (titleCipher.Length > 0 && fileCipher.Length > 0 && TextMatch.Compare(titleCipher, fileCipher) != Severity.Ok)
            shownTitle = titleCipher + " / файл: " + fileCipher;
        else if (titleCipher.Length == 0)
            shownTitle = fileCipher;

        var cipherStatus = CompareMany(actCipher, verdCipher, titleCipher, fileCipher);
        var cipherBlame = cipherStatus == Severity.Ok ? "" : SourceBlame.Of(actCipher, titleCipher, verdCipher);
        if (cipherStatus != Severity.Ok)
        {
            var (left, right) = FirstMismatch(actCipher, verdCipher, titleCipher, fileCipher);
            _findings.Add(new Finding(
                actLabel,
                cipher,
                "Шифр",
                TextMatch.ForReport(left),
                TextMatch.ForReport(right),
                cipherStatus,
                "",
                _currentElement,
                TextMatch.ForReport(actCipher),
                TextMatch.ForReport(shownTitle),
                TextMatch.ForReport(verdCipher),
                "",
                _currentActPath,
                ErrorWhere: cipherBlame));
        }

        var actName = actKit?.Name ?? "";
        var verdName = verdKit?.Name ?? "";
        var rdName = rd?.KitName ?? "";
        var nameStatus = actKit is null && verdKit is null && rd is null
            ? Severity.Ok
            : EmitField(actLabel, cipher, "Наименование комплекта", actName, rdName, verdName, "");
        if (actKit is not null && verdKit is null)
            nameStatus = SeverityText.Worst(nameStatus, Severity.Critical);
        if (actKit is null && verdKit is not null)
            nameStatus = SeverityText.Worst(nameStatus, Severity.Critical);
        var nameBlame = nameStatus == Severity.Ok ? "" : SourceBlame.Of(actName, rdName, verdName);

        var actElement = actElementName ?? "";
        var verdElement = element?.Name ?? "";
        var rdElement = rd?.ElementName ?? "";
        var elementNameStatus = EmitField(actLabel, cipher, "Наименование элемента", actElement, rdElement, verdElement, "");
        var elementNameBlame = elementNameStatus == Severity.Ok ? "" : SourceBlame.Of(actElement, rdElement, verdElement);

        var rdElementNo = rd?.ElementNo ?? "";
        var verdElementNo = element?.Number ?? "";
        var actElementNo = _currentElement;
        var elementNoStatus = Severity.Ok;
        if (rd is not null && element is not null && rdElementNo.Length == 0)
        {
            elementNoStatus = Severity.Critical;
            _findings.Add(new Finding(
                actLabel, cipher, "Номер элемента", "", verdElementNo, Severity.Critical, "",
                _currentElement, TextMatch.ForReport(actElementNo), "", TextMatch.ForReport(verdElementNo), "",
                _currentActPath));
        }
        else
        {
            elementNoStatus = EmitField(actLabel, cipher, "Номер элемента", actElementNo, rdElementNo, verdElementNo, "");
        }

        var pdfState = rd is null ? "нет" : "есть";
        var sigState = rd is null ? "нет" : rd.SignaturePath is null ? "нет" : "есть";
        var fileStatus = Severity.Ok;
        if (rd is null && _rdFilesLoaded && actKit is not null)
        {
            fileStatus = Severity.Warning;
            Add(actLabel, cipher, "Файл комплекта РД", cipher, "", Severity.Warning, "");
        }

        var surnameStatus = Severity.Ok;
        var missingSigners = "";
        if (rd is not null)
        {
            (surnameStatus, missingSigners) = AuditSigners(actLabel, cipher, rd);
            if (rd.SignaturePath is null || rd.SignatureRejected)
                fileStatus = SeverityText.Worst(fileStatus, Severity.Critical);
            else if (rd.SignatureNameDiffers)
                fileStatus = SeverityText.Worst(fileStatus, Severity.Formatting);
        }

        var presence = rd is null
            ? (_rdFilesLoaded ? "файл не загружен" : "файл не прикладывался")
            : actKit is null ? "нет в актах" : "загружен";

        var overall = SeverityText.Worst(cipherStatus, nameStatus, elementNameStatus, elementNoStatus, fileStatus, surnameStatus);
        return new KitResult
        {
            ElementNo = element?.Number ?? rd?.ElementNo ?? actElementNo,
            ActName = actLabel,
            ActNo = actElementNo.Length > 0 ? actElementNo : "—",
            SourcePath = actPath,
            AdvanceNet = advance,
            Cipher = cipher,
            ActCipher = actCipher,
            VerdCipher = verdCipher,
            TitleCipher = shownTitle,
            CipherStatus = cipherStatus,
            CipherBlame = cipherBlame,
            DgpCode = dgp,
            ActKitName = OneLine(actName),
            VerdKitName = OneLine(verdName),
            NameStatus = nameStatus,
            NameBlame = nameBlame,
            PaperCopies = actKit?.PaperCopies ?? "",
            ElectronicCopies = actKit?.ElectronicCopies ?? "",
            RdFileName = rd?.FileName ?? "",
            RdCipher = titleCipher,
            RdKitName = OneLine(rdName),
            RdNameStatus = nameStatus,
            ActElementName = OneLine(actElement),
            VerdElementName = OneLine(verdElement),
            RdElementNo = rdElementNo,
            RdElementName = OneLine(rdElement),
            RdElementStatus = SeverityText.Worst(elementNameStatus, elementNoStatus),
            ElementNameStatus = elementNameStatus,
            ElementNameBlame = elementNameBlame,
            PdfState = pdfState,
            SigState = sigState,
            Surnames = rd is null ? "" : string.Join(", ", rd.Surnames),
            MissingSigners = missingSigners,
            Signers = rd is null ? "" : string.Join(", ", rd.Signers),
            SurnameStatus = rd is null ? Severity.Ok : surnameStatus,
            Presence = presence,
            Overall = overall,
        };
    }

    private (Severity Status, string Missing) AuditSigners(string actLabel, string cipher, RdDocument rd)
    {
        var signers = MatchSigners(rd);
        var audit = SignerAudit.Check(
            rd.Surnames,
            signers,
            rd.SignaturePath is not null,
            rd.SignatureError,
            _options.IgnoredSigners);
        var status = Severity.Ok;

        if (rd.SignatureRejected)
        {
            var text = "Подпись не относится к файлу";
            if (!string.IsNullOrWhiteSpace(rd.RejectedSignatureName))
                text += " (" + rd.RejectedSignatureName + ")";
            if (!string.IsNullOrWhiteSpace(rd.CoveringSignatureName))
                text += ". Подходит " + rd.CoveringSignatureName;
            Add(actLabel, cipher, "Подпись не относится к файлу", text, "", Severity.Critical, "");
            status = Severity.Critical;
            if (rd.SignaturePath is null)
                return (Severity.Critical, "");
        }
        else if (rd.SignatureNameDiffers && rd.SignaturePath is not null)
        {
            Add(actLabel, cipher, "Электронная подпись",
                "Ошибка оформления: файл подписи назван иначе (" + Path.GetFileName(rd.SignaturePath) + ")",
                "",
                Severity.Formatting,
                "");
            status = Severity.Formatting;
        }

        if (rd.SignaturePath is null)
        {
            Add(actLabel, cipher, "Электронная подпись", rd.FileName, "", Severity.Critical, "");
            return (Severity.Critical, "");
        }

        if (!string.IsNullOrWhiteSpace(rd.SignatureError))
        {
            Add(actLabel, cipher, "Электронная подпись", rd.SignaturePath ?? "", rd.SignatureError, Severity.Critical, "");
            return (Severity.Critical, "");
        }

        if (audit.NoSurnames)
        {
            Add(actLabel, cipher, "Фамилии в комплекте", "", string.Join(", ", signers), Severity.Critical, "");
            status = Severity.Critical;
        }
        else
        {
            foreach (var surname in audit.Missing)
                Add(actLabel, cipher, "Фамилия без подписи", surname, string.Join(", ", signers), Severity.Critical, "");
            status = SeverityText.Worst(status, audit.Status);
        }

        status = SeverityText.Worst(status, NoteCertificates(actLabel, cipher, rd));
        return (status, string.Join(", ", audit.Missing));
    }

    private Severity NoteCertificates(string actLabel, string cipher, RdDocument rd)
    {
        var status = Severity.Ok;
        foreach (var person in rd.CertificateSigners)
        {
            if (person.TimeStatus == CertTimeStatus.Expired)
            {
                Add(actLabel, cipher, "Срок сертификата",
                    "Сертификат истёк на момент подписания: " + person.DisplayName, "", Severity.Critical, "");
                status = Severity.Critical;
            }
            else if (person.TimeStatus == CertTimeStatus.NotYetValid)
            {
                Add(actLabel, cipher, "Срок сертификата",
                    "Сертификат ещё не действовал на момент подписания: " + person.DisplayName, "", Severity.Critical, "");
                status = Severity.Critical;
            }
            else if (person.TimeStatus == CertTimeStatus.SigningTimeMissing)
            {
                Add(actLabel, cipher, "Время подписания",
                    "В подписи нет времени подписания: " + person.DisplayName, "", Severity.Warning, "");
                status = SeverityText.Worst(status, Severity.Warning);
            }
        }

        return status;
    }

    private static List<string> MatchSigners(RdDocument rd)
    {
        var signers = rd.Signers.ToList();
        foreach (var person in rd.CertificateSigners)
        {
            if (person.MatchKey.Length == 0)
                continue;
            if (signers.Any(item => string.Equals(item, person.MatchKey, StringComparison.OrdinalIgnoreCase)))
                continue;
            signers.Add(person.MatchKey);
        }

        return signers;
    }

    private IEnumerable<RegistryRow> BuildRegistry(CheckReport report, List<RdDocument> rdFiles)
    {
        var documents = rdFiles
            .Concat(_stray)
            .Where(rd => IsPdf(rd.FilePath))
            .OrderBy(rd => rd.FileName, StringComparer.CurrentCultureIgnoreCase);
        foreach (var rd in documents)
            yield return RegistryOf(report, rd);
    }

    private RegistryRow RegistryOf(CheckReport report, RdDocument rd)
    {
        var elementNo = rd.ElementNo;
        if (elementNo.Length == 0)
        {
            var kit = report.Kits.FirstOrDefault(item =>
                item.RdFileName.Equals(rd.FileName, StringComparison.OrdinalIgnoreCase)
                && item.ElementNo.Length > 0);
            if (kit is not null)
                elementNo = kit.ElementNo;
        }

        var elementName = OneLine(rd.ElementName);
        var element = elementNo.Length == 0
            ? elementName
            : elementName.Length == 0 ? elementNo : elementNo + " " + elementName;
        if (RdFileRules.IsStray(rd))
        {
            return new RegistryRow
            {
                FileName = rd.FileName,
                Cipher = rd.Cipher,
                Element = element,
                Surnames = string.Join(", ", rd.Surnames),
                Signers = "—",
                CertValidity = "—",
                CertStatus = Severity.Ok,
                SignatureStatus = Severity.Warning,
                Overall = Severity.Warning,
            };
        }

        var signature = SignatureState(rd);
        var surnames = SurnameState(rd);
        var cert = rd.CertificateSigners.Count == 0 ? Severity.Ok : rd.CertStatus;
        var signerText = rd.CertificateSigners.Count == 0
            ? string.Join(", ", rd.Signers)
            : string.Join(", ", rd.CertificateSigners.Select(person => person.DisplayName));
        var certText = rd.CertValidityText;
        if (certText.Length == 0)
            certText = "—";
        return new RegistryRow
        {
            FileName = rd.FileName,
            Cipher = rd.Cipher,
            Element = element,
            Surnames = string.Join(", ", rd.Surnames),
            Signers = signerText,
            CertValidity = certText,
            CertStatus = cert,
            SignatureStatus = signature,
            Overall = SeverityText.Worst(signature, surnames, cert),
        };
    }

    private Severity SurnameState(RdDocument rd)
    {
        if (rd.SignaturePath is null || !string.IsNullOrWhiteSpace(rd.SignatureError))
            return Severity.Critical;
        return SignerAudit.Check(
            rd.Surnames,
            MatchSigners(rd),
            true,
            null,
            _options.IgnoredSigners).Status;
    }

    private static Severity SignatureState(RdDocument rd)
    {
        if (rd.SignatureRejected || rd.SignaturePath is null || !string.IsNullOrWhiteSpace(rd.SignatureError))
            return Severity.Critical;
        if (rd.SignatureNameDiffers)
            return Severity.Formatting;
        return Severity.Ok;
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

        var fromNet = decimal.Round(net.Value * (1m + VatFraction), 2, MidpointRounding.AwayFromZero);
        if (!TextMatch.SameKopecks(gross.Value, fromNet))
        {
            messages.Add("без НДС × (1 + " + VatPercentText + "%) = " + TextMatch.FormatMoney(fromNet)
                         + ", а сумма с НДС в акте " + TextMatch.FormatMoney(gross.Value));
        }

        if (messages.Count == 0)
            return;
        Add(label, "—", "Арифметика акта",
            net is null ? "" : TextMatch.FormatMoney(net.Value),
            TextMatch.FormatMoney(gross.Value),
            Severity.Critical,
            "Суммы внутри акта не сходятся: " + string.Join("; ", messages) + ". Ставка НДС в проверке — " + VatPercentText + "%.");
    }

    private void Add(string act, string kit, string check, string valueA, string valueB, Severity severity, string explanation)
    {
        _ = explanation;
        var (inAct, inRd, inVerd, inVesr) = FindingSources.Place(check, valueA, valueB);
        _findings.Add(new Finding(
            act,
            kit,
            check,
            valueA,
            valueB,
            severity,
            "",
            _currentElement,
            inAct,
            inRd,
            inVerd,
            inVesr,
            _currentActPath));
    }

    private static Severity CompareMany(params string[] values)
    {
        var filled = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        var severity = Severity.Ok;
        for (var i = 0; i < filled.Count; i++)
        {
            for (var j = i + 1; j < filled.Count; j++)
                severity = SeverityText.Worst(severity, TextMatch.Compare(filled[i], filled[j]));
        }
        return severity;
    }

    private static (string Left, string Right) FirstMismatch(params string[] values)
    {
        var filled = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        foreach (var left in filled)
        {
            foreach (var right in filled)
            {
                if (TextMatch.Compare(left, right) != Severity.Ok)
                    return (left, right);
            }
        }
        return ("", "");
    }

    private Severity EmitField(
        string act,
        string kit,
        string check,
        string inAct,
        string inRd,
        string inVerd,
        string inVesr,
        params string[] also)
    {
        var compare = new List<string>();
        void Take(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                compare.Add(value);
        }

        Take(inAct);
        Take(inRd);
        Take(inVerd);
        Take(inVesr);
        foreach (var value in also)
            Take(value);

        var severity = Severity.Ok;
        string left = "";
        string right = "";
        for (var i = 0; i < compare.Count; i++)
        {
            for (var j = i + 1; j < compare.Count; j++)
            {
                var pair = TextMatch.Compare(compare[i], compare[j]);
                if (pair == Severity.Ok)
                    continue;
                if (left.Length == 0 || pair > severity)
                {
                    left = compare[i];
                    right = compare[j];
                }
                severity = SeverityText.Worst(severity, pair);
            }
        }

        if (severity == Severity.Ok)
            return severity;

        _findings.Add(new Finding(
            act,
            kit,
            check,
            TextMatch.ForReport(left),
            TextMatch.ForReport(right),
            severity,
            "",
            _currentElement,
            TextMatch.ForReport(inAct),
            TextMatch.ForReport(inRd),
            TextMatch.ForReport(inVerd),
            TextMatch.ForReport(inVesr),
            _currentActPath,
            ErrorWhere: SourceBlame.Of(inAct, inRd, inVerd)));
        return severity;
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

    private LoadStats BuildLoadStats(
        CheckReport report,
        CheckInput input,
        List<ActDocument> acts,
        List<RdDocument> rdFiles,
        HashSet<RdDocument> usedRd)
    {
        var actsLoaded = acts.Count > 0;
        var matchedPdf = 0;
        var loosePdf = 0;
        var matchedDocx = 0;
        var looseDocx = 0;
        foreach (var rd in rdFiles)
        {
            var matched = usedRd.Contains(rd);
            if (IsPdf(rd.FilePath))
            {
                if (matched)
                    matchedPdf++;
                else if (actsLoaded)
                    loosePdf++;
            }
            else if (IsDocx(rd.FilePath))
            {
                if (matched)
                    matchedDocx++;
                else if (actsLoaded)
                    looseDocx++;
            }
        }

        var strayPdf = _stray.Count(rd => IsPdf(rd.FilePath));
        var strayDocx = _stray.Count(rd => IsDocx(rd.FilePath));
        var (auxiliaryFiles, signatureFiles) = ScanSideFiles(input);
        foreach (var file in auxiliaryFiles)
            Remember(report, "РД", file, "Пропущен вспомогательный файл .СВОР/_ИЗМ.");
        var auxiliary = auxiliaryFiles.Count;
        var signatures = signatureFiles.Count;
        var pdfs = matchedPdf + loosePdf + strayPdf;
        var docx = matchedDocx + looseDocx + strayDocx;
        var kits = acts.Sum(act => act.Kits.Count);
        var text = "Актов: " + acts.Count
            + ". Комплектов в актах: " + kits
            + ", найдено PDF: " + pdfs
            + ". Из PDF к актам подошло: " + matchedPdf
            + ", «Комплект вне актов»: " + loosePdf
            + ", «Посторонний файл»: " + strayPdf
            + ". Пропущено .СВОР/_ИЗМ: " + auxiliary
            + ". Файлов .sig: " + signatures
            + ".";
        if (docx > 0)
        {
            text += " DOCX: " + docx
                + " (подошло " + matchedDocx
                + ", «Комплект вне актов»: " + looseDocx
                + ", «Посторонний файл»: " + strayDocx
                + ").";
        }

        text += " Ставка НДС: " + VatPercentText + "%.";
        return new LoadStats
        {
            Acts = acts.Count,
            ActSets = kits,
            Pdfs = pdfs,
            MatchedPdfs = matchedPdf,
            LoosePdfs = loosePdf,
            StrayPdfs = strayPdf,
            Docx = docx,
            MatchedDocx = matchedDocx,
            LooseDocx = looseDocx,
            StrayDocx = strayDocx,
            Auxiliary = auxiliary,
            Signatures = signatures,
            Text = text,
        };
    }

    private static int CountSteps(CheckInput input) =>
        (string.IsNullOrWhiteSpace(input.VerdPath) ? 0 : 1)
        + (string.IsNullOrWhiteSpace(input.VesrPath) ? 0 : 1)
        + input.ActPaths.Count(path => !string.IsNullOrWhiteSpace(path))
        + input.RdPaths.Count(path => !string.IsNullOrWhiteSpace(path));

    private static void Absorb(CheckReport report, string source, IEnumerable<Notice> notices)
    {
        foreach (var notice in notices)
            Remember(report, notice.Source.Length == 0 ? source : notice.Source, notice.Place, notice.Message);
    }

    private static void Remember(CheckReport report, string source, string place, string message)
    {
        if (report.Notices.Any(notice =>
                string.Equals(notice.Source, source, StringComparison.Ordinal)
                && string.Equals(notice.Place, place, StringComparison.OrdinalIgnoreCase)
                && string.Equals(notice.Message, message, StringComparison.Ordinal)))
            return;
        report.Notices.Add(new Notice(source, place, message));
    }

    private static (HashSet<string> Auxiliary, HashSet<string> Signatures) ScanSideFiles(CheckInput input)
    {
        var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var auxiliary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in input.RdFolders)
            ScanDirectory(folder, recursive: true, signatures, auxiliary);
        foreach (var path in input.RdPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            if (Directory.Exists(SafePath.ForIo(path)))
                ScanDirectory(path, recursive: true, signatures, auxiliary);
            else if (File.Exists(SafePath.ForIo(path)))
            {
                var parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent))
                    ScanDirectory(parent, recursive: false, signatures, auxiliary);
            }
        }

        return (auxiliary, signatures);
    }

    private static void ScanDirectory(string directory, bool recursive, HashSet<string> signatures, HashSet<string> auxiliary)
    {
        var root = SafePath.ForIo(directory);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return;
        try
        {
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var file in Directory.EnumerateFiles(root, "*", option))
            {
                if (file.EndsWith(".sig", StringComparison.OrdinalIgnoreCase))
                    signatures.Add(Path.GetFullPath(file));
                else if (RdFileRules.IsAuxiliary(file))
                    auxiliary.Add(Path.GetFullPath(file));
            }
        }
        catch
        {
            // Недоступная папка не должна срывать уже посчитанную проверку.
        }
    }

    private static bool IsPdf(string path) => path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    private static bool IsDocx(string path) => path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

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
