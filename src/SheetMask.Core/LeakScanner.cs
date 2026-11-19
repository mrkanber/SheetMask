using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SheetMask.Core;

/// <summary>
/// Opens the finished .xlsx as a zip and searches every XML part (cells, shared strings, comments, pivot caches,
/// charts, defined names, properties …) for the real values that were supposed to be replaced.
/// Linear time: the text is cut into tokens and every run of 1–8 tokens is looked up in a hash set,
/// so tens of thousands of sensitive values cost no more than a handful.
/// </summary>
public static class LeakScanner
{
    private const int MaxTokens = 8;
    internal static readonly Regex TokenRx = new(@"[\p{L}\p{N}@_%+]+(?:\.[\p{L}\p{N}@_%+]+)*", RegexOptions.Compiled);   // '-' separates: 0537-111 = 0537 111
    private static readonly Regex ValueTag = new("<v>([^<]+)</v>", RegexOptions.Compiled);

    // Only places that carry content: text between tags, plus the few attributes that hold data
    // (pivot-cache items v="…", link targets, captions). Structural attributes such as row r="10001" are skipped.
    private static readonly Regex Content = new(@">([^<]+)<|\s(?:v|Target|caption)=""([^""]*)""", RegexOptions.Compiled);

    public static string Key(string s) => string.Join(' ', TokenRx.Matches(s).Select(m => Text.Norm(m.Value)));

    public static List<LeakFinding> Scan(string xlsxPath, IEnumerable<(string What, string Value)> sensitive, IEnumerable<double> realNumbers)
    {
        var byKey = new Dictionary<string, (string What, string Value)>();
        foreach (var s in sensitive)
        {
            var k = Key(s.Value);
            // Short pure numbers (employee no 1042) collide with ordinary numeric cells; they are not checked.
            if (k.Length < 3 || (k.All(char.IsDigit) && k.Length < 6)) continue;
            byKey.TryAdd(k, s);
        }
        var numbers = new HashSet<string>(realNumbers
            .Where(n => Math.Abs(n) >= 1000)
            .Select(n => n.ToString("R", CultureInfo.InvariantCulture)));

        var findings = new List<LeakFinding>();
        using var zip = ZipFile.OpenRead(xlsxPath);
        foreach (var entry in zip.Entries)
        {
            if (!(entry.FullName.EndsWith(".xml") || entry.FullName.EndsWith(".rels") || entry.FullName.EndsWith(".vml"))) continue;
            string xml;
            using (var r = new StreamReader(entry.Open())) xml = r.ReadToEnd();

            if (byKey.Count > 0)
            {
                var hits = new Dictionary<string, LeakFinding>();
                var sb = new StringBuilder();
                foreach (Match c in Content.Matches(xml))
                {
                    var piece = c.Groups[1].Success ? c.Groups[1].Value : c.Groups[2].Value;
                    var tokens = TokenRx.Matches(WebUtility.HtmlDecode(piece)).Select(m => Text.Norm(m.Value)).ToArray();
                    for (var i = 0; i < tokens.Length; i++)
                    {
                        sb.Clear();
                        for (var n = 0; n < MaxTokens && i + n < tokens.Length; n++)
                        {
                            if (n > 0) sb.Append(' ');
                            sb.Append(tokens[i + n]);
                            if (!byKey.TryGetValue(sb.ToString(), out var s)) continue;
                            if (!hits.TryGetValue(s.Value, out var f))
                                hits[s.Value] = f = new LeakFinding { Part = entry.FullName, What = s.What, Hint = Text.Hint(s.Value) };
                            f.Count++;
                        }
                    }
                }
                findings.AddRange(hits.Values);
            }

            if (numbers.Count > 0 && entry.FullName.StartsWith("xl/worksheets/"))
            {
                var count = ValueTag.Matches(xml).Count(m => numbers.Contains(m.Groups[1].Value));
                if (count > 0)
                    findings.Add(new LeakFinding { Part = entry.FullName, What = "number (possible)", Hint = "original amounts", Count = count });
            }
        }
        return findings;
    }
}
