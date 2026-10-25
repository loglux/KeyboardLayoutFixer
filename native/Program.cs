using KeyboardLayoutFixer;

// Usage: KeyboardLayoutFixer.exe [--lang ru|en] [--scan] [--fix] [--screenshot path.png]
static string? Arg(string[] a, string name)
{
    int i = Array.IndexOf(a, name);
    return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
}

if (args.Contains("--tsf")) { Native.AttachConsole(-1); var e = Tsf.Enabled(); Console.WriteLine(e == null ? "TSF: unavailable" : string.Join(Environment.NewLine, e.Select(x => $"lang={x.Lang:X4} hkl={x.Hkl:X8} type={x.Type} flags={x.Flags}"))); return; }
var lang = Arg(args, "--lang");
if (lang is "ru" or "en") Loc.Lang = lang;

if (args.Contains("--scan") || args.Contains("--fix") || args.Contains("--restore") || Arg(args, "--fix-klid") != null)
{
    Native.AttachConsole(-1);   // WinExe: print to the console it was started from
    var svc = new LayoutService();
    // also written to a log file: a WinExe started without a console (e.g. inside an MSIX container) has no stdout
    var logFile = Path.Combine(Path.GetDirectoryName(LayoutService.BackupDir)!, "last-cli.log");
    Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
    File.WriteAllText(logFile, "");
    void Print(string m, Level l)
    {
        var line = $"[{l}] {m}";
        Console.WriteLine(line);
        File.AppendAllText(logFile, line + Environment.NewLine);
    }

    foreach (var f in svc.Diagnose()) Print(f.Text, f.Level);
    // debugging: run the add -> remove cycle for an arbitrary layout, e.g. --fix-klid 0409:00000409
    if (Arg(args, "--fix-klid") is { } spec)
    {
        var parts = spec.Split(':');
        int langId = Convert.ToInt32(parts[0], 16);
        var fake = new LiveLayout(langId, System.Globalization.CultureInfo.GetCultureInfo(langId).Name, parts[1].ToUpperInvariant(), "test");
        await svc.FixAsync(fake, Print);
        foreach (var f in svc.Diagnose()) Print(f.Text, f.Level);
    }
    if (args.Contains("--restore"))
    {
        var last = LayoutService.LatestBackup();
        if (last == null) Print(Loc.T("r_none"), Level.Warn);
        else { Print(Loc.T("r_restoring", Path.GetFileName(last)), Level.Info); svc.Restore(last, Print); }
        foreach (var f in svc.Diagnose()) Print(f.Text, f.Level);
    }
    if (args.Contains("--fix"))
    {
        foreach (var p in LayoutService.FindPhantoms(svc.GetProfile(), svc.GetLive()))
        {
            Print(Loc.T("f_fixing", $"{p.Culture} ({p.Layout})"), Level.Info);
            await svc.FixAsync(p, Print);
        }
        foreach (var f in svc.Diagnose()) Print(f.Text, f.Level);
    }
    return;
}

ApplicationConfiguration.Initialize();
Application.Run(new MainForm(Arg(args, "--screenshot")));
