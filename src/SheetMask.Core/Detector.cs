using System.Globalization;
using ClosedXML.Excel;

namespace SheetMask.Core;

/// <summary>Reads a workbook and proposes a masking kind for every column (header words + content patterns).</summary>
public static class Detector
{
    private const int SampleRows = 1500;

    public static WorkbookPlan Analyze(string path, IReadOnlyDictionary<string, int>? headerRows = null) =>
        Analyze(path, headerRows, 0, out _);

    /// <summary>
    /// Analyses the workbook and, when <paramref name="sampleRows"/> &gt; 0, also returns a small copy of it
    /// (everything above the data plus the first data rows) so previews never reopen a large file.
    /// </summary>
    public static WorkbookPlan Analyze(string path, IReadOnlyDictionary<string, int>? headerRows, int sampleRows, out byte[]? sample)
    {
        using var wb = Book.Open(path);
        var plan = new WorkbookPlan { SourcePath = path };
        foreach (var ws in wb.Worksheets)
        {
            int? forced = headerRows != null && headerRows.TryGetValue(ws.Name, out var hr) ? hr : null;
            var sp = AnalyzeSheet(ws, forced);
            if (sp != null) plan.Sheets.Add(sp);
        }
        sample = sampleRows > 0 ? BuildSample(wb, plan, sampleRows) : null;
        return plan;
    }

    private static byte[] BuildSample(XLWorkbook src, WorkbookPlan plan, int rows)
    {
        using var dst = new XLWorkbook();
        foreach (var sp in plan.Sheets)
        {
            var from = src.Worksheet(sp.Name);
            var to = dst.AddWorksheet(sp.Name);
            var lastRow = Math.Min(sp.LastRow, sp.FirstDataRow + rows - 1);
            var lastCol = from.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (var r = 1; r <= lastRow; r++)
                for (var c = 1; c <= lastCol; c++)
                {
                    var a = from.Cell(r, c);
                    if (a.IsEmpty() && !a.HasFormula) continue;
                    var b = to.Cell(r, c);
                    if (a.HasFormula) b.FormulaA1 = a.FormulaA1; else b.Value = a.Value;
                    b.Style.NumberFormat.Format = a.Style.NumberFormat.Format;
                    if (a.Style.NumberFormat.NumberFormatId > 0 && string.IsNullOrEmpty(a.Style.NumberFormat.Format))
                        b.Style.NumberFormat.NumberFormatId = a.Style.NumberFormat.NumberFormatId;
                }
        }
        using var ms = new MemoryStream();
        dst.SaveAs(ms);
        return ms.ToArray();
    }

    private static SheetPlan? AnalyzeSheet(IXLWorksheet ws, int? forcedHeader)
    {
        var firstRow = ws.FirstRowUsed()?.RowNumber() ?? 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (firstRow == 0 || lastCol == 0) return null;

        var header = forcedHeader ?? GuessHeaderRow(ws, firstRow, lastRow, lastCol);
        var sp = new SheetPlan
        {
            Name = ws.Name,
            HeaderRow = header,
            FirstDataRow = header > 0 ? header + 1 : firstRow,
            LastRow = lastRow
        };

        for (var c = 1; c <= lastCol; c++)
        {
            var col = AnalyzeColumn(ws, sp, c);
            if (col != null) sp.Columns.Add(col);
        }
        return sp;
    }

    /// <summary>Among the first ten used rows, the one with the most text cells (a title row above it has only one).</summary>
    private static int GuessHeaderRow(IXLWorksheet ws, int firstRow, int lastRow, int lastCol)
    {
        int best = 0, bestCount = 0;
        for (var r = firstRow; r <= Math.Min(lastRow - 1, firstRow + 9); r++)
        {
            var count = 0;
            for (var c = 1; c <= lastCol; c++)
            {
                var cell = ws.Cell(r, c);
                if (!cell.HasFormula && cell.Value.Type == XLDataType.Text && cell.Value.GetText().Trim().Length > 0) count++;
            }
            if (count > bestCount) { best = r; bestCount = count; }
        }
        if (bestCount == 0) return 0;
        // A header row needs data below it that is not all text, or at least two header cells.
        return bestCount >= 2 || lastCol == 1 ? best : 0;
    }

