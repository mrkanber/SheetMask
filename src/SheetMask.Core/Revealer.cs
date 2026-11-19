using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace SheetMask.Core;

/// <summary>Turns masked values back into real ones using a decrypted <see cref="MaskKey"/>.</summary>
public sealed class Revealer
{
    private readonly MaskKey _key;
    private readonly Dictionary<string, (string Real, string Domain)> _byFake = new();   // Norm(fake) → real
    private readonly Dictionary<string, (string Real, string Domain)> _byKey = new();   // token key of fake → real

    private static readonly Regex NumberRx = new(@"(?<![\p{L}\p{N}.,])-?\d[\d.,]*\d(?![\p{L}\p{N}])|(?<![\p{L}\p{N}.,])\d(?![\p{L}\p{N}])", RegexOptions.Compiled);

    public Revealer(MaskKey key)
    {
        _key = key;
        foreach (var (domain, map) in key.Maps)
        {
            foreach (var (real, fake) in map)
            {
                if (fake.Length < 2) continue;
                _byFake.TryAdd(Text.Norm(fake), (real, domain));
                _byKey.TryAdd(LeakScanner.Key(fake), (real, domain));
            }
        }
    }

    public MaskKey Key => _key;

    /// <summary>Replaces every known replacement value in <paramref name="text"/> with the real value.</summary>
    /// <param name="divideNumbers">Divide numbers in the text by the shared factor (only meaningful when every amount column used it).</param>
    /// <param name="shiftDates">Move dates back by the secret number of days.</param>
    public string RevealText(string text, bool divideNumbers, bool shiftDates, out int count)
    {
        var n = 0;
        // Restored values and dates are parked behind placeholders so the number pass cannot touch their digits.
        var parked = new List<string>();
        // Private-use marks around a letter index: no digits, so the number pass cannot see inside.
        string Park(string v) { parked.Add(v); return "\uE000" + Generators.Letters(parked.Count - 1) + "\uE001"; }

        text = ReplaceKnown(text, Park, ref n);
        text = DateText.InText.Replace(text, m =>
        {
            if (!shiftDates || _key.DateShiftDays == 0 || !DateText.TryParse(m.Value, out var d, out var fmt)) return Park(m.Value);
            n++;
            return Park(d.AddDays(-_key.DateShiftDays).ToString(fmt, CultureInfo.InvariantCulture));
        });
        if (divideNumbers && _key.SharedFactor > 0)
        {
            text = NumberRx.Replace(text, m =>
            {
                var s = m.Value;
                if (s.StartsWith('0') && s.Length > 1 && char.IsDigit(s[1])) return s;   // codes, phone parts
                if (!NumberShape.TryParse(s, out var v, out var shape) || Math.Abs(v) < 100) return s;
                if (shape.Thousands == null && shape.Decimal == null && v is >= 1900 and <= 2100) return s;   // years
                n++;
                return shape.Format(RevealNumber(v));
            });
        }
        text = Regex.Replace(text, "\uE000([A-Z]+)\uE001", m => parked[LetterIndex(m.Groups[1].Value)]);
        count = n;
        return text;
    }

    /// <summary>
    /// Linear-time lookup: the text is cut into tokens and runs of up to eight adjacent tokens are looked up
    /// in a dictionary (longest run wins). "0537-271-31-93" and "0537 271 31 93" share a key.
    /// </summary>
    private string ReplaceKnown(string text, Func<string, string> park, ref int count)
    {
        var ms = LeakScanner.TokenRx.Matches(text);
        if (ms.Count == 0 || _byKey.Count == 0) return text;
        var norms = ms.Select(m => Text.Norm(m.Value)).ToArray();
        var sb = new StringBuilder(text.Length);
        var key = new StringBuilder();
        var pos = 0;
        var i = 0;
        while (i < ms.Count)
        {
            var best = -1;
            (string Real, string Domain) hit = default;
            key.Clear();
            for (var k = 0; k < 8 && i + k < ms.Count; k++)
            {
                if (k > 0)
                {
                    var gapStart = ms[i + k - 1].Index + ms[i + k - 1].Length;
                    var gap = text.AsSpan(gapStart, ms[i + k].Index - gapStart);
                    if (gap.Length > 3 || gap.IndexOfAny('\n', '\r') >= 0) break;
                    key.Append(' ');
                }
                key.Append(norms[i + k]);
                if (_byKey.TryGetValue(key.ToString(), out var e)) { best = k; hit = e; }
            }
            // "can" in a sentence is a word, "Can" a name.
            if (best < 0 || (hit.Domain is "first" or "last" && !char.IsUpper(ms[i].Value[0]))) { i++; continue; }

            var start = ms[i].Index;
            var end = ms[i + best].Index + ms[i + best].Length;
            var original = text[start..end];
            sb.Append(text, pos, start - pos);
            sb.Append(park(hit.Domain is "phone" or "tckn" or "iban" or "email" or "maildomain" || hit.Domain.StartsWith("id:")
                ? hit.Real
                : Text.ApplyStyle(hit.Real, original)));
            count++;
            pos = end;
            i += best + 1;
        }
        sb.Append(text, pos, text.Length - pos);
        return sb.ToString();
    }

    private static int LetterIndex(string s)
    {
        var i = 0;
        foreach (var ch in s) i = i * 26 + (ch - 'A' + 1);
        return i - 1;
    }

    public double RevealNumber(double masked, double? factor = null)
    {
        var f = factor ?? _key.SharedFactor;
        return masked == Math.Floor(masked) ? Unscale(masked, f) : Math.Round(masked / f, 2);
    }

