using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;

namespace KeyboardLayoutFixer;

public enum Level { Info, Ok, Warn, Err }

internal sealed record ProfileLayout(string Tag, string Language, string Klid, string Layout, string Tip);
internal sealed record LiveLayout(int LangId, string Culture, string Klid, string Layout);
internal sealed record Finding(Level Level, string Text);
internal sealed record BackupEntry(string Tag, string[] Tips);

/// <summary>Scan / fix / restore logic. Changes the profile only through the documented InstallLayoutOrTip.</summary>
internal sealed class LayoutService
{
    private const string LayoutsKey = @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts";
    private Dictionary<string, (string Text, string Id)>? _table;

    public static string BackupDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyboardLayoutFixer", "backups");

    // ---------------------------------------------------------------- reading ----

    private Dictionary<string, (string Text, string Id)> Table()
    {
        if (_table != null) return _table;
        _table = new(StringComparer.OrdinalIgnoreCase);
        using var root = Registry.LocalMachine.OpenSubKey(LayoutsKey);
        if (root == null) return _table;
        foreach (var name in root.GetSubKeyNames())
        {
            using var k = root.OpenSubKey(name);
            _table[name] = (k?.GetValue("Layout Text") as string ?? "", k?.GetValue("Layout Id") as string ?? "");
        }
        return _table;
    }

    private string LayoutText(string klid) =>
        Table().TryGetValue(klid, out var e) && e.Text.Length > 0 ? e.Text : Loc.T("unknown");

    /// <summary>Profile = what Settings → Time &amp; language shows.</summary>
    public List<ProfileLayout> GetProfile()
    {
        var result = new List<ProfileLayout>();
        IEnumerable<(string Tag, string[] Tips)> source;
        try
        {
            source = Native.GetProfileLanguages().Select(l => (l, Native.GetProfileInputMethods(l))).ToList();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            source = ProfileFromRegistry();
        }

        foreach (var (tag, tips) in source)
        {
            string lang;
            try { lang = CultureInfo.GetCultureInfo(tag).DisplayName; } catch { lang = tag; }
            if (tips.Length == 0)
                result.Add(new(tag, lang, "", Loc.T("nolayout"), ""));
            foreach (var tip in tips)
            {
                var klid = tip.Contains(':') ? tip.Split(':')[1] : "";
                result.Add(new(tag, lang, klid, LayoutText(klid), tip));
            }
        }
        return result;
    }

    private static IEnumerable<(string Tag, string[] Tips)> ProfileFromRegistry()
    {
        using var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\International\User Profile");
        if (root == null) yield break;
        foreach (var tag in root.GetSubKeyNames())
        {
            using var k = root.OpenSubKey(tag);
            var tips = k?.GetValueNames().Where(n => n.Length > 5 && n[4] == ':').ToArray() ?? Array.Empty<string>();
            yield return (tag, tips);
        }
    }

    /// <summary>Layouts actually loaded in the session = what Win+Space switches between.</summary>
    public List<LiveLayout> GetLive()
    {
        var result = new List<LiveLayout>();
        foreach (var h in Native.GetLiveLayoutHandles())
        {
            long v = h.ToInt64();
            int lang = (int)(v & 0xFFFF);
            int hi = (int)((v >> 16) & 0xFFFF);
            string klid;
            if ((hi & 0xF000) == 0xF000)
            {
                var id = (hi & 0x0FFF).ToString("X4");
                var suffix = lang.ToString("X4");
                klid = Table().FirstOrDefault(p => p.Value.Id.Equals(id, StringComparison.OrdinalIgnoreCase)
                                                   && p.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).Key ?? "";
            }
            else if (hi == lang) klid = "0000" + lang.ToString("X4");
            else klid = hi.ToString("X4") + lang.ToString("X4");

            string culture;
            try { culture = CultureInfo.GetCultureInfo(lang).Name; } catch { culture = "?"; }
            result.Add(new(lang, culture, klid.ToUpperInvariant(), klid.Length > 0 ? LayoutText(klid) : Loc.T("undetermined")));
        }
        return result;
    }