    private static ColumnPlan? AnalyzeColumn(IXLWorksheet ws, SheetPlan sp, int c)
    {
        var col = new ColumnPlan
        {
            Sheet = ws.Name,
            Column = c,
            Letter = XLHelper.GetColumnLetterFromNumber(c),
            Header = sp.HeaderRow > 0 ? ws.Cell(sp.HeaderRow, c).GetFormattedString().Trim() : ""
        };

        var texts = new List<string>();
        var numbers = new List<double>();
        var dates = 0;
        var end = Math.Min(sp.LastRow, sp.FirstDataRow + SampleRows - 1);
        for (var r = sp.FirstDataRow; r <= end; r++)
        {
            var cell = ws.Cell(r, c);
            if (cell.HasFormula) { col.Formulas++; continue; }
            var v = cell.Value;
            switch (v.Type)
            {
                case XLDataType.Blank: continue;
                case XLDataType.Number: numbers.Add(v.GetNumber()); break;
                case XLDataType.DateTime: dates++; break;
                case XLDataType.Text:
                    var t = v.GetText().Trim();
                    if (t.Length == 0) continue;
                    texts.Add(t);
                    break;
                default: break;
            }
            col.NonEmpty++;
            if (col.Samples.Count < 4)
            {
                var s = cell.GetFormattedString().Trim();
                if (s.Length > 0 && !col.Samples.Contains(s)) col.Samples.Add(s.Length > 60 ? s[..60] + "…" : s);
            }
        }

        if (col.NonEmpty == 0 && col.Formulas == 0 && col.Header.Length == 0) return null;

        var total = Math.Max(1, col.NonEmpty);
        col.ValueType = col.NonEmpty == 0 ? (col.Formulas > 0 ? "formula" : "empty")
            : numbers.Count == col.NonEmpty ? "number"
            : dates == col.NonEmpty ? "date"
            : texts.Count == col.NonEmpty ? "text"
            : "mixed";

        var (kind, reason) = Decide(col.Header, texts, numbers, dates, total);
        col.Kind = col.Detected = kind;
        col.Reason = reason;
        if (col.NonEmpty == 0 && col.Formulas > 0) col.Reason = "formula column – recalculated after masking";
        return col;
    }

    // ------------------------------------------------------------------ rules

