using System.Windows;

namespace SheetMask.App;

public partial class App : Application
{
    /// <summary>An .xlsx passed on the command line (or dropped on the exe) is opened at start.</summary>
    public static string? FileArg { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        FileArg = e.Args.FirstOrDefault(a => a.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                                          || a.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase));
        base.OnStartup(e);
    }
}
