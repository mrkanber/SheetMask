namespace SheetMask.Core;

/// <summary>Everything needed to turn masked values back into real ones. Stored encrypted (see KeyFile).</summary>
public sealed class MaskKey
{
    public int Version { get; set; } = 1;
    public DateTime Created { get; set; } = DateTime.Now;
    public string SourceFile { get; set; } = "";
    public double SharedFactor { get; set; }
    public int DateShiftDays { get; set; }
    public double NoisePercent { get; set; }
    public List<KeyColumn> Columns { get; set; } = new();

    /// <summary>domain → (real → fake). Domains: first, last, city, country, address, tckn, phone, email, iban, id:HEADER, cat:HEADER.</summary>
    public Dictionary<string, Dictionary<string, string>> Maps { get; set; } = new();
}

public sealed class KeyColumn
{
    public string Sheet { get; set; } = "";
    public int Column { get; set; }
    public string Header { get; set; } = "";
    public ColumnKind Kind { get; set; }
    public double Factor { get; set; }
}