    private static (ColumnKind, string) Decide(string header, List<string> texts, List<double> numbers, int dates, int total)
    {
        var tokens = Text.Tokens(header);
        var joined = string.Concat(tokens);

        // All values as strings (whole numbers without decimals) for pattern checks.
        var asStrings = texts.Concat(numbers.Where(n => n == Math.Floor(n) && Math.Abs(n) < 1e15)
            .Select(n => n.ToString("0", CultureInfo.InvariantCulture))).ToList();
        double Ratio(Func<string, bool> f) => asStrings.Count == 0 ? 0 : (double)asStrings.Count(f) / total;

        // 1) unmistakable content
        if (Ratio(Generators.IsValidTckn) >= 0.7) return (ColumnKind.NationalId, "valid TCKN check digits");
        if (Ratio(Generators.IsTrIban) >= 0.7) return (ColumnKind.Iban, "TR IBAN pattern");
        if (Ratio(s => Patterns.EmailExact.IsMatch(s)) >= 0.7) return (ColumnKind.Email, "e-mail pattern");

        var numericText = texts.Count(t => NumberShape.TryParse(t, out _, out _));
        var numericShare = (double)(numbers.Count + numericText) / total;
        var dateText = texts.Count(t => DateText.TryParse(t, out _, out _));
        var dateShare = (double)(dates + dateText) / total;

        // 2) header words
        bool Has(params string[] keys) => tokens.Any(t => keys.Any(k => t == k || (k.Length >= 4 && t.StartsWith(k))))
                                          || keys.Any(k => k.Length >= 6 && joined.Contains(k));

        if (Has("TCKN", "KIMLIK", "TCNO", "TCKIMLIK") || (tokens.Contains("TC") ) || ContainsPair(tokens, "T", "C"))
            return (ColumnKind.NationalId, "header: national ID");
        if (Has("IBAN")) return (ColumnKind.Iban, "header: IBAN");
        if (Has("MAIL", "EPOSTA", "EMAIL")) return (ColumnKind.Email, "header: e-mail");
        if (Has("TEL", "TELEFON", "GSM", "CEP", "PHONE", "MOBILE", "FAKS", "FAX") || Ratio(Patterns.IsPhone) >= 0.7)
            return numericShare > 0 || texts.Count > 0
                ? (ColumnKind.Phone, Ratio(Patterns.IsPhone) >= 0.7 ? "phone number pattern" : "header: phone")
                : (ColumnKind.Phone, "header: phone");

        var hasSurname = Has("SOYAD", "SOYADI", "SOYISIM", "SURNAME", "LASTNAME") || ContainsPair(tokens, "LAST", "NAME");
        var hasName = tokens.Any(t => t is "AD" or "ADI" or "ISIM" or "ISMI" or "NAME" or "FIRSTNAME" or "ADSOYAD" or "ADISOYADI")
                      || Has("ADSOYAD", "ADISOYADI", "ISIMSOYISIM", "FULLNAME", "PERSONEL", "CALISAN", "EMPLOYEE", "MUSTERI", "CUSTOMER", "HASTA", "OGRENCI");
        var looksIdentifier = Has("SICIL", "NUMARA", "PLAKA", "POLICE", "HESAPNO", "MUSTERINO", "SERI")
                              || tokens.Any(t => t is "NO" or "ID" or "KOD" or "KODU" or "CODE" or "NUMBER");

        if (looksIdentifier && !tokens.Contains("SIRA") && (numericShare > 0.5 || texts.Count > 0))
            return (ColumnKind.Identifier, "header: number / code");

        var avgTokens = texts.Count == 0 ? 0 : texts.Average(t => t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        if (hasSurname && hasName) return (ColumnKind.FullName, "header: name + surname");
        if (hasSurname) return (ColumnKind.LastName, "header: surname");
        if (hasName && texts.Count > 0) return avgTokens >= 1.6 ? (ColumnKind.FullName, "header: name (several words)") : (ColumnKind.FirstName, "header: name");

        if (Has("ADRES", "ADDRESS")) return (ColumnKind.Address, "header: address");
        if (Has("ULKE", "COUNTRY", "UYRUK", "NATIONALITY")) return (ColumnKind.Country, "header: country");
        if (tokens.Any(t => t is "IL" or "ILI" or "SEHIR" or "SEHRI" or "CITY") || Has("DOGUMYERI", "MEMLEKET"))
            return (ColumnKind.City, "header: city");
        if (Has("ILCE", "DISTRICT", "SEMT", "MAHALLE", "DEPARTMAN", "BIRIM", "BOLUM", "SUBE", "FIRMA", "SIRKET", "KURUM",
                "UNVAN", "GOREV", "POZISYON", "DEPARTMENT", "COMPANY", "TITLE", "TEAM", "EKIP", "PROJE", "MUDURLUK", "BRANCH"))
            return texts.Count > 0 ? (ColumnKind.Category, "header: group / organisation") : (ColumnKind.Keep, "header: group, but numeric");

        if (Has("ACIKLAMA", "NOTLAR", "YORUM", "NOTES", "DESCRIPTION", "COMMENT", "MESAJ", "MESSAGE") || tokens.Contains("NOT") || tokens.Contains("NOTE"))
            return (ColumnKind.FreeText, "header: free text");

        if (numericShare >= 0.7 && Has("MAAS", "UCRET", "BRUT", "TUTAR", "PRIM", "IKRAMIYE", "SALARY", "WAGE", "AMOUNT",
                "BONUS", "GELIR", "CIRO", "BAKIYE", "FIYAT", "PRICE", "COST", "MALIYET", "BORC", "ALACAK", "LIMIT",
                "ODEME", "TOPLAM", "KAZANC", "GIDER", "HARCAMA", "INCOME", "REVENUE", "BALANCE", "TOTAL", "KESINTI", "AVANS",
                "NET", "TL", "USD", "EUR", "TRY"))
            return (ColumnKind.Scale, "header: money / amount");

        if (dateShare >= 0.7) return (ColumnKind.DateShift, "date values");

        // 3) dictionaries
        if (texts.Count > 0)
        {
            double TR(Func<string, bool> f) => (double)texts.Count(f) / total;
            var cityR = TR(t => Pools.CityKeys.Contains(Text.Norm(t)));
            var countryR = TR(t => Pools.CountryKeys.Contains(Text.Norm(t)));
            var fullR = TR(t =>
            {
                var p = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return p.Length is >= 2 and <= 4 && (Pools.FirstNameKeys.Contains(Text.Norm(p[0])) || Pools.SurnameKeys.Contains(Text.Norm(p[^1])));
            });
            var firstR = TR(t => !t.Contains(' ') && Pools.FirstNameKeys.Contains(Text.Norm(t)));
            var lastR = TR(t => !t.Contains(' ') && Pools.SurnameKeys.Contains(Text.Norm(t)));

            if (cityR >= 0.6) return (ColumnKind.City, "known city names");
            if (countryR >= 0.6) return (ColumnKind.Country, "known country names");
            if (fullR >= 0.5 && avgTokens >= 1.6) return (ColumnKind.FullName, "known first/last names");
            if (lastR >= 0.5 && lastR > firstR) return (ColumnKind.LastName, "known surnames");
            if (firstR >= 0.5) return (ColumnKind.FirstName, "known first names");

            var avgLen = texts.Average(t => t.Length);
            if (avgLen > 40 || texts.Any(t => t.Length > 100)) return (ColumnKind.FreeText, "long text");
            if (texts.Any(t => Patterns.Tckn.IsMatch(t) || Patterns.Email.IsMatch(t) || Patterns.Iban.IsMatch(t)))
                return (ColumnKind.FreeText, "contains ID / e-mail patterns");
        }

        if (numericShare >= 0.7) return (ColumnKind.Keep, "number – choose Scale if it is an amount");
        return (ColumnKind.Keep, texts.Count > 0 ? "text – not recognised" : "no data");
    }

    private static bool ContainsPair(string[] tokens, string a, string b)
    {
        for (var i = 0; i + 1 < tokens.Length; i++)
            if (tokens[i] == a && tokens[i + 1] == b) return true;
        return false;
    }
}
