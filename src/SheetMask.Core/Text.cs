using System.Globalization;
using System.Text;

namespace SheetMask.Core;

/// <summary>Turkish-safe text helpers used for matching and for restoring letter case.</summary>
public static class Text
{
    public static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>
    /// Matching key: trimmed, single-spaced, Turkish upper-case, diacritics folded (Ş→S, İ→I ...).
    /// "şehir", "ŞEHİR" and "SEHIR" all produce "SEHIR".
    /// </summary>
    public static string Norm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var upper = CollapseSpaces(s).ToUpper(Tr);
        var sb = new StringBuilder(upper.Length);
        foreach (var ch in upper)
        {
            sb.Append(ch switch
            {
                'İ' or 'Î' or 'Ì' or 'Í' => 'I',
                'Ş' => 'S',
                'Ğ' => 'G',
                'Ü' or 'Û' or 'Ù' or 'Ú' => 'U',
                'Ö' or 'Ô' or 'Ò' or 'Ó' => 'O',
                'Ç' => 'C',
                'Â' or 'À' or 'Á' or 'Ä' => 'A',
                'É' or 'È' or 'Ê' or 'Ë' => 'E',
                _ => ch
            });
        }
        return sb.ToString();
    }

    /// <summary>Header tokens: punctuation becomes a separator ("T.C. Kimlik No" → T, C, KIMLIK, NO).</summary>
    public static string[] Tokens(string? s)
    {
        var n = Norm(s);
        var sb = new StringBuilder(n.Length);
        foreach (var ch in n) sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    public static string CollapseSpaces(string s)
    {
        var sb = new StringBuilder(s.Length);
        var space = false;
        foreach (var ch in s.Trim())
        {
            if (char.IsWhiteSpace(ch)) { if (!space) sb.Append(' '); space = true; }
            else { sb.Append(ch); space = false; }
        }
        return sb.ToString();
    }

    public enum CaseStyle { Title, Upper, Lower, Mixed }

    public static CaseStyle StyleOf(string s)
    {
        var letters = s.Where(char.IsLetter).ToArray();
        if (letters.Length == 0) return CaseStyle.Mixed;
        if (letters.All(char.IsUpper)) return letters.Length > 1 ? CaseStyle.Upper : CaseStyle.Title;
        if (letters.All(char.IsLower)) return CaseStyle.Lower;
        return CaseStyle.Title;
    }

    /// <summary>Writes <paramref name="fake"/> in the same case style as <paramref name="original"/>.</summary>
    public static string ApplyStyle(string fake, string original) => StyleOf(original) switch
    {
        CaseStyle.Upper => fake.ToUpper(Tr),
        CaseStyle.Lower => fake.ToLower(Tr),
        _ => fake
    };

    public static string TitleCase(string s)
    {
        var parts = CollapseSpaces(s).ToLower(Tr).Split(' ');
        for (var i = 0; i < parts.Length; i++)
            if (parts[i].Length > 0) parts[i] = char.ToUpper(parts[i][0], Tr) + parts[i][1..];
        return string.Join(' ', parts);
    }

    /// <summary>ASCII form for e-mail local parts ("Şükrü Öz" → "sukru.oz").</summary>
    public static string Ascii(string s) => Norm(s).ToLowerInvariant();

    public static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());

    /// <summary>Partially hidden form for reports: "Ahmet Yılmaz" → "Ah*** Yı****".</summary>
    public static string Hint(string s)
    {
        if (s.Length <= 2) return new string('*', s.Length);
        var keep = Math.Min(2, s.Length / 3);
        var sb = new StringBuilder();
        var inWord = 0;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch)) { sb.Append(ch); inWord = 0; continue; }
            sb.Append(inWord < keep ? ch : '*');
            inWord++;
        }
        return sb.ToString();
    }
}
