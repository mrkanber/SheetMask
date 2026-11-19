using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SheetMask.Core;

/// <summary>Numbers stored as text, e.g. "45.000,00 TL" or "$1,250.5".</summary>
public sealed record NumberShape(string Prefix, string Suffix, char? Thousands, char? Decimal, int Decimals, bool Negative)
{
    private static readonly Regex Rx = new(@"^(?<pre>[^\d\-]*?)(?<neg>-)?(?<num>\d[\d.,' ]*\d|\d)(?<suf>\D*)$", RegexOptions.Compiled);

    public static bool TryParse(string s, out double value, out NumberShape shape)
    {
        value = 0;
        shape = null!;
        var m = Rx.Match(s.Trim());
        if (!m.Success) return false;
        var pre = m.Groups["pre"].Value;
        var suf = m.Groups["suf"].Value;
        if (pre.Any(char.IsLetterOrDigit) && pre.Trim().Length > 4) return false;
        if (suf.Trim().Length > 5) return false;

        var num = m.Groups["num"].Value;
        char? dec = null, th = null;
        var lastDot = num.LastIndexOf('.');
        var lastComma = num.LastIndexOf(',');
        if (lastDot >= 0 && lastComma >= 0)
        {
            dec = lastDot > lastComma ? '.' : ',';
            th = dec == '.' ? ',' : '.';
        }
        else if (lastDot >= 0 || lastComma >= 0)
        {
            var sep = lastDot >= 0 ? '.' : ',';
            var count = num.Count(c => c == sep);
            var after = num.Length - num.LastIndexOf(sep) - 1;
            if (count > 1 || after == 3) th = sep; else dec = sep;
        }
        if (num.Contains(' ') || num.Contains('\'')) th ??= num.Contains(' ') ? ' ' : '\'';

        var clean = new StringBuilder();
        foreach (var ch in num)
        {
            if (char.IsDigit(ch)) clean.Append(ch);
            else if (dec != null && ch == dec) clean.Append('.');
        }
        if (!double.TryParse(clean.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
        var decimals = dec == null ? 0 : num.Length - num.LastIndexOf(dec.Value) - 1;
        var neg = m.Groups["neg"].Success;
        if (neg) value = -value;
        shape = new NumberShape(pre, suf, th, dec, decimals, neg);
        return true;
    }

    public string Format(double v)
    {
        var abs = Math.Abs(Math.Round(v, Decimals));
        var s = abs.ToString("F" + Decimals, CultureInfo.InvariantCulture);
        var parts = s.Split('.');
        var intPart = parts[0];
        if (Thousands != null)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < intPart.Length; i++)
            {
                if (i > 0 && (intPart.Length - i) % 3 == 0) sb.Append(Thousands);
                sb.Append(intPart[i]);
            }
            intPart = sb.ToString();
        }
        var body = parts.Length > 1 ? intPart + Decimal + parts[1] : intPart;
        return Prefix + (v < 0 ? "-" : "") + body + Suffix;
    }
}

/// <summary>Dates stored as text, e.g. "15.03.2024".</summary>
public static class DateText
{
    private static readonly string[] Formats =
    {
        "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yyyy HH:mm", "dd.MM.yyyy HH:mm:ss",
        "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy",
        "yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss",
        "yyyy.MM.dd"
    };

    public static bool TryParse(string s, out DateTime value, out string format)
    {
        s = s.Trim();
        foreach (var f in Formats)
        {
            if (DateTime.TryParseExact(s, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            {
                format = f;
                return true;
            }
        }
        value = default;
        format = "";
        return false;
    }

    public static readonly Regex InText = new(
        @"\b(\d{1,2}[./-]\d{1,2}[./-]\d{4}|\d{4}-\d{2}-\d{2})\b", RegexOptions.Compiled);
}

public static class Patterns
{
    public static readonly Regex Email = new(@"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled);
    public static readonly Regex EmailExact = new(@"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}$", RegexOptions.Compiled);

    public static readonly Regex Tckn = new(@"(?<!\d)[1-9]\d{10}(?!\d)", RegexOptions.Compiled);

    public static readonly Regex Iban = new(@"\bTR\d{2}(?:[ ]?\d{4}){5}[ ]?\d{2}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 0532 123 45 67, +90 (532) 123-4567, 5321234567, 0312 444 55 66
    public static readonly Regex Phone = new(
        @"(?<![\d+])(?:\+?90[\s\-]?)?\(?0?[2-5]\d{2}\)?[\s\-]?\d{3}[\s\-]?\d{2}[\s\-]?\d{2}(?!\d)", RegexOptions.Compiled);

    public static bool IsPhone(string s)
    {
        s = s.Trim();
        if (s.Any(c => !(char.IsDigit(c) || " ()-+.".Contains(c)))) return false;
        var d = Text.Digits(s);
        if (d.StartsWith("90") && d.Length == 12) d = d[2..];
        else if (d.StartsWith("0") && d.Length == 11) d = d[1..];
        return d.Length == 10 && d[0] is >= '2' and <= '5';
    }
}
