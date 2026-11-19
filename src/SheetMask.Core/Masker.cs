using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace SheetMask.Core;

/// <summary>
/// Applies a <see cref="WorkbookPlan"/>: two passes over the workbook. Pass 1 collects every real value so that
/// no replacement can ever equal a real value; pass 2 writes replacements. The same real value always gets the
/// same replacement, across columns and sheets.
/// </summary>
public sealed class Masker
{
    private readonly WorkbookPlan _plan;
    private readonly MaskOptions _o;

    // domain → normalised real → (display real, fake)
    private readonly Dictionary<string, Dictionary<string, (string Real, string Fake)>> _maps = new();
    private readonly HashSet<string> _reals = new();       // Norm of every real string value
    private readonly HashSet<string> _usedFakes = new();   // Norm of every fake handed out
    private readonly Dictionary<string, Queue<string>> _pools = new();
    private readonly Dictionary<string, int> _categoryCounters = new();
    private readonly Dictionary<(string Sheet, int Col), double> _factors = new();
    private readonly Dictionary<(string Sheet, int Col), (double Min, double Max, bool Integers)> _ranges = new();
    private readonly List<double> _realNumbers = new();
    private readonly HashSet<(string Sheet, int Row, int Col)> _done = new();

    private double _sharedFactor;
    private int _dateShift;
    private Regex? _knownRx;
    private Dictionary<string, string>? _knownDomain;      // Norm(real) → domain
    private int _cellsChanged;
    private int _textReplacements;

    public Masker(WorkbookPlan plan)
    {
        _plan = plan;
        _o = plan.Options;
    }

    /// <summary>Optional status callback ("collect", "write", "save" …) for long runs.</summary>
    public Action<string>? Progress { get; set; }

    private int _rowLimit;   // > 0 only for previews: process just the first N data rows of each sheet

    public MaskResult Run(string outputPath, string keyPath, string password)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("A password is required for the key file.");
        var result = new MaskResult { OutputPath = outputPath, KeyPath = keyPath };

        Progress?.Invoke("open");
        using (var wb = Book.Open(_plan.SourcePath))
        {
            Apply(wb, result);
            Progress?.Invoke("save");
            wb.SaveAs(outputPath);
        }

        Progress?.Invoke("key");
        KeyFile.Save(keyPath, BuildKey(), password);

