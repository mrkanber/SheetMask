using System.Globalization;
using ClosedXML.Excel;
using SheetMask.Core;

// SheetMask command line — same engine as the desktop app.
//   sample  <out.xlsx>                                  realistic demo workbook
//   analyze <file.xlsx>                                 proposed kind per column
//   mask    <file.xlsx> --password P [--noise 2] [--set "Sheet!C=Scale"]... [--independent] [--no-round]
//   reveal  <key.smkey> --password P (--text "..." | --xlsx file.xlsx) [--numbers] [--dates]

Console.OutputEncoding = System.Text.Encoding.UTF8;
if (args.Length == 0) { Usage(); return 1; }

try
{
    switch (args[0])
    {
        case "sample": Sample(args[1], args.Length > 2 ? int.Parse(args[2]) : 40); return 0;
        case "analyze": PrintPlan(Detector.Analyze(args[1])); return 0;
        case "preview":
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var plan0 = Detector.Analyze(args[1], null, 30, out var sampleBook);
            Console.WriteLine($"analyze {sw.ElapsedMilliseconds} ms");
            var mk = new Masker(plan0) { Progress = st => Console.WriteLine($"  {st} @ {sw.ElapsedMilliseconds} ms") };
            var pv = mk.Preview(15, sampleBook);
            Console.WriteLine($"first preview done @ {sw.ElapsedMilliseconds} ms");
            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            new Masker(plan0).Preview(15, sampleBook);
            Console.WriteLine($"second preview {sw2.ElapsedMilliseconds} ms");
            Console.WriteLine($"preview {sw.ElapsedMilliseconds} ms");
            foreach (var p in pv) Console.WriteLine(p.Name + ": " + string.Join(" | ", p.Before.FirstOrDefault() ?? new()) + "\n  -> " + string.Join(" | ", p.After.FirstOrDefault() ?? new()));
            return 0;
        }
        case "mask": return Mask(args);
        case "reveal": return Reveal(args);
        case "dump": Dump(args[1], args.Skip(2).Select(int.Parse).ToArray()); return 0;
        default: Usage(); return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine("error: " + ex.Message);
    return 2;
}

static void Usage() => Console.WriteLine("""
    sheetmask sample  <out.xlsx>
    sheetmask analyze <file.xlsx>
    sheetmask mask    <file.xlsx> --password P [--noise 2] [--set "Sheet!C=Scale"]... [--independent] [--no-round]
    sheetmask reveal  <key.smkey> --password P (--text "..." | --xlsx file.xlsx) [--numbers] [--dates]
    """);