    public List<string> GetPreload()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Keyboard Layout\Preload");
        if (k == null) return new();
        return k.GetValueNames().Where(n => int.TryParse(n, out _)).OrderBy(int.Parse)
                .Select(n => (k.GetValue(n) as string ?? "").ToUpperInvariant()).ToList();
    }

    /// <summary>Loaded in the session, but its KLID is not in the profile.</summary>
    private static List<LiveLayout> NotInProfile(IEnumerable<ProfileLayout> profile, IEnumerable<LiveLayout> live)
    {
        var known = profile.Where(p => p.Klid.Length > 0).Select(p => p.Klid.ToUpperInvariant()).ToHashSet();
        return live.Where(l => l.Klid.Length > 0 && !known.Contains(l.Klid)).ToList();
    }

    /// <summary>Is the layout one of the keyboard profiles that Win+Space lists? (null TSF = unknown, assume yes)</summary>
    private static bool InSwitcher(LiveLayout l, List<(int Lang, long Hkl, uint Type, uint Flags)>? tsf)
        => tsf == null || tsf.Any(t => t.Lang == l.LangId && (int)(t.Hkl & 0xFFFF) == l.LangId
                                        && (((t.Hkl >> 16) & 0xFFFF) is var hi && (hi == l.LangId || (hi & 0xF000) == 0xF000 || hi.ToString("X4") == l.Klid[..4])));

    /// <summary>Phantom = loaded in the session, not in the profile, and actually offered by Win+Space.</summary>
    public static List<LiveLayout> FindPhantoms(IEnumerable<ProfileLayout> profile, IEnumerable<LiveLayout> live)
    {
        var tsf = Tsf.Enabled();
        return NotInProfile(profile, live).Where(l => InSwitcher(l, tsf)).ToList();
    }
    public List<Finding> Diagnose()
    {
        var res = new List<Finding>();
        var prof = GetProfile();
        var live = GetLive();
        var pre = GetPreload();
        res.Add(new(Level.Info, Loc.T("d_summary", prof.Count, live.Count, pre.Count)));

        var ph = FindPhantoms(prof, live);
        if (ph.Count > 0)
            foreach (var p in ph) res.Add(new(Level.Err, Loc.T("d_ghost", p.Culture, p.Layout)));
        else
            res.Add(new(Level.Ok, Loc.T("d_none")));
        foreach (var h in NotInProfile(prof, live).Except(ph))
            res.Add(new(Level.Info, Loc.T("d_hidden", h.Culture, h.Layout)));

        foreach (var p in prof.Where(p => p.Klid.Length > 0))
            if (!pre.Contains(p.Klid.ToUpperInvariant()))
                res.Add(new(Level.Info, Loc.T("d_desync_a", p.Layout, p.Klid)));
        foreach (var q in pre)
            if (!prof.Any(p => p.Klid.Equals(q, StringComparison.OrdinalIgnoreCase)))
                res.Add(new(Level.Info, Loc.T("d_desync_b", q, LayoutText(q))));
        return res;
    }

    // ---------------------------------------------------------------- backup -----

    public string SaveBackup()
    {
        Directory.CreateDirectory(BackupDir);
        var file = Path.Combine(BackupDir, $"layouts_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        var entries = GetProfile().GroupBy(p => p.Tag)
            .Select(g => new BackupEntry(g.Key, g.Where(p => p.Tip.Length > 0).Select(p => p.Tip).ToArray()));
        File.WriteAllText(file, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        return file;
    }

    public static string? LatestBackup() =>
        Directory.Exists(BackupDir)
            ? new DirectoryInfo(BackupDir).GetFiles("*.json").OrderByDescending(f => f.LastWriteTime).FirstOrDefault()?.FullName
            : null;

    // ---------------------------------------------------------------- changes ----

    private static string TipFor(int langId, string klid) => $"0x{langId:X4}:0x{klid}";

    private static void Apply(string tip, uint flags)
    {
        if (!Native.InstallLayoutOrTip(tip, flags))
            throw new InvalidOperationException($"InstallLayoutOrTip({tip}, {flags}) = FALSE");
    }

    private bool Present(string klid) =>
        GetProfile().Any(p => p.Klid.Equals(klid, StringComparison.OrdinalIgnoreCase)) ||
        GetLive().Any(l => l.Klid.Equals(klid, StringComparison.OrdinalIgnoreCase));

    /// <summary>Add the phantom as a real profile entry, then remove it. Verified, up to 3 removals.</summary>
    public async Task FixAsync(LiveLayout phantom, Action<string, Level> log)
    {
        var file = SaveBackup();
        log(Loc.T("f_backup", file), Level.Info);

        var tip = TipFor(phantom.LangId, phantom.Klid);
        var name = $"{phantom.Culture} ({phantom.Layout})";
        try
        {
            log(Loc.T("f_step1", name), Level.Info);
            Apply(tip, 0);
            await Task.Delay(800);

            log(Loc.T("f_step2", name), Level.Info);
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                Apply(tip, Native.ILOT_UNINSTALL);
                await Task.Delay(1000);
                if (!Present(phantom.Klid)) { log(Loc.T("f_done", name), Level.Ok); return; }
                log(Loc.T("f_retry", name, attempt), Level.Warn);
            }
            log(Loc.T("f_fail", name), Level.Err);
        }
        catch (InvalidOperationException ex)
        {
            log(Loc.T("f_apifail", ex.Message), Level.Err);
        }
    }

    /// <summary>Make the profile layouts match a backup: add missing ones first, then remove extra ones.</summary>
    public void Restore(string file, Action<string, Level> log)
    {
        var entries = JsonSerializer.Deserialize<List<BackupEntry>>(File.ReadAllText(file)) ?? new();
        // old PowerShell backups used "Tag"/"Tips" too, so both formats load
        var wanted = entries.SelectMany(e => e.Tips).Select(NormalizeTip).ToHashSet();
        var current = GetProfile().Where(p => p.Tip.Length > 0).Select(p => NormalizeTip(p.Tip)).ToHashSet();

        foreach (var tip in wanted.Except(current))
        {
            Apply(ToApiTip(tip), 0);
            log(Loc.T("r_added", tip), Level.Ok);
        }
        foreach (var tip in current.Except(wanted))
        {
            if (wanted.Count == 0) break;   // never leave the user without any layout
            Apply(ToApiTip(tip), Native.ILOT_UNINSTALL);
            log(Loc.T("r_removed", tip), Level.Ok);
        }
    }

    private static string NormalizeTip(string tip) => tip.ToUpperInvariant();

    private static string ToApiTip(string tip)
    {
        var parts = tip.Split(':');
        if (parts.Length != 2) return tip;
        // keyboard layouts are "0x0409:0x00000409"; text services are "0x0411:{CLSID}{ProfileGUID}"
        return parts[1].StartsWith('{') ? $"0x{parts[0]}:{parts[1]}" : $"0x{parts[0]}:0x{parts[1]}";
    }
}
