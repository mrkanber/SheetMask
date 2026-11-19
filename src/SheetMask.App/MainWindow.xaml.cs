/*
 * MainWindow: WebView2 host. The page (public\) and every /api/* call are answered in-process through
 * WebResourceRequested; there is no listening port and no network access. Heavy work runs on the
 * thread pool; responses are created back on the UI thread.
 */
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using SheetMask.Core;

namespace SheetMask.App;

public partial class MainWindow : Window
{
    private const string Host = "sheetmask.local";
    private const int SampleRows = 30;
    private const int PreviewRows = 20;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    // session state
    private WorkbookPlan? _plan;
    private byte[]? _sample;
    private Revealer? _revealer;
    private string? _keyPath;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SheetMask", "WebView2");
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await Web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            Status.Text = "WebView2 runtime could not be started.\n" + ex.Message + "\n\nMicrosoft Edge WebView2 Runtime must be installed.";
            return;
        }

        var core = Web.CoreWebView2;
        core.AddWebResourceRequestedFilter($"https://{Host}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnRequest;
        core.Settings.AreDefaultContextMenusEnabled = true;   // copy / paste in text boxes
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
        core.Settings.IsReputationCheckingRequired = false;   // no SmartScreen look-ups: the page never leaves sheetmask.local
        core.NewWindowRequested += (_, a) => a.Handled = true;
        core.NavigationStarting += (_, a) =>
        {
            if (!a.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase)) a.Cancel = true;
        };
        core.NavigationCompleted += (_, _) => Status.Visibility = Visibility.Collapsed;
        core.WebMessageReceived += (_, a) => Log("JS: " + a.TryGetWebMessageAsString());
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            "window.addEventListener('error',function(e){try{chrome.webview.postMessage('error: '+e.message+' @'+e.filename+':'+e.lineno);}catch(_){}});");
        Web.Source = new Uri($"https://{Host}/index.html");
    }

    // ------------------------------------------------------------------ routing

    private void OnRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = new Uri(e.Request.Uri);
        if (!uri.AbsolutePath.StartsWith("/api/"))
        {
            ServeStatic(e, uri.AbsolutePath);
            return;
        }
        var body = ReadBody(e.Request.Content);
        var deferral = e.GetDeferral();
        _ = Handle(e, uri.AbsolutePath, body, deferral);
    }

    private async Task Handle(CoreWebView2WebResourceRequestedEventArgs e, string path, string body, CoreWebView2Deferral deferral)
    {
        try
        {
            await System.Windows.Threading.Dispatcher.Yield();   // leave the event callback before showing dialogs
            var input = string.IsNullOrWhiteSpace(body) ? JsonDocument.Parse("{}").RootElement : JsonDocument.Parse(body).RootElement;
            object result = path switch
            {
                "/api/state" => new { file = App.FileArg, lang = SystemLanguage() },
                "/api/open" => await OpenFile(Str(input, "path")),
                "/api/reanalyze" => await Reanalyze(input),
                "/api/preview" => await Preview(input),
                "/api/mask" => await Mask(input),
                "/api/reveal-folder" => RevealInFolder(Str(input, "path")),
                "/api/key/choose" => ChooseKey(),
                "/api/key/unlock" => await UnlockKey(input),
                "/api/reveal/text" => RevealText(input),
                "/api/reveal/xlsx" => await RevealXlsx(),
                _ => throw new UserError("Unknown request.")
            };
            Respond(e, 200, result);
        }
        catch (UserError ex) { Respond(e, 400, new { error = ex.Message }); }
        catch (UnauthorizedAccessException ex) { Respond(e, 400, new { error = ex.Message, code = "password" }); }
        catch (NotSupportedException ex) { Respond(e, 400, new { error = ex.Message }); }
        catch (IOException ex) { Log(ex.ToString()); Respond(e, 400, new { error = ex.Message }); }
        catch (Exception ex)
        {
            Log(path + "\n" + ex);
            Respond(e, 500, new { error = ex.Message });
        }
        finally
        {
            deferral.Complete();
        }
    }

    private sealed class UserError(string message) : Exception(message);

    /// <summary>Turkish when either the Windows display language or the regional format is Turkish.</summary>
    private static string SystemLanguage() =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ||
        System.Globalization.CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";

    // ------------------------------------------------------------------ mask side

    private async Task<object> OpenFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            var dlg = new OpenFileDialog { Filter = "Excel workbook (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|Old Excel (*.xls)|*.xls", Title = "Choose a workbook" };
            if (dlg.ShowDialog(this) != true) return new { cancelled = true };
            path = dlg.FileName;
        }
        if (!File.Exists(path)) throw new UserError("File not found: " + path);
        return await Analyze(path, null);
    }

    private async Task<object> Reanalyze(JsonElement input)
    {
        if (_plan == null) throw new UserError("No file is open.");
        var rows = input.TryGetProperty("headerRows", out var hr)
            ? hr.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32())
            : null;
        return await Analyze(_plan.SourcePath, rows);
    }

    private async Task<object> Analyze(string path, Dictionary<string, int>? headerRows)
    {
        var (plan, sample) = await Task.Run(() =>
        {
            var p = Detector.Analyze(path, headerRows, SampleRows, out var s);
            return (p, s);
        });
        _plan = plan;
        _sample = sample;
        var info = new FileInfo(path);
        return new { plan, file = new { name = info.Name, folder = info.DirectoryName, size = info.Length } };
    }

    /// <summary>Takes the edited plan from the page; the source path always comes from the session.</summary>
    private WorkbookPlan PlanFrom(JsonElement input)
    {
        if (_plan == null) throw new UserError("No file is open.");
        var plan = input.GetProperty("plan").Deserialize<WorkbookPlan>(Json) ?? throw new UserError("Missing plan.");
        plan.SourcePath = _plan.SourcePath;
        return plan;
    }

    private async Task<object> Preview(JsonElement input)
    {
        var plan = PlanFrom(input);
        var sample = _sample;
        var sheets = await Task.Run(() => new Masker(plan).Preview(PreviewRows, sample));
        return new { sheets };
    }

    private async Task<object> Mask(JsonElement input)
    {
        var plan = PlanFrom(input);
        var password = Str(input, "password") ?? "";
        if (password.Length < 6) throw new UserError("The key password needs at least 6 characters.");

        var (outPath, keyPath) = OutputPaths(plan.SourcePath);
        var masker = new Masker(plan) { Progress = stage => Post(new { type = "progress", stage }) };
        var result = await Task.Run(() => masker.Run(outPath, keyPath, password));
        return result;
    }

    /// <summary>"bordro.xlsx" → "bordro.masked.xlsx" + "bordro.masked.smkey"; never overwrites an earlier pair.</summary>
    private static (string Out, string Key) OutputPaths(string source)
    {
        var dir = Path.GetDirectoryName(source)!;
        var name = Path.GetFileNameWithoutExtension(source);
        for (var i = 1; ; i++)
        {
            var stem = Path.Combine(dir, name + ".masked" + (i > 1 ? "-" + i : ""));
            if (!File.Exists(stem + ".xlsx") && !File.Exists(stem + ".smkey")) return (stem + ".xlsx", stem + ".smkey");
        }
    }

    private static object RevealInFolder(string? path)
    {
        if (path != null && File.Exists(path))
            Process.Start("explorer.exe", "/select,\"" + path + "\"");
        return new { ok = true };
    }

    // ------------------------------------------------------------------ reveal side

    private object ChooseKey()
    {
        var dlg = new OpenFileDialog { Filter = "SheetMask key (*.smkey)|*.smkey", Title = "Choose a key file" };
        if (dlg.ShowDialog(this) != true) return new { cancelled = true };
        return new { path = dlg.FileName, name = Path.GetFileName(dlg.FileName) };
    }

    private async Task<object> UnlockKey(JsonElement input)
    {
        var path = Str(input, "path") ?? throw new UserError("Choose a key file first.");
        var password = Str(input, "password") ?? "";
        var key = await Task.Run(() => KeyFile.Load(path, password));
        _revealer = new Revealer(key);
        _keyPath = path;
        return new
        {
            sourceFile = key.SourceFile,
            created = key.Created,
            columns = key.Columns.Count,
            values = key.Maps.Sum(m => m.Value.Count),
            hasAmounts = key.Columns.Any(c => c.Kind == ColumnKind.Scale),
            hasDates = key.DateShiftDays != 0 && key.Columns.Any(c => c.Kind == ColumnKind.DateShift),
            noise = key.NoisePercent
        };
    }

    private object RevealText(JsonElement input)
    {
        if (_revealer == null) throw new UserError("Unlock a key file first.");
        var text = Str(input, "text") ?? "";
        var numbers = input.TryGetProperty("numbers", out var n) && n.GetBoolean();
        var dates = input.TryGetProperty("dates", out var d) && d.GetBoolean();
        var output = _revealer.RevealText(text, numbers, dates, out var count);
        return new { text = output, count };
    }

    private async Task<object> RevealXlsx()
    {
        if (_revealer == null) throw new UserError("Unlock a key file first.");
        var dlg = new OpenFileDialog { Filter = "Excel workbook (*.xlsx;*.xlsm)|*.xlsx;*.xlsm", Title = "Choose the masked or AI-edited workbook" };
        if (dlg.ShowDialog(this) != true) return new { cancelled = true };
        var input = dlg.FileName;
        var dir = Path.GetDirectoryName(input)!;
        var stem = Path.GetFileNameWithoutExtension(input);
        var output = Path.Combine(dir, stem + ".revealed.xlsx");
        for (var i = 2; File.Exists(output); i++) output = Path.Combine(dir, stem + ".revealed-" + i + ".xlsx");
        var notes = new List<string>();
        var rv = _revealer;
        var changed = await Task.Run(() => rv.RevealWorkbook(input, output, notes));
        return new { outputPath = output, changed, notes };
    }

    // ------------------------------------------------------------------ plumbing

    private void Post(object message)
    {
        var json = JsonSerializer.Serialize(message, Json);
        Dispatcher.BeginInvoke(() => Web.CoreWebView2?.PostWebMessageAsJson(json));
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string ReadBody(Stream? content)
    {
        if (content == null) return "";
        using var r = new StreamReader(content, Encoding.UTF8);
        return r.ReadToEnd();
    }

    private void Respond(CoreWebView2WebResourceRequestedEventArgs e, int status, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        e.Response = Web.CoreWebView2.Environment.CreateWebResourceResponse(
            new MemoryStream(bytes), status, status == 200 ? "OK" : "Error",
            "Content-Type: application/json; charset=utf-8\r\nCache-Control: no-store");
    }

    private void ServeStatic(CoreWebView2WebResourceRequestedEventArgs e, string path)
    {
        var publicDir = Path.Combine(AppContext.BaseDirectory, "public");
        var rel = path.TrimStart('/');
        if (rel.Length == 0) rel = "index.html";
        var full = Path.GetFullPath(Path.Combine(publicDir, rel));
        if (!full.StartsWith(publicDir, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            e.Response = Web.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
            return;
        }
        var type = Path.GetExtension(full).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
        e.Response = Web.CoreWebView2.Environment.CreateWebResourceResponse(
            new MemoryStream(File.ReadAllBytes(full)), 200, "OK", $"Content-Type: {type}\r\nCache-Control: no-store");
    }

    private static void Log(string text)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SheetMask");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), DateTime.Now.ToString("s") + "  " + text + Environment.NewLine);
        }
        catch { /* not important */ }
    }
}
