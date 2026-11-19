using ClosedXML.Excel;

namespace SheetMask.Core;

public static class Book
{
    /// <summary>Opens a workbook even when Excel has it open (shared read).</summary>
    public static XLWorkbook Open(string path)
    {
        if (Path.GetExtension(path).Equals(".xls", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Old .xls files are not supported. Save the file as .xlsx in Excel first.");
        var ms = new MemoryStream();
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            fs.CopyTo(ms);
        ms.Position = 0;
        return new XLWorkbook(ms);
    }
}
