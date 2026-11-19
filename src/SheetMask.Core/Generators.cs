using System.Security.Cryptography;
using System.Text;

namespace SheetMask.Core;

/// <summary>
/// Cryptographic randomness. A seeded System.Random would let someone holding the masked
/// file brute-force the 32-bit seed from the fake values and recover the secret factor.
/// </summary>
public static class Rng
{
    public static int Next(int maxExclusive) => RandomNumberGenerator.GetInt32(maxExclusive);
    public static int Next(int min, int maxExclusive) => RandomNumberGenerator.GetInt32(min, maxExclusive);

    public static double NextDouble()
    {
        Span<byte> b = stackalloc byte[8];
        RandomNumberGenerator.Fill(b);
        return (BitConverter.ToUInt64(b) >> 11) * (1.0 / (1UL << 53));
    }

    public static T Pick<T>(IReadOnlyList<T> list) => list[Next(list.Count)];

    public static List<T> Shuffled<T>(IEnumerable<T> items)
    {
        var l = items.ToList();
        for (var i = l.Count - 1; i > 0; i--)
        {
            var j = Next(i + 1);
            (l[i], l[j]) = (l[j], l[i]);
        }
        return l;
    }

    /// <summary>An "ugly" factor far from 1: 0.35–0.85 or 1.25–2.8, six decimals.</summary>
    public static double Factor()
    {
        var f = Next(2) == 0 ? 0.35 + NextDouble() * 0.50 : 1.25 + NextDouble() * 1.55;
        return Math.Round(f, 6);
    }
}

public static class Generators
{
    private const string Upper = "ABCDEFGHJKLMNPRSTUVYZ";
    private const string Lower = "abcdefghjklmnprstuvyz";

    // ------------------------------------------------------------------ TCKN

    public static bool IsValidTckn(string s)
    {
        if (s.Length != 11 || !s.All(char.IsDigit) || s[0] == '0') return false;
        var d = s.Select(c => c - '0').ToArray();
        var d10 = ((d[0] + d[2] + d[4] + d[6] + d[8]) * 7 - (d[1] + d[3] + d[5] + d[7])) % 10;
        if (d10 < 0) d10 += 10;
        var d11 = d.Take(10).Sum() % 10;
        return d[9] == d10 && d[10] == d11;
    }

    public static string Tckn()
    {
        var d = new int[11];
        d[0] = Rng.Next(1, 10);
        for (var i = 1; i < 9; i++) d[i] = Rng.Next(10);
        var d10 = ((d[0] + d[2] + d[4] + d[6] + d[8]) * 7 - (d[1] + d[3] + d[5] + d[7])) % 10;
        d[9] = d10 < 0 ? d10 + 10 : d10;
        d[10] = d.Take(10).Sum() % 10;
        return string.Concat(d);
    }

    // ------------------------------------------------------------------ IBAN

    public static bool IsTrIban(string s)
    {
        var c = s.Replace(" ", "").ToUpperInvariant();
        return c.Length == 26 && c.StartsWith("TR") && c[2..].All(char.IsDigit) && IbanMod97(c) == 1;
    }

    private static int IbanMod97(string iban)
    {
        var r = iban[4..] + iban[..4];
        var rem = 0;
        foreach (var ch in r)
        {
            var v = char.IsDigit(ch) ? (ch - '0').ToString() : (ch - 'A' + 10).ToString();
            foreach (var dc in v) rem = (rem * 10 + (dc - '0')) % 97;
        }
        return rem;
    }

    /// <summary>Valid TR IBAN; keeps the original's spacing (groups of four or none).</summary>
    public static string TrIban(string original)
    {
        var body = new StringBuilder();
        for (var i = 0; i < 5; i++) body.Append(Rng.Next(10));   // bank code
        body.Append('0');                                          // reserved
        for (var i = 0; i < 16; i++) body.Append(Rng.Next(10));   // account
        var tmp = "TR00" + body;
        var check = 98 - IbanMod97(tmp);
        var iban = "TR" + check.ToString("00") + body;
        if (!original.Contains(' ')) return iban;
        return string.Join(' ', Enumerable.Range(0, (iban.Length + 3) / 4)
            .Select(i => iban.Substring(i * 4, Math.Min(4, iban.Length - i * 4))));
    }

    // ----------------------------------------------------------------- phone

    /// <summary>
    /// Keeps every non-digit character and the dialling prefix (+90 / 0 and the next three digits),
    /// replaces the subscriber digits.
    /// </summary>
    public static string Phone(string original)
    {
        var digits = Text.Digits(original);
        var keep = digits.StartsWith("90") && digits.Length >= 12 ? 5
                 : digits.StartsWith("0") ? 4
                 : digits.Length == 10 ? 3
                 : Math.Max(0, digits.Length - 7);
        var sb = new StringBuilder(original.Length);
        var idx = 0;
        foreach (var ch in original)
        {
            if (!char.IsDigit(ch)) { sb.Append(ch); continue; }
            sb.Append(idx < keep ? ch : (char)('0' + Rng.Next(10)));
            idx++;
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------- identifier

    /// <summary>Same shape: digit → random digit, letter → random letter of the same case.</summary>
    public static string SameShape(string original)
    {
        var sb = new StringBuilder(original.Length);
        for (var i = 0; i < original.Length; i++)
        {
            var ch = original[i];
            if (char.IsDigit(ch)) sb.Append((char)('0' + (i == 0 && ch != '0' ? Rng.Next(1, 10) : Rng.Next(10))));
            else if (char.IsUpper(ch)) sb.Append(Upper[Rng.Next(Upper.Length)]);
            else if (char.IsLower(ch)) sb.Append(Lower[Rng.Next(Lower.Length)]);
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>Pronounceable made-up word, used when a word list runs out.</summary>
    public static string Word(int syllables = 3)
    {
        string[] cons = { "b", "c", "d", "f", "g", "k", "l", "m", "n", "p", "r", "s", "t", "v", "y", "z" };
        string[] vow = { "a", "e", "ı", "i", "o", "u" };
        var sb = new StringBuilder();
        for (var i = 0; i < syllables; i++) sb.Append(Rng.Pick(cons)).Append(Rng.Pick(vow));
        if (Rng.Next(2) == 0) sb.Append(Rng.Pick(cons));
        return Text.TitleCase(sb.ToString());
    }

    public static string Letters(int index)
    {
        // 0 → A, 25 → Z, 26 → AA ...
        var s = "";
        index++;
        while (index > 0)
        {
            index--;
            s = (char)('A' + index % 26) + s;
            index /= 26;
        }
        return s;
    }
}