    /// <summary>
    /// Masking rounded 45 000 × f to a step (e.g. 100). The real value lies within ±step/2 of masked/f;
    /// the roundest number in that window is almost always the original.
    /// </summary>
    public static double Unscale(double masked, double f)
    {
        var step = 1.0;
        while (step < 1e6 && masked != 0 && Math.Abs(masked) % (step * 10) == 0) step *= 10;
        var lo = (masked - step / 2) / f;
        var hi = (masked + step / 2) / f;
        if (lo > hi) (lo, hi) = (hi, lo);
        for (var g = 1e6; g >= 1; g /= 10)
        {
            var c = Math.Round((lo + hi) / 2 / g) * g;
            if (c >= lo && c <= hi) return c;
        }
        return Math.Round(masked / f);
    }

    /// <summary>Writes a copy of <paramref name="input"/> with real values restored. Returns the number of changed cells.</summary>
    public int RevealWorkbook(string input, string output, List<string> notes)
    {
        var changed = 0;
        using var wb = Book.Open(input);
        var handled = new HashSet<(string, int, int)>();

        foreach (var ws in wb.Worksheets)
        {
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            var cols = _key.Columns.Where(c => c.Sheet == ws.Name || !_key.Columns.Any(k => k.Sheet == ws.Name)).ToList();

            foreach (var kc in cols.Where(c => c.Kind is ColumnKind.Scale or ColumnKind.DateShift
                                                     or ColumnKind.Identifier or ColumnKind.NationalId or ColumnKind.Phone))
            {
                var (col, headerRow) = FindColumn(ws, kc, lastRow, lastCol);
                if (col == 0) continue;
                for (var r = headerRow + 1; r <= lastRow; r++)
                {
                    var cell = ws.Cell(r, col);
                    if (cell.HasFormula) continue;
                    if (RevealCell(cell, kc)) changed++;
                    handled.Add((ws.Name, r, col));
                }
            }

            foreach (var cell in ws.CellsUsed())
            {
                if (cell.HasFormula || cell.Value.Type != XLDataType.Text) continue;
                if (handled.Contains((ws.Name, cell.Address.RowNumber, cell.Address.ColumnNumber))) continue;
                var before = cell.Value.GetText();
                var after = RevealText(before, false, false, out var n);
                if (n > 0) { cell.Value = after; changed++; }
            }
        }

        if (_key.NoisePercent > 0) notes.Add($"Amounts had ±{_key.NoisePercent}% noise; restored amounts are approximate.");
        if (_key.Columns.Any(c => c.Kind == ColumnKind.RandomNumber)) notes.Add("Random-number columns cannot be restored.");
        try { wb.RecalculateAllFormulas(); } catch { notes.Add("Formulas will be recalculated when the file is opened in Excel."); }
        wb.FullCalculationOnLoad = true;
        wb.SaveAs(output);
        return changed;
    }

    private bool RevealCell(IXLCell cell, KeyColumn kc)
    {
        var v = cell.Value;
        switch (kc.Kind)
        {
            case ColumnKind.Scale:
            {
                var f = kc.Factor > 0 ? kc.Factor : _key.SharedFactor;
                if (v.Type == XLDataType.Number)
                {
                    var m = v.GetNumber();
                    cell.Value = m == Math.Floor(m) ? Unscale(m, f) : Math.Round(m / f, 2);
                    return true;
                }
                if (v.Type == XLDataType.Text && NumberShape.TryParse(v.GetText(), out var t, out var shape))
                {
                    cell.Value = shape.Format(shape.Decimals == 0 ? Unscale(t, f) : t / f);
                    return true;
                }
                return false;
            }
            case ColumnKind.DateShift:
                if (v.Type == XLDataType.DateTime) { cell.Value = v.GetDateTime().AddDays(-_key.DateShiftDays); return true; }
                if (v.Type == XLDataType.Text && DateText.TryParse(v.GetText(), out var d, out var fmt))
                {
                    cell.Value = d.AddDays(-_key.DateShiftDays).ToString(fmt, CultureInfo.InvariantCulture);
                    return true;
                }
                return false;
            default:
            {
                var s = v.Type switch
                {
                    XLDataType.Number => v.GetNumber().ToString("0", CultureInfo.InvariantCulture),
                    XLDataType.Text => v.GetText(),
                    _ => null
                };
                if (s == null || !_byFake.TryGetValue(Text.Norm(s.Trim()), out var e)) return false;
                if (v.Type == XLDataType.Number && double.TryParse(e.Real, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                    cell.Value = num;
                else
                    cell.Value = e.Real;
                return true;
            }
        }
    }

    /// <summary>Finds the column by its header text in the first rows; falls back to the original column number.</summary>
    private static (int Col, int HeaderRow) FindColumn(IXLWorksheet ws, KeyColumn kc, int lastRow, int lastCol)
    {
        var target = Text.Norm(kc.Header);
        if (target.Length > 0)
        {
            for (var r = 1; r <= Math.Min(15, lastRow); r++)
            {
                if (kc.Column <= lastCol && Text.Norm(ws.Cell(r, kc.Column).GetFormattedString()) == target) return (kc.Column, r);
                for (var c = 1; c <= lastCol; c++)
                    if (Text.Norm(ws.Cell(r, c).GetFormattedString()) == target) return (c, r);
            }
            return (0, 0);
        }
        return kc.Column <= lastCol ? (kc.Column, 0) : (0, 0);
    }
}
