using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace KeyboardLayoutFixer;

/// <summary>
/// UI texts live in lang\*.json. English and Russian are embedded in the EXE; any lang\xx.json next to the EXE
/// is picked up too (and wins over an embedded file of the same name), so a new language needs no rebuild:
/// copy lang\template.json to xx.json and fill in the values. An empty or missing value falls back to English.
/// </summary>
internal static class Loc
{
    private static readonly Dictionary<string, Dictionary<string, string>> Tables = new();
    private static Dictionary<string, string> _en = new();
    private static Dictionary<string, string> _cur = new();
    private static string _lang = "en";

    static Loc()
    {
        // embedded languages first, then the external folder (overrides / adds)
        var asm = Assembly.GetExecutingAssembly();
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith("lang.") && n.EndsWith(".json")))
        {
            using var s = asm.GetManifestResourceStream(res)!;
            Add(res[5..^5], s);
        }
        var dir = Path.Combine(AppContext.BaseDirectory, "lang");
        if (Directory.Exists(dir))
            foreach (var f in Directory.GetFiles(dir, "*.json"))
            {
                var code = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                if (code == "template") continue;
                try { using var s = File.OpenRead(f); Add(code, s); } catch { /* a broken translation must not break the app */ }
            }
        _en = Tables.GetValueOrDefault("en") ?? new();
        var ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        Lang = Tables.ContainsKey(ui) ? ui : "en";
    }

    private static void Add(string code, Stream s)
    {
        var d = JsonSerializer.Deserialize<Dictionary<string, string>>(s);
        if (d != null) Tables[code] = d;
    }

    public static string Lang
    {
        get => _lang;
        set { if (Tables.ContainsKey(value)) { _lang = value; _cur = Tables[value]; } }
    }

    public static IReadOnlyList<string> Languages => Tables.Keys.OrderBy(k => k == "en" ? "" : k).ToList();

    /// <summary>Switches to the next available language (the button cycles through all of them).</summary>
    public static void Next()
    {
        var all = Languages;
        Lang = all[(all.ToList().IndexOf(_lang) + 1) % all.Count];
    }

    /// <summary>Code of the language the switch button will select next, e.g. "RU".</summary>
    public static string NextCode
    {
        get { var all = Languages; return all[(all.ToList().IndexOf(_lang) + 1) % all.Count].ToUpperInvariant(); }
    }

    public static string T(string key, params object[] args)
    {
        var s = _cur.GetValueOrDefault(key);
        if (string.IsNullOrEmpty(s)) s = _en.GetValueOrDefault(key);
        if (string.IsNullOrEmpty(s)) s = key;
        return args.Length > 0 ? string.Format(s, args) : s;
    }
}