static string? Opt(string[] a, string name) { var i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

static void PrintPlan(WorkbookPlan plan)
{
    foreach (var s in plan.Sheets)
    {
        Console.WriteLine($"== {s.Name}  header row {s.HeaderRow}, data {s.FirstDataRow}-{s.LastRow}");
        foreach (var c in s.Columns)
            Console.WriteLine($"  {c.Letter,-3} {Trim(c.Header, 22),-22} {c.Kind,-12} {Trim(c.Reason, 38),-38} {Trim(string.Join(" | ", c.Samples), 50)}");
    }
}

static string Trim(string s, int n) => s.Length > n ? s[..(n - 1)] + "…" : s;

static int Mask(string[] a)
{
    var path = a[1];
    var pw = Opt(a, "--password") ?? throw new ArgumentException("--password is required");
    var plan = Detector.Analyze(path);
    if (Opt(a, "--noise") is { } noise) plan.Options.NoisePercent = double.Parse(noise, CultureInfo.InvariantCulture);
    if (a.Contains("--independent")) plan.Options.SharedFactor = false;
    if (a.Contains("--no-round")) plan.Options.KeepRoundness = false;
    if (a.Contains("--no-scan")) plan.Options.ScanAllText = false;
    for (var i = 0; i < a.Length - 1; i++)
    {
        if (a[i] != "--set") continue;
        var (target, kind) = (a[i + 1].Split('=')[0], Enum.Parse<ColumnKind>(a[i + 1].Split('=')[1], true));
        var (sheet, letter) = (target.Split('!')[0], target.Split('!')[1]);
        var col = plan.AllColumns.First(c => c.Sheet == sheet && c.Letter == letter);
        col.Kind = kind;
    }
    PrintPlan(plan);
    var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
    var name = Path.GetFileNameWithoutExtension(path);
    var swm = System.Diagnostics.Stopwatch.StartNew();
    var res = new Masker(plan) { Progress = st => Console.WriteLine($"  {st} @ {swm.ElapsedMilliseconds} ms") }.Run(Path.Combine(dir, name + ".masked.xlsx"), Path.Combine(dir, name + ".smkey"), pw);
    Console.WriteLine($"\ncells changed {res.CellsChanged}, in-text replacements {res.TextReplacements}, factor {res.Factor}, date shift {res.DateShiftDays} d");
    Console.WriteLine($"time {swm.ElapsedMilliseconds} ms");
    Console.WriteLine($"out: {res.OutputPath}\nkey: {res.KeyPath}");
    foreach (var n in res.Notes) Console.WriteLine("note: " + n);
    Console.WriteLine(res.Leaks.Count == 0 ? "leak check: clean" : "leak check: " + res.Leaks.Count + " finding(s)");
    foreach (var l in res.Leaks) Console.WriteLine($"  LEAK {l.Part}  {l.What}  {l.Hint}  x{l.Count}");
    return res.Leaks.Count == 0 ? 0 : 3;
}

static int Reveal(string[] a)
{
    var key = KeyFile.Load(a[1], Opt(a, "--password") ?? throw new ArgumentException("--password is required"));
    var rv = new Revealer(key);
    if (Opt(a, "--text") is { } text)
    {
        Console.WriteLine(rv.RevealText(text, a.Contains("--numbers"), a.Contains("--dates"), out var n));
        Console.WriteLine($"({n} replacements)");
        return 0;
    }
    var xlsx = Opt(a, "--xlsx") ?? throw new ArgumentException("--text or --xlsx is required");
    var outPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(xlsx))!, Path.GetFileNameWithoutExtension(xlsx) + ".revealed.xlsx");
    var notes = new List<string>();
    var changed = rv.RevealWorkbook(xlsx, outPath, notes);
    Console.WriteLine($"{changed} cells restored → {outPath}");
    foreach (var n in notes) Console.WriteLine("note: " + n);
    return 0;
}

static void Dump(string path, int[] rows)
{
    using var wb = new XLWorkbook(path);
    Console.WriteLine($"author='{wb.Properties.Author}' company='{wb.Properties.Company}'");
    foreach (var ws in wb.Worksheets)
    {
        Console.WriteLine("== " + ws.Name);
        var last = ws.LastRowUsed()?.RowNumber() ?? 0;
        foreach (var r in rows.Length > 0 ? rows : Enumerable.Range(1, Math.Min(5, last)).ToArray())
        {
            if (r > last) continue;
            var cells = ws.Row(r).CellsUsed(XLCellsUsedOptions.All).Select(c =>
                (c.HasFormula ? "{" + c.FormulaA1 + "}=" : "") + c.GetFormattedString()
                + (c.HasComment ? " [comment:" + c.GetComment().Text + "]" : "")
                + (c.HasHyperlink ? " [link:" + c.GetHyperlink().ExternalAddress + "]" : ""));
            Console.WriteLine($"{r,3}: " + string.Join(" ¦ ", cells));
        }
    }
}