        result.CellsChanged = _cellsChanged;
        result.TextReplacements = _textReplacements;
        result.Factor = _sharedFactor;
        result.DateShiftDays = _dateShift;
        Progress?.Invoke("leak");
        result.Leaks = LeakScanner.Scan(outputPath, SensitiveValues(), _realNumbers);
        if (_plan.AllColumns.Any(c => c.Kind is ColumnKind.RandomNumber or ColumnKind.Redact or ColumnKind.Clear))
            result.Notes.Add("Random-number, redacted and cleared columns cannot be turned back; their real values are not stored.");
        return result;
    }

    /// <summary>
    /// Masks only the first <paramref name="rows"/> data rows in memory (nothing is saved) and returns, per sheet,
    /// the original and masked cell texts of those rows (columns in plan order).
    /// </summary>
    public List<PreviewSheet> Preview(int rows, byte[]? sampleBook = null)
    {
        _rowLimit = Math.Max(1, rows);
        using var wb = sampleBook != null ? new XLWorkbook(new MemoryStream(sampleBook)) : Book.Open(_plan.SourcePath);
        var before = _plan.Sheets.Select(sp => Snapshot(wb, sp)).ToList();
        Apply(wb, new MaskResult());
        var list = new List<PreviewSheet>();
        for (var i = 0; i < _plan.Sheets.Count; i++)
            list.Add(new PreviewSheet { Name = _plan.Sheets[i].Name, Before = before[i], After = Snapshot(wb, _plan.Sheets[i]) });
        return list;
    }

    private List<List<string>> Snapshot(XLWorkbook wb, SheetPlan sp)
    {
        var rows = new List<List<string>>();
        if (!wb.TryGetWorksheet(sp.Name, out var ws)) return rows;
        var last = Math.Min(sp.LastRow, sp.FirstDataRow + _rowLimit - 1);
        for (var r = sp.FirstDataRow; r <= last; r++)
            rows.Add(sp.Columns.Select(c => ws.Cell(r, c.Column).GetFormattedString()).ToList());
        return rows;
    }

    private bool InScope(IXLWorksheet ws, int row)
    {
        if (_rowLimit <= 0) return true;
        var sp = _plan.Sheets.FirstOrDefault(p => p.Name == ws.Name);
        return sp == null || row < sp.FirstDataRow + _rowLimit;
    }

    /// <summary>Used cells of a sheet; in preview mode only the rows that will be shown.</summary>
    private IEnumerable<IXLCell> UsedCells(IXLWorksheet ws)
    {
        if (_rowLimit <= 0) return ws.CellsUsed();
        var sp = _plan.Sheets.FirstOrDefault(p => p.Name == ws.Name);
        var lastRow = Math.Min(ws.LastRowUsed()?.RowNumber() ?? 0, (sp?.FirstDataRow ?? 1) + _rowLimit - 1);
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        return lastRow < 1 || lastCol < 1 ? Enumerable.Empty<IXLCell>() : ws.Range(1, 1, lastRow, lastCol).CellsUsed();
    }

    private void Apply(XLWorkbook wb, MaskResult result)
    {
        _sharedFactor = Rng.Factor();
        _dateShift = Rng.Next(45, 400) * (Rng.Next(2) == 0 ? -1 : 1);
        if (_o.KeepWeekday) _dateShift = (int)Math.Round(_dateShift / 7.0) * 7;

        var masked = _plan.AllColumns.Where(c => c.Kind != ColumnKind.Keep).ToList();

        // ---- pass 1: collect real values
        Progress?.Invoke("collect");
        foreach (var col in masked)
        {
            var (ws, sp) = Sheet(wb, col);
            if (ws == null) continue;
            Collect(ws, sp!, col);
            if (col.Kind == ColumnKind.Scale)
                _factors[(col.Sheet, col.Column)] = _o.SharedFactor ? _sharedFactor : Rng.Factor();
        }

        // Names written inside text cells ("Prepared by: Kemal Öztürk") are real values too.
        if (_o.ScanAllText || masked.Any(c => c.Kind == ColumnKind.FreeText))
            foreach (var ws in wb.Worksheets)
            {
                var headerRow = _plan.Sheets.FirstOrDefault(p => p.Name == ws.Name)?.HeaderRow ?? 0;
                foreach (var cell in UsedCells(ws))
                    if (cell.Address.RowNumber != headerRow
                        && !cell.HasFormula && cell.Value.Type == XLDataType.Text)
                        foreach (Match m in NamePair.Matches(cell.Value.GetText()))
                            if (NameParts(m.Value) is { } parts)
                                foreach (var p in parts) _reals.Add(Text.Norm(p.Word));
            }

        // ---- pass 2: structured columns, then free text, then every other text cell
        Progress?.Invoke("write");
        foreach (var col in masked.Where(c => c.Kind != ColumnKind.FreeText))
        {
            var (ws, sp) = Sheet(wb, col);
            if (ws != null) WriteColumn(ws, sp!, col);
        }

        BuildKnownRegex();

        Progress?.Invoke("text");
        var shiftTextDates = masked.Any(c => c.Kind == ColumnKind.DateShift);
        foreach (var col in masked.Where(c => c.Kind == ColumnKind.FreeText))
        {
            var (ws, sp) = Sheet(wb, col);
            if (ws == null) continue;
            foreach (var cell in DataCells(ws, sp!, col))
            {
                _done.Add((ws.Name, cell.Address.RowNumber, col.Column));
                if (cell.Value.Type != XLDataType.Text) continue;
                var before = cell.Value.GetText();
                var after = ReplaceInText(before, shiftTextDates);
                if (after != before) { cell.Value = after; _cellsChanged++; }
                DropHyperlink(ws, cell);
            }
        }

        if (_o.ScanAllText)
        {
            foreach (var ws in wb.Worksheets)
            {
                var headerRow = _plan.Sheets.FirstOrDefault(p => p.Name == ws.Name)?.HeaderRow ?? 0;
                foreach (var cell in UsedCells(ws))
                {
                    if (cell.Address.RowNumber == headerRow) continue;
                    if (cell.HasFormula || _done.Contains((ws.Name, cell.Address.RowNumber, cell.Address.ColumnNumber))) continue;
                    if (cell.Value.Type != XLDataType.Text) continue;
                    var before = cell.Value.GetText();
                    var after = ReplaceInText(before, false);
                    if (after != before) { cell.Value = after; _cellsChanged++; }
                }
            }
        }

        foreach (var ws in wb.Worksheets)
        {
            if (_o.RemoveComments) ws.Clear(XLClearOptions.Comments);
            foreach (var h in ws.Hyperlinks.ToList())
            {
                var target = h.ExternalAddress?.ToString() ?? "";
                if (target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || target.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
                    try { ws.Hyperlinks.Delete(h.Cell.Address); } catch { /* best effort */ }
            }
        }

        if (_o.ClearProperties) ClearProperties(wb);

        Progress?.Invoke("formulas");
        try
        {
            wb.RecalculateAllFormulas();
        }
        catch (Exception ex)
        {
            result.Notes.Add("Formulas could not be recalculated here (" + ex.Message + "); Excel recalculates them on open.");
        }
        wb.FullCalculationOnLoad = true;
    }

    // ------------------------------------------------------------- pass 1

    private void Collect(IXLWorksheet ws, SheetPlan sp, ColumnPlan col)
    {
        double min = double.MaxValue, max = double.MinValue;
        var ints = true;
        foreach (var cell in DataCells(ws, sp, col))
        {
            var v = cell.Value;
            if (v.Type == XLDataType.Number && col.Kind is ColumnKind.Scale or ColumnKind.RandomNumber)
            {
                var n = v.GetNumber();
                min = Math.Min(min, n); max = Math.Max(max, n);
                if (n != Math.Floor(n)) ints = false;
                if (Math.Abs(n) >= 1000) _realNumbers.Add(n);
                continue;
            }
            if (v.Type == XLDataType.Text && col.Kind is ColumnKind.Scale or ColumnKind.RandomNumber
                && NumberShape.TryParse(v.GetText(), out var tn, out var shape))
            {
                min = Math.Min(min, tn); max = Math.Max(max, tn);
                if (shape.Decimals > 0) ints = false;
                continue;
            }
            var s = AsString(v);
            if (s == null) continue;
            foreach (var part in RealParts(col.Kind, s)) _reals.Add(Text.Norm(part));
        }
        if (col.Kind == ColumnKind.RandomNumber && min <= max)
            _ranges[(col.Sheet, col.Column)] = (min, max, ints);
    }

    private static IEnumerable<string> RealParts(ColumnKind kind, string s) => kind switch
    {
        ColumnKind.FirstName or ColumnKind.LastName or ColumnKind.FullName =>
            s.Split(' ', StringSplitOptions.RemoveEmptyEntries),
        _ => new[] { s }
    };

    // ------------------------------------------------------------- pass 2

    private void WriteColumn(IXLWorksheet ws, SheetPlan sp, ColumnPlan col)
    {
        foreach (var cell in DataCells(ws, sp, col))
        {
            var v = cell.Value;
            if (v.Type == XLDataType.Blank) continue;
            var changed = col.Kind switch
            {
                ColumnKind.Scale => WriteScale(cell, col),
                ColumnKind.RandomNumber => WriteRandom(cell, col),
                ColumnKind.DateShift => WriteDate(cell),
                ColumnKind.Clear => Clear(cell),
                ColumnKind.Redact => WriteRedacted(cell),
                _ => WriteMapped(cell, col)
            };
            if (changed)
            {
                _done.Add((ws.Name, cell.Address.RowNumber, col.Column));
                _cellsChanged++;
                DropHyperlink(ws, cell);
            }
        }
    }

    private bool WriteScale(IXLCell cell, ColumnPlan col)
    {
        var f = _factors[(col.Sheet, col.Column)];
        var v = cell.Value;
        if (v.Type == XLDataType.Number)
        {
            var n = v.GetNumber();
            cell.Value = Scale(n, f, n == Math.Floor(n), Decimals(n));
            return true;
        }
        if (v.Type == XLDataType.Text && NumberShape.TryParse(v.GetText(), out var tn, out var shape))
        {
            cell.Value = shape.Format(Scale(tn, f, shape.Decimals == 0, shape.Decimals));
            return true;
        }
        return false;
    }

    private double Scale(double v, double f, bool integer, int decimals)
    {
        var noise = _o.NoisePercent > 0 ? (Rng.NextDouble() * 2 - 1) * _o.NoisePercent / 100.0 : 0;
        var r = v * f * (1 + noise);
        if (!integer) return Math.Round(r, Math.Max(decimals, 2));
        if (_o.KeepRoundness && v != 0)
        {
            var step = 1.0;
            var a = Math.Abs(v);
            while (step < 1e6 && a % (step * 10) == 0) step *= 10;
            while (step > 1 && step / Math.Abs(r) > 0.01) step /= 10;   // keep the error under 0.5 %
            return Math.Round(r / step) * step;
        }
        return Math.Round(r);
    }

    private static int Decimals(double v)
    {
        var s = v.ToString("R", CultureInfo.InvariantCulture);
        var i = s.IndexOf('.');
        return i < 0 || s.Contains('E') ? 0 : Math.Min(4, s.Length - i - 1);
    }

    private bool WriteRandom(IXLCell cell, ColumnPlan col)
    {
        if (!_ranges.TryGetValue((col.Sheet, col.Column), out var r)) return false;
        double Draw(bool ints, int dec)
        {
            var x = r.Min + Rng.NextDouble() * (r.Max - r.Min);
            return ints ? Math.Round(x) : Math.Round(x, Math.Max(dec, 2));
        }
        var v = cell.Value;
        if (v.Type == XLDataType.Number) { cell.Value = Draw(r.Integers, Decimals(v.GetNumber())); return true; }
        if (v.Type == XLDataType.Text && NumberShape.TryParse(v.GetText(), out _, out var shape))
        {
            cell.Value = shape.Format(Draw(shape.Decimals == 0, shape.Decimals));
            return true;
        }
        return false;
    }

    private bool WriteDate(IXLCell cell)
    {
        var v = cell.Value;
        if (v.Type == XLDataType.DateTime) { cell.Value = v.GetDateTime().AddDays(_dateShift); return true; }
        if (v.Type == XLDataType.Text && DateText.TryParse(v.GetText(), out var d, out var fmt))
        {
            cell.Value = d.AddDays(_dateShift).ToString(fmt, CultureInfo.InvariantCulture);
            return true;
        }
        return false;
    }

    private static bool WriteRedacted(IXLCell cell)
    {
        var s = AsString(cell.Value);
        if (s == null) return false;
        cell.Value = Redact(s);
        return true;
    }

    /// <summary>Keeps the first and last character of every word: "Ahmet Yılmaz" → "A***t Y****z", "0532" → "0**2".</summary>
    public static string Redact(string s)
    {
        var words = s.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            var w = words[i];
            if (w.Length == 0) continue;
            words[i] = w.Length <= 2 ? new string('*', w.Length) : w[0] + new string('*', w.Length - 2) + w[^1];
        }
        return string.Join(' ', words);
    }

    private static bool Clear(IXLCell cell)
    {
        cell.Value = Blank.Value;
        return true;
    }

    private bool WriteMapped(IXLCell cell, ColumnPlan col)
    {
        var v = cell.Value;
        var s = AsString(v);
        if (s == null) return false;
        var fake = MapValue(col, s);
        if (fake == s) return false;
        if (v.Type == XLDataType.Number && double.TryParse(fake, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            && fake.Length == s.Length)
            cell.Value = n;
        else
            cell.Value = fake;
        return true;
    }

    private string MapValue(ColumnPlan col, string s)
    {
        switch (col.Kind)
        {
            case ColumnKind.FirstName:
                return MapTokens(s, _ => "first");
            case ColumnKind.LastName:
                return MapTokens(s, _ => "last");
            case ColumnKind.FullName:
            {
                var count = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                return MapTokens(s, i => count > 1 && i == count - 1 ? "last" : "first");
            }
            case ColumnKind.City: return Fake("city", s);
            case ColumnKind.Country: return Fake("country", s);
            case ColumnKind.Address: return Fake("address", s);
            case ColumnKind.Category: return Fake("cat:" + Text.Norm(col.Header), s, col.Header);
            case ColumnKind.Identifier: return Fake("id:" + Text.Norm(col.Header), s);
            // Pattern kinds only replace values that have the pattern; "TOTAL" in an IBAN column stays.
            case ColumnKind.NationalId:
                return Text.Digits(s).Length == 11 && s.Trim().All(char.IsDigit) ? Fake("tckn", s.Trim()) : s;
            case ColumnKind.Phone:
                return Text.Digits(s).Length >= 7 && !s.Any(char.IsLetter) ? Fake("phone", s) : s;
            case ColumnKind.Email:
                return Patterns.EmailExact.IsMatch(s.Trim()) ? Fake("email", s.Trim()) : s;
            case ColumnKind.Iban:
                return LooksIban(s) ? Fake("iban", s.Trim()) : s;
            default: return s;
        }
    }

    private static bool LooksIban(string s)
    {
        var c = s.Replace(" ", "").ToUpperInvariant();
        return c.Length is >= 15 and <= 34 && char.IsLetter(c[0]) && char.IsLetter(c[1]) && c[2..].All(char.IsLetterOrDigit) && c[2..4].All(char.IsDigit);
    }

    private string MapTokens(string s, Func<int, string> domainOf)
    {
        var parts = s.Split(' ');
        var idx = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            parts[i] = Fake(domainOf(idx), parts[i]);
            idx++;
        }
        return string.Join(' ', parts);
    }

    // ------------------------------------------------------------- mapping core

    private static string KeyOf(string domain, string real) => domain switch
    {
        "phone" => PhoneKey(real),
        "tckn" => Text.Digits(real),
        "iban" => real.Replace(" ", "").ToUpperInvariant(),
        "email" => real.ToLowerInvariant(),
        _ => Text.Norm(real)
    };

    private static string PhoneKey(string s)
    {
        var d = Text.Digits(s);
        if (d.StartsWith("90") && d.Length == 12) d = d[2..];
        else if (d.StartsWith("0") && d.Length == 11) d = d[1..];
        return d;
    }

    /// <summary>Returns the replacement for <paramref name="real"/> in <paramref name="domain"/>, creating it on first sight.</summary>
    private string Fake(string domain, string real, string? header = null)
    {
        if (!_maps.TryGetValue(domain, out var map)) _maps[domain] = map = new();
        var key = KeyOf(domain, real);
        if (key.Length == 0) return real;
        if (!map.TryGetValue(key, out var entry))
        {
            string fake;
            var guard = 0;
            do
            {
                fake = NewFake(domain, real, header);
            } while ((_usedFakes.Contains(Text.Norm(fake)) || _reals.Contains(Text.Norm(fake)) || KeyOf(domain, fake) == key) && ++guard < 50);
            _usedFakes.Add(Text.Norm(fake));
            map[key] = entry = (real, fake);
        }

        // Shape-bound kinds are returned verbatim; word kinds follow the original's letter case.
        return domain is "phone" or "tckn" or "iban" or "email" || domain.StartsWith("id:")
            ? entry.Fake
            : Text.ApplyStyle(entry.Fake, real);
    }

    private string NewFake(string domain, string real, string? header)
    {
        switch (domain)
        {
            case "first":
            {
                var n = Text.Norm(real);
                var en = Pools.EnglishKeys.Contains(n);
                var female = Pools.FemaleKeys.Contains(n) || (!Pools.FirstNameKeys.Contains(n) && Rng.Next(2) == 0);
                var pool = en ? (female ? "fen" : "men") : (female ? "ftr" : "mtr");
                return Next(pool, () => Generators.Word(2));
            }
            case "last":
                return Next(Pools.EnglishKeys.Contains(Text.Norm(real)) ? "sen" : "str", () => Generators.Word(3));
            case "city":
                return Next("city", () => Generators.Word(3));
            case "country":
                return Next(Pools.CountryEnKeys.Contains(Text.Norm(real)) && !Pools.CountryTrKeys.Contains(Text.Norm(real)) ? "cen" : "ctr", () => Generators.Word(3) + "ya");
            case "address":
            {
                var street = PickClean(Pools.Streets);
                var type = Rng.Pick(new[] { "Sk.", "Cd.", "Sokak", "Caddesi" });
                var town = PickClean(Pools.TownsTr);
                return $"{street} {type} No:{Rng.Next(1, 180)}/{Rng.Next(1, 25)} {town}";
            }
            case "tckn": return Generators.Tckn();
            case "phone": return Generators.Phone(real);
            case "iban": return Generators.TrIban(real);
            case "email": return FakeEmail(real);
            default:
                if (domain.StartsWith("cat:"))
                {
                    _categoryCounters.TryGetValue(domain, out var i);
                    _categoryCounters[domain] = i + 1;
                    var label = string.IsNullOrWhiteSpace(header) ? "Group" : Text.TitleCase(header!.Length > 24 ? header[..24] : header);
                    return $"{label} {Generators.Letters(i)}";
                }
                return Generators.SameShape(real);   // id:*
        }
    }

    private string FakeEmail(string real)
    {
        var at = real.IndexOf('@');
        var domain = at >= 0 ? real[(at + 1)..].ToLowerInvariant() : "";
        string fakeDomain;
        if (Pools.PublicMailDomains.Contains(domain)) fakeDomain = domain;
        else
        {
            if (!_maps.TryGetValue("maildomain", out var dm)) _maps["maildomain"] = dm = new();
            if (!dm.TryGetValue(domain, out var e))
            {
                var tld = domain.EndsWith(".tr") ? "com.tr" : "com";
                dm[domain] = e = (domain, $"ornek{dm.Count + 1}.{tld}");
            }
            fakeDomain = e.Fake;
        }
        var first = Text.Ascii(PickClean(Rng.Next(2) == 0 ? Pools.MaleTr : Pools.FemaleTr));
        var last = Text.Ascii(PickClean(Pools.SurnameTr));
        var local = Rng.Next(3) switch
        {
            0 => $"{first}.{last}",
            1 => $"{first[0]}{last}",
            _ => $"{first}{last}{Rng.Next(1, 99)}"
        };
        return $"{local}@{fakeDomain}";
    }

    /// <summary>Random word from <paramref name="list"/> that is not a real value in this workbook.</summary>
    private string PickClean(IReadOnlyList<string> list)
    {
        for (var i = 0; i < 40; i++)
        {
            var c = Rng.Pick(list);
            if (!_reals.Contains(Text.Norm(c))) return c;
        }
        return Generators.Word(3);
    }

    private string Next(string pool, Func<string> fallback)
    {
        if (!_pools.TryGetValue(pool, out var q))
        {
            var src = pool switch
            {
                "mtr" => Pools.MaleTr, "ftr" => Pools.FemaleTr, "men" => Pools.MaleEn, "fen" => Pools.FemaleEn,
                "str" => Pools.SurnameTr, "sen" => Pools.SurnameEn,
                "city" => Pools.CitiesTr.Concat(Pools.TownsTr).ToArray(),
                "ctr" => Pools.CountriesTr, "cen" => Pools.CountriesEn,
                _ => Array.Empty<string>()
            };
            _pools[pool] = q = new Queue<string>(Rng.Shuffled(src));
        }
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            var n = Text.Norm(c);
            if (!_reals.Contains(n) && !_usedFakes.Contains(n)) return c;
        }
        return fallback();
    }

    // ------------------------------------------------------------- free text

    private void BuildKnownRegex()
    {
        _knownDomain = new();
        var alts = new HashSet<string>();
        foreach (var (domain, map) in _maps)
        {
            if (domain is "phone" or "tckn" or "iban" or "email" or "maildomain" || domain.StartsWith("id:")) continue;
            foreach (var (key, e) in map)
            {
                if (e.Real.Trim().Length < 3) continue;
                _knownDomain.TryAdd(key, domain);
                var r = e.Real.Trim();
                alts.Add(r);
                alts.Add(r.ToUpper(Text.Tr));
                alts.Add(r.ToLower(Text.Tr));
                alts.Add(Text.TitleCase(r));
            }
        }
        if (alts.Count == 0) return;
        var pattern = string.Join("|", alts.OrderByDescending(a => a.Length).Select(Regex.Escape));
        _knownRx = new Regex($@"(?<![\p{{L}}\p{{N}}])(?:{pattern})(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    // Two or three capitalised words in a row: "Kemal Öztürk", "Ayşe Nur Kaya".
    private static readonly Regex NamePair = new(@"(?<![\p{L}\p{N}])\p{Lu}\p{Ll}+(?:[ ]+\p{Lu}\p{Ll}+){1,2}(?![\p{L}\p{N}])", RegexOptions.Compiled);

    /// <summary>
    /// Splits a capitalised word run into name parts when it looks like a person: the first word is a known
    /// first name and the last word is a known surname. Returns null otherwise.
    /// </summary>
    private List<(string Word, string Domain)>? NameParts(string run)
    {
        var words = run.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool IsFirst(string w) => Pools.FirstNameKeys.Contains(Text.Norm(w)) || Known("first", w);
        bool IsLast(string w) => Pools.SurnameKeys.Contains(Text.Norm(w)) || Known("last", w);
        if (!IsFirst(words[0]) || !IsLast(words[^1])) return null;
        var parts = new List<(string, string)>();
        for (var i = 0; i < words.Length; i++) parts.Add((words[i], i == words.Length - 1 ? "last" : "first"));
        return parts;
    }

    private bool Known(string domain, string word) =>
        _maps.TryGetValue(domain, out var m) && m.ContainsKey(Text.Norm(word));

    private string ReplaceInText(string s, bool shiftDates)
    {
        var count = 0;
        s = NamePair.Replace(s, m =>
        {
            if (NameParts(m.Value) is not { } parts) return m.Value;
            count++;
            return string.Join(' ', parts.Select(p => Fake(p.Domain, p.Word)));
        });
        s = Patterns.Iban.Replace(s, m => { if (!Generators.IsTrIban(m.Value)) return m.Value; count++; return Fake("iban", m.Value); });
        s = Patterns.Email.Replace(s, m => { count++; return Fake("email", m.Value); });
        s = Patterns.Tckn.Replace(s, m => { if (!Generators.IsValidTckn(m.Value)) return m.Value; count++; return Fake("tckn", m.Value); });
        s = Patterns.Phone.Replace(s, m => { if (!Patterns.IsPhone(m.Value)) return m.Value; count++; return Fake("phone", m.Value); });
        if (_knownRx != null)
        {
            s = _knownRx.Replace(s, m =>
            {
                if (_knownDomain == null || !_knownDomain.TryGetValue(Text.Norm(m.Value), out var domain)) return m.Value;
                // A lower-case "deniz" or "can" in a sentence is a word, not a name.
                if (domain is "first" or "last" && !char.IsUpper(m.Value[0])) return m.Value;
                count++;
                return Fake(domain, m.Value);
            });
        }
        if (shiftDates)
        {
            s = DateText.InText.Replace(s, m =>
            {
                if (!DateText.TryParse(m.Value, out var d, out var fmt)) return m.Value;
                count++;
                return d.AddDays(_dateShift).ToString(fmt, CultureInfo.InvariantCulture);
            });
        }
        _textReplacements += count;
        return s;
    }

    // ------------------------------------------------------------- helpers

    private (IXLWorksheet?, SheetPlan?) Sheet(XLWorkbook wb, ColumnPlan col)
    {
        var sp = _plan.Sheets.FirstOrDefault(s => s.Name == col.Sheet);
        return wb.TryGetWorksheet(col.Sheet, out var ws) ? (ws, sp) : (null, null);
    }

    private IEnumerable<IXLCell> DataCells(IXLWorksheet ws, SheetPlan sp, ColumnPlan col)
    {
        var last = _rowLimit > 0 ? Math.Min(sp.LastRow, sp.FirstDataRow + _rowLimit - 1) : sp.LastRow;
        for (var r = sp.FirstDataRow; r <= last; r++)
        {
            var cell = ws.Cell(r, col.Column);
            if (cell.HasFormula) continue;
            yield return cell;
        }
    }

    private static string? AsString(XLCellValue v) => v.Type switch
    {
        XLDataType.Text => v.GetText().Trim().Length == 0 ? null : v.GetText(),
        XLDataType.Number => v.GetNumber().ToString(v.GetNumber() == Math.Floor(v.GetNumber()) ? "0" : "R", CultureInfo.InvariantCulture),
        _ => null
    };

    private bool HyperlinkLeaks(IXLCell cell)
    {
        try
        {
            var target = cell.GetHyperlink().ExternalAddress?.ToString() ?? "";
            return target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || target.StartsWith("tel:", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void DropHyperlink(IXLWorksheet ws, IXLCell cell)
    {
        if (!cell.HasHyperlink) return;
        try { ws.Hyperlinks.Delete(cell.Address); } catch { /* best effort */ }
    }

    private static void ClearProperties(XLWorkbook wb)
    {
        var p = wb.Properties;
        p.Author = p.Title = p.Subject = p.Category = p.Keywords = p.Comments = p.Status = "";
        p.LastModifiedBy = p.Company = p.Manager = "";
        foreach (var cp in wb.CustomProperties.ToList()) wb.CustomProperties.Delete(cp.Name);
    }

    private MaskKey BuildKey()
    {
        var key = new MaskKey
        {
            SourceFile = Path.GetFileName(_plan.SourcePath),
            SharedFactor = _sharedFactor,
            DateShiftDays = _dateShift,
            NoisePercent = _o.NoisePercent
        };
        foreach (var c in _plan.AllColumns.Where(c => c.Kind != ColumnKind.Keep))
            key.Columns.Add(new KeyColumn
            {
                Sheet = c.Sheet, Column = c.Column, Header = c.Header, Kind = c.Kind,
                Factor = _factors.TryGetValue((c.Sheet, c.Column), out var f) ? f : 0
            });
        foreach (var (domain, map) in _maps)
        {
            var d = new Dictionary<string, string>();
            foreach (var e in map.Values) d.TryAdd(e.Real, e.Fake);
            key.Maps[domain] = d;
        }
        return key;
    }

    private IEnumerable<(string What, string Value)> SensitiveValues()
    {
        // A column header that happens to equal a value ("Müdür" header vs "Müdür" title) is not a leak.
        var headers = new HashSet<string>(_plan.AllColumns.Select(c => Text.Norm(c.Header)));
        foreach (var (domain, map) in _maps)
        {
            if (domain == "maildomain") continue;
            var what = domain.Contains(':') ? domain[..domain.IndexOf(':')] : domain;
            foreach (var e in map.Values)
                if (e.Real.Trim().Length >= 3 && !headers.Contains(Text.Norm(e.Real))) yield return (what, e.Real.Trim());
        }
    }
}
