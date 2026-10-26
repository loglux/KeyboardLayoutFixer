using System.Drawing;

namespace KeyboardLayoutFixer;

internal sealed class MainForm : Form
{
    private static readonly Color Bg = ColorTranslator.FromHtml("#1E1F26");
    private static readonly Color Panel = ColorTranslator.FromHtml("#272933");
    private static readonly Color Fg = ColorTranslator.FromHtml("#E6E8EF");
    private static readonly Color Dim = ColorTranslator.FromHtml("#8B90A3");
    private static readonly Color Accent = ColorTranslator.FromHtml("#4C8DFF");
    private static readonly Color Ok = ColorTranslator.FromHtml("#4ADE80");
    private static readonly Color Warn = ColorTranslator.FromHtml("#FBBF24");
    private static readonly Color Err = ColorTranslator.FromHtml("#F87171");
    private static readonly Color Info = ColorTranslator.FromHtml("#7DD3FC");

    private readonly LayoutService _svc = new();
    private readonly string? _screenshot;
    private List<LiveLayout> _phantoms = new();

    private readonly Label _sub = Caption(24, 48, Dim, 9.5f, FontStyle.Regular);
    private readonly Label _phLbl = new();
    private readonly Button _bScan, _bFix, _bRestore, _bLang;
    private readonly Label _c1 = Caption(20, 126, Dim, 9f), _c2 = Caption(20, 268, Dim, 9f), _c3 = Caption(20, 410, Dim, 9f);
    private readonly Panel _hdrProfile, _hdrLive;
    private readonly ListView _lvProfile, _lvLive;
    private readonly RichTextBox _log = new();
    private readonly Label _status = Caption(20, 690, Dim, 9.5f, FontStyle.Regular);

    public MainForm(string? screenshot)
    {
        _screenshot = screenshot;
        Text = "Keyboard Layout Fixer";
        AutoScaleMode = AutoScaleMode.None;   // sizes are scaled explicitly below (fonts are in points and follow DPI by themselves)
        ClientSize = new Size(924, 721);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg; ForeColor = Fg; Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;

        var title = Caption(20, 12, Fg, 17f);
        title.Text = "⌨  Keyboard Layout Fixer";

        _phLbl.Location = new Point(560, 30); _phLbl.Size = new Size(250, 30); _phLbl.TextAlign = ContentAlignment.MiddleRight;
        _phLbl.Font = new Font("Segoe UI Semibold", 11f); _phLbl.ForeColor = Dim;

        _bLang = MakeButton(830, 20, 70, 30, "#475569");
        _bLang.Visible = Loc.Languages.Count > 1;   // nothing to switch to with a single language
        _bScan = MakeButton(20, 74, 170, 40, "#4C8DFF");
        _bFix = MakeButton(200, 74, 170, 40, "#16A34A");
        _bRestore = MakeButton(380, 74, 150, 40, "#6D28D9");

        _hdrProfile = MakeHeader(148, new[] { 90, 220, 330, 240 });
        _lvProfile = MakeList(172, new[] { 90, 220, 330, 240 });
        _hdrLive = MakeHeader(290, new[] { 90, 330, 220, 240 });
        _lvLive = MakeList(314, new[] { 90, 330, 220, 240 });

        _log.Location = new Point(20, 430); _log.Size = new Size(880, 250); _log.ReadOnly = true;
        _log.BackColor = ColorTranslator.FromHtml("#14151A"); _log.BorderStyle = BorderStyle.None; _log.Font = new Font("Consolas", 10f);

        Controls.AddRange(new Control[] { title, _sub, _phLbl, _bLang, _bScan, _bFix, _bRestore,
            _c1, _hdrProfile, _lvProfile, _c2, _hdrLive, _lvLive, _c3, _log, _status });
        ApplyLanguage();
        ScaleToDpi();

        _bScan.Click += async (_, _) => await RunAsync(() => { Scan(); return Task.CompletedTask; });
        _bFix.Click += async (_, _) => await RunAsync(FixAllAsync);
        _bRestore.Click += async (_, _) => await RunAsync(() => { RestoreLatest(); return Task.CompletedTask; });
        _bLang.Click += (_, _) => { Loc.Next(); ApplyLanguage(); UpdateViews(); };

        Shown += async (_, _) =>
        {
            Log(Loc.T("started"), Level.Info);
            await RunAsync(() => { Scan(); return Task.CompletedTask; });
            if (_screenshot != null) { await Task.Delay(600); SaveScreenshot(_screenshot); Close(); }
        };
    }

    private void ScaleToDpi()
    {
        float k = DeviceDpi / 96f;
        if (Math.Abs(k - 1f) < 0.01f) return;
        Scale(new SizeF(k, k));
        foreach (var lv in new[] { _lvProfile, _lvLive })
            foreach (ColumnHeader c in lv.Columns) c.Width = (int)Math.Round(c.Width * k);
    }

    // ---------------------------------------------------------------- actions ----

    private async Task RunAsync(Func<Task> body)
    {
        Cursor = Cursors.WaitCursor;
        _bScan.Enabled = _bFix.Enabled = _bRestore.Enabled = false;
        try { await body(); }
        catch (Exception ex) { Log(Loc.T("error", ex.Message), Level.Err); }
        finally
        {
            _bScan.Enabled = _bFix.Enabled = _bRestore.Enabled = true;
            Cursor = Cursors.Default;
        }
        UpdateViews();
    }