static void Sample(string path, int count)
{
    var rnd = new Random(7);
    string P(string[] l) => l[rnd.Next(l.Length)];
    using var wb = new XLWorkbook();
    var ws = wb.AddWorksheet("Personel");
    ws.Cell(1, 1).Value = "2026 Kasım Bordro Listesi – Hazırlayan: Kemal Öztürk";
    ws.Range(1, 1, 1, 8).Merge();
    string[] headers = { "Sicil No", "Ad Soyad", "T.C. Kimlik No", "Doğum Tarihi", "İşe Giriş", "Departman", "Unvan", "Şehir",
                         "Cep Telefonu", "E-posta", "IBAN", "Brüt Maaş", "Net Maaş", "Prim", "Açıklama" };
    for (var i = 0; i < headers.Length; i++) ws.Cell(3, i + 1).Value = headers[i];
    string[] deps = { "Satış", "Muhasebe", "Bilgi İşlem", "İnsan Kaynakları", "Üretim" };
    string[] titles = { "Uzman", "Kıdemli Uzman", "Müdür", "Mühendis", "Teknisyen" };
    var row = 4;
    for (var i = 0; i < count; i++, row++)
    {
        var female = rnd.Next(2) == 0;
        var first = P(female ? Pools.FemaleTr : Pools.MaleTr);
        var last = P(Pools.SurnameTr);
        ws.Cell(row, 1).Value = 10001 + i;
        ws.Cell(row, 2).Value = $"{first} {last}";
        ws.Cell(row, 3).Value = Generators.Tckn();
        ws.Cell(row, 4).Value = new DateTime(1965 + rnd.Next(35), rnd.Next(1, 13), rnd.Next(1, 28));
        ws.Cell(row, 5).Value = new DateTime(2005 + rnd.Next(20), rnd.Next(1, 13), rnd.Next(1, 28));
        ws.Cell(row, 6).Value = P(deps);
        ws.Cell(row, 7).Value = P(titles);
        ws.Cell(row, 8).Value = P(Pools.CitiesTr);
        ws.Cell(row, 9).Value = $"05{rnd.Next(30, 56)} {rnd.Next(100, 999)} {rnd.Next(10, 99)} {rnd.Next(10, 99)}";
        var mail = $"{Text.Ascii(first)}.{Text.Ascii(last)}@ornek-firma.com.tr";
        ws.Cell(row, 10).Value = mail;
        ws.Cell(row, 10).SetHyperlink(new XLHyperlink("mailto:" + mail));
        ws.Cell(row, 11).Value = Generators.TrIban("TR00 0000");
        ws.Cell(row, 12).Value = 30000 + rnd.Next(0, 90) * 1000;
        ws.Cell(row, 13).FormulaA1 = $"=ROUND(L{row}*0.71,2)";
        ws.Cell(row, 14).Value = Math.Round(rnd.NextDouble() * 8000, 2);
        ws.Cell(row, 15).Value = rnd.Next(4) switch
        {
            0 => $"{first} Hanım/Bey {ws.Cell(row, 9).GetString()} numarasından ulaşılabilir.",
            1 => $"İzin dönüşü 15.03.2025 tarihinde başladı, yöneticisi {P(Pools.MaleTr)} {P(Pools.SurnameTr)}.",
            2 => "Performans değerlendirmesi olumlu.",
            _ => ""
        };
    }
    ws.Cell(row, 11).Value = "TOPLAM";
    ws.Cell(row, 12).FormulaA1 = $"=SUM(L4:L{row - 1})";
    ws.Cell(row, 13).FormulaA1 = $"=SUM(M4:M{row - 1})";
    ws.Cell(row, 14).FormulaA1 = $"=SUM(N4:N{row - 1})";
    ws.Column(4).Style.DateFormat.Format = "dd.MM.yyyy";
    ws.Column(5).Style.DateFormat.Format = "dd.MM.yyyy";
    ws.Columns(12, 14).Style.NumberFormat.Format = "#,##0.00";
    ws.Row(3).Style.Font.Bold = true;
    ws.Cell(5, 2).CreateComment().AddText("Ayşe Yılmaz ile görüşüldü.");

    var s2 = wb.AddWorksheet("Şubeler");
    string[] h2 = { "Şube", "Ülke", "Müdür", "Ciro" };
    for (var i = 0; i < h2.Length; i++) s2.Cell(1, i + 1).Value = h2[i];
    for (var i = 0; i < 8; i++)
    {
        s2.Cell(i + 2, 1).Value = P(Pools.CitiesTr) + " Şubesi";
        s2.Cell(i + 2, 2).Value = P(new[] { "Türkiye", "Almanya", "Azerbaycan" });
        s2.Cell(i + 2, 3).Value = P(Pools.FemaleTr) + " " + P(Pools.SurnameTr);
        s2.Cell(i + 2, 4).Value = (rnd.Next(500, 5000) * 1000 + 0.5).ToString("#,##0.00", Text.Tr) + " TL";
    }
    wb.Properties.Author = "Kemal Öztürk";
    wb.Properties.Company = "Örnek A.Ş.";
    wb.SaveAs(path);
    Console.WriteLine("sample written: " + path);
}
