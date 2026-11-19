namespace SheetMask.Core;

public enum ColumnKind
{
    Keep,           // leave untouched
    Scale,          // numbers × secret factor (ratios preserved)
    RandomNumber,   // numbers → random value inside the column's min–max
    FirstName,
    LastName,
    FullName,
    City,
    Country,
    Category,       // department, branch, title … → "Department A"
    Address,
    Identifier,     // employee no, plate, policy no … → same shape, random characters
    NationalId,     // TCKN → valid fake TCKN
    Phone,
    Email,
    Iban,
    DateShift,      // all dates moved by the same secret number of days
    FreeText,       // notes: known values + TCKN/phone/e-mail/IBAN patterns replaced inside the text
    Redact,         // "Ahmet Yılmaz" → "A***t Y****z"; cannot be restored, rows become indistinguishable
    Clear           // emptied
}

public sealed class ColumnPlan
{
    public string Sheet { get; set; } = "";
    public int Column { get; set; }
    public string Letter { get; set; } = "";
    public string Header { get; set; } = "";
    public ColumnKind Kind { get; set; }
    public ColumnKind Detected { get; set; }
    public string Reason { get; set; } = "";
    public int NonEmpty { get; set; }
    public int Formulas { get; set; }
    public string ValueType { get; set; } = "";   // "number", "text", "date", "mixed", "empty"
    public List<string> Samples { get; set; } = new();
}

public sealed class SheetPlan
{
    public string Name { get; set; } = "";
    public int HeaderRow { get; set; }      // 0 = no header row
    public int FirstDataRow { get; set; }
    public int LastRow { get; set; }
    public List<ColumnPlan> Columns { get; set; } = new();
}

public sealed class MaskOptions
{
    /// <summary>All Scale columns share one factor, so "net + deduction = gross" style relations survive.</summary>
    public bool SharedFactor { get; set; } = true;

    /// <summary>Random ± percentage added on top of the factor (0 = exact proportions).</summary>
    public double NoisePercent { get; set; }

    /// <summary>45 000 stays a round-looking number (error kept under 0.5%).</summary>
    public bool KeepRoundness { get; set; } = true;

    /// <summary>Date shift is a multiple of 7 days, so weekdays stay the same.</summary>
    public bool KeepWeekday { get; set; } = true;

    /// <summary>Replace already-mapped values and ID/phone/e-mail patterns in every other text cell too.</summary>
    public bool ScanAllText { get; set; } = true;

    public bool RemoveComments { get; set; } = true;
    public bool ClearProperties { get; set; } = true;
}

public sealed class WorkbookPlan
{
    public string SourcePath { get; set; } = "";
    public List<SheetPlan> Sheets { get; set; } = new();
    public MaskOptions Options { get; set; } = new();

    public IEnumerable<ColumnPlan> AllColumns => Sheets.SelectMany(s => s.Columns);
}

public sealed class LeakFinding
{
    public string Part { get; set; } = "";     // e.g. "xl/sharedStrings.xml"
    public string What { get; set; } = "";     // kind of value
    public string Hint { get; set; } = "";     // partially hidden value
    public int Count { get; set; }
}

public sealed class MaskResult
{
    public string OutputPath { get; set; } = "";
    public string KeyPath { get; set; } = "";
    public int CellsChanged { get; set; }
    public int TextReplacements { get; set; }
    public double Factor { get; set; }
    public int DateShiftDays { get; set; }
    public List<LeakFinding> Leaks { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

public sealed class PreviewSheet
{
    public string Name { get; set; } = "";
    public List<List<string>> Before { get; set; } = new();
    public List<List<string>> After { get; set; } = new();
}