    private void Scan()
    {
        Log(Loc.T("s_scanning"), Level.Info);
        _phantoms = LayoutService.FindPhantoms(_svc.GetProfile(), _svc.GetLive());
        UpdateViews();
        var found = _svc.Diagnose();
        foreach (var f in found) Log(f.Text, f.Level);
        Log(found.Any(f => f.Level == Level.Err) ? Loc.T("s_problem") : Loc.T("s_clean"),
            found.Any(f => f.Level == Level.Err) ? Level.Err : Level.Ok);
    }

    private async Task FixAllAsync()
    {
        if (_phantoms.Count == 0) { Log(Loc.T("f_autonone"), Level.Ok); return; }
        foreach (var p in _phantoms.ToList())   // every detected phantom in one go
        {
            Log(Loc.T("f_fixing", $"{p.Culture} ({p.Layout})"), Level.Info);
            await _svc.FixAsync(p, Log);
        }
        Scan();
    }

    private void RestoreLatest()
    {
        var last = LayoutService.LatestBackup();
        if (last == null) { Log(Loc.T("r_none"), Level.Warn); return; }
        Log(Loc.T("r_restoring", Path.GetFileName(last)), Level.Info);
        _svc.Restore(last, Log);
        Log(Loc.T("r_done"), Level.Ok);
    }

    // ---------------------------------------------------------------- views ------

    private void UpdateViews()
    {
        var prof = _svc.GetProfile();
        var live = _svc.GetLive();
        var pre = _svc.GetPreload();
        var ph = LayoutService.FindPhantoms(prof, live);

        _lvProfile.Items.Clear(); _lvLive.Items.Clear();
        foreach (var p in prof)
            _lvProfile.Items.Add(new ListViewItem(new[] { p.Tag, p.Language, p.Layout, p.Tip }));
        foreach (var l in live)
        {
            var it = new ListViewItem(new[] { l.Culture, l.Layout, l.Klid, pre.Contains(l.Klid) ? Loc.T("yes") : Loc.T("no") });
            if (ph.Any(x => x.Klid == l.Klid)) it.ForeColor = Err;
            _lvLive.Items.Add(it);
        }

        if (ph.Count > 0)
        {
            _phLbl.Text = Loc.T("st_found", string.Join(", ", ph.Select(p => p.Culture).Distinct().Order()));
            _phLbl.ForeColor = Err;
        }
        else { _phLbl.Text = Loc.T("st_none"); _phLbl.ForeColor = Ok; }
        _status.Text = Loc.T("status", prof.Count, live.Count, pre.Count);
    }

    private void ApplyLanguage()
    {
        _sub.Text = Loc.T("subtitle");
        _bScan.Text = Loc.T("btn_scan"); _bFix.Text = Loc.T("btn_fix"); _bRestore.Text = Loc.T("btn_restore"); _bLang.Text = Loc.NextCode;
        _c1.Text = Loc.T("cap_profile"); _c2.Text = Loc.T("cap_live"); _c3.Text = Loc.T("cap_log");
        var p = new[] { "col_tag", "col_lang", "col_layout", "col_code" };
        var l = new[] { "col_culture", "col_layout", "", "col_inpreload" };
        for (int i = 0; i < 4; i++)
        {
            _hdrProfile.Controls[i].Text = Loc.T(p[i]);
            _hdrLive.Controls[i].Text = l[i].Length == 0 ? "KLID" : Loc.T(l[i]);
        }
    }

    private void Log(string msg, Level level)
    {
        var (icon, color) = level switch
        {
            Level.Ok => ("✔", Ok), Level.Warn => ("⚠", Warn), Level.Err => ("✖", Err), _ => ("ℹ", Info),
        };
        _log.SelectionStart = _log.TextLength; _log.SelectionColor = Dim;
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  ");
        _log.SelectionColor = color;
        _log.AppendText($"{icon}  {msg}\r\n");
        _log.ScrollToCaret();
    }

    private void SaveScreenshot(string path)
    {
        Activate(); _lvProfile.Refresh(); _lvLive.Refresh(); _log.Refresh(); Application.DoEvents();
        using var bmp = new Bitmap(Width, Height);
        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            Native.PrintWindow(Handle, hdc, 2);   // PW_RENDERFULLCONTENT
            g.ReleaseHdc(hdc);
        }
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    // ---------------------------------------------------------------- controls ---

    private static Label Caption(int x, int y, Color color, float size, FontStyle style = FontStyle.Bold)
    {
        var l = new Label { Location = new Point(x, y), AutoSize = true, ForeColor = color, UseMnemonic = false };
        l.Font = new Font(style == FontStyle.Regular ? "Segoe UI" : "Segoe UI Semibold", size);
        return l;
    }

    private static Button MakeButton(int x, int y, int w, int h, string color)
    {
        var b = new Button
        {
            Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
            BackColor = ColorTranslator.FromHtml(color), ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 9.5f), Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    private static ListView MakeList(int y, int[] widths)
    {
        var lv = new ListView
        {
            Location = new Point(20, y), Size = new Size(880, 88), View = View.Details, FullRowSelect = true,
            BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.None, BackColor = Panel, ForeColor = Fg,
            Font = new Font("Segoe UI", 9.5f),
        };
        foreach (var w in widths) lv.Columns.Add("", w);
        return lv;
    }

    // Dark header strip above a list (the native ListView header cannot be themed reliably)
    private static Panel MakeHeader(int y, int[] widths)
    {
        var panel = new Panel { Location = new Point(20, y), Size = new Size(880, 24), BackColor = ColorTranslator.FromHtml("#32343F") };
        int x = 0;
        foreach (var w in widths)
        {
            panel.Controls.Add(new Label
            {
                Location = new Point(x + 6, 0), Size = new Size(w - 6, 24), TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Fg, Font = new Font("Segoe UI Semibold", 9f), UseMnemonic = false,
            });
            x += w;
        }
        return panel;
    }
}
