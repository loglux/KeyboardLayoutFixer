#Requires -Version 5.1
<#
.SYNOPSIS
    Keyboard Layout Fixer - fixes the "stuck" phantom keyboard layout (en-US etc.) on Windows 10/11.
    Лечит «прилипшую» раскладку клавиатуры (en-US и т.п.) в Windows 10/11.
.DESCRIPTION
    PowerShell + WinForms. Shows the profile language list, the layouts actually loaded in the
    session and the HKCU Preload entries, detects the mismatch and fixes it with the
    "add language -> apply -> remove -> apply" trick. A backup is made before every change.
.PARAMETER Lang
    UI language: ru or en. Default: taken from the Windows UI culture. Can be toggled in the window.
.PARAMETER NoGui
    Console diagnostics only, no changes.
.PARAMETER Fix
    Console mode: scan, back up, fix phantom layouts (auto-detected, or the language given by -Tag), scan again. Run in Windows PowerShell 5.1.
.PARAMETER Screenshot
    Scan, save a PNG of the window to the given path and exit (read-only; used for docs).
#>
param(
    [ValidateSet('ru', 'en')][string]$Lang,
    [switch]$NoGui,
    [switch]$Fix,
    [string]$Tag,
    [string]$Screenshot
)

# Get-/Set-WinUserLanguageList need Windows PowerShell 5.1 (pwsh 7 returns deserialized objects without methods).
if (-not $NoGui -and -not $Fix -and ($PSVersionTable.PSEdition -eq 'Core' -or [Threading.Thread]::CurrentThread.ApartmentState -ne 'STA')) {
    $relaunch = @('-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($Lang) { $relaunch += @('-Lang', $Lang) }
    if ($Screenshot) { $relaunch += @('-Screenshot', "`"$Screenshot`"") }
    Start-Process powershell.exe -WindowStyle Hidden -ArgumentList $relaunch
    return
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Namespace Kbd -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern int GetKeyboardLayoutList(int nBuff, IntPtr[] lpList);
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
'@

$script:BackupDir = Join-Path $PSScriptRoot 'backups'
$script:LayoutTable = $null

# ------------------------------------------------------------------ i18n -----

$script:Lang = if ($Lang) { $Lang } elseif ((Get-UICulture).TwoLetterISOLanguageName -eq 'ru') { 'ru' } else { 'en' }

$script:S = @{
    ru = @{
        subtitle      = 'Лечит «прилипшую» раскладку-призрак (обычно en-US) в списке Win+Space'
        target        = 'Язык-призрак:'
        btn_scan      = 'Сканировать'
        btn_fix       = 'Исправить'
        btn_restore   = '↩  Откатить'
        btn_lang      = 'EN'
        cap_profile   = 'ЯЗЫКИ ПРОФИЛЯ (Параметры → Время и язык)'
        cap_live      = 'ЖИВЫЕ РАСКЛАДКИ СЕССИИ (то, что переключает Win+Space)'
        cap_log       = 'ЖУРНАЛ'
        col_tag       = 'Тег'; col_lang = 'Язык'; col_layout = 'Раскладка'; col_code = 'Код'
        col_culture   = 'Культура'; col_inpreload = 'В Preload'
        yes           = 'да'; no = 'нет'
        status        = 'Языков в профиле: {0}   •   живых раскладок: {1}   •   Preload: {2}'
        unknown       = '(неизвестная раскладка)'
        nolayout      = '(без раскладки)'
        undetermined  = '(не определена)'
        d_summary     = 'Профиль: {0} раскладок, живых в сессии: {1}, записей Preload: {2}'
        d_inprofile   = 'Язык {0} присутствует в профиле ({1}).'
        d_notinprof   = 'Язык {0} в списке языков профиля отсутствует.'
        d_ghost       = 'ПРИЗРАК: раскладка {0} ({1}) активна в сессии, хотя в списке языков её нет. Это и есть «прилипшая» раскладка.'
        d_loaded      = 'Раскладка {0} загружена в сессии.'
        d_notloaded   = 'В сессии раскладка {0} не загружена.'
        d_preloadhas  = 'В HKCU\Keyboard Layout\Preload есть запись: {0}.'
        d_desync_a    = 'К сведению: раскладка «{0}» ({1}) есть в профиле, но не записана в Preload. Это не призрак.'
        d_desync_b    = 'К сведению: Preload содержит {0} («{1}»), а в профиле такой раскладки нет. Это не призрак.'
        f_backup      = 'Бэкап сохранён: {0}'
        f_step1       = 'Шаг 1/2: временно добавляю {0}, чтобы Windows пересобрала список…'
        f_nothing     = 'Нечего удалять: {0} в списке нет.'
        f_only        = 'Отказ: {0} — единственный язык, удалять нельзя.'
        f_step2       = 'Шаг 2/2: удаляю {0} из профиля…'
        f_done        = 'Готово: {0} убран, остальные языки сохранены.'
        f_retry       = 'Windows вернула {0}, повторяю удаление (попытка {1})…'
        f_fail        = 'Не удалось убрать {0}. Удалите его вручную в Параметрах или перезайдите в систему.'
        f_fixing      = 'Исправляю {0} (цикл «добавить → удалить»)…'
        f_removing    = 'Удаляю {0} из профиля без цикла…'
        s_scanning    = 'Сканирую раскладки…'
        st_none       = '✔ Призраков нет'
        st_found      = '✖ Призрак: {0}'
        d_none        = 'Призрачных раскладок нет: всё, что загружено в сессии, есть в профиле.'
        f_autonone    = 'Исправлять нечего: призраков нет.'
        s_problem     = 'Найдена проблема. Нажмите «Исправить».'
        s_clean       = 'Критичных проблем не найдено.'
        r_none        = 'Бэкапов пока нет.'
        r_restoring   = 'Восстанавливаю из {0}…'
        r_done        = 'Список языков восстановлен.'
        error         = 'Ошибка: {0}'
        started       = 'Keyboard Layout Fixer запущен. Бэкап создаётся перед каждым изменением.'
    }
    en = @{
        subtitle      = 'Fixes the "stuck" phantom layout (usually en-US) in the Win+Space list'
        target        = 'Phantom language:'
        btn_scan      = 'Scan'
        btn_fix       = 'Fix'
        btn_restore   = '↩  Restore'
        btn_lang      = 'RU'
        cap_profile   = 'PROFILE LANGUAGES (Settings → Time & language)'
        cap_live      = 'LIVE SESSION LAYOUTS (what Win+Space switches between)'
        cap_log       = 'LOG'
        col_tag       = 'Tag'; col_lang = 'Language'; col_layout = 'Layout'; col_code = 'Code'
        col_culture   = 'Culture'; col_inpreload = 'In Preload'
        yes           = 'yes'; no = 'no'
        status        = 'Profile languages: {0}   •   live layouts: {1}   •   Preload: {2}'
        unknown       = '(unknown layout)'
        nolayout      = '(no layout)'
        undetermined  = '(undetermined)'
        d_summary     = 'Profile: {0} layouts, live in session: {1}, Preload entries: {2}'
        d_inprofile   = 'Language {0} is present in the profile ({1}).'
        d_notinprof   = 'Language {0} is not in the profile language list.'
        d_ghost       = 'PHANTOM: layout {0} ({1}) is active in the session although it is not in the language list. This is the "stuck" layout.'
        d_loaded      = 'Layout {0} is loaded in the session.'
        d_notloaded   = 'Layout {0} is not loaded in the session.'
        d_preloadhas  = 'HKCU\Keyboard Layout\Preload contains an entry: {0}.'
        d_desync_a    = 'FYI: layout "{0}" ({1}) is in the profile but not listed in Preload. This is not the phantom.'
        d_desync_b    = 'FYI: Preload contains {0} ("{1}") but the profile has no such layout. This is not the phantom.'
        f_backup      = 'Backup saved: {0}'
        f_step1       = 'Step 1/2: temporarily adding {0} so Windows rebuilds the list…'
        f_nothing     = 'Nothing to remove: {0} is not in the list.'
        f_only        = 'Refused: {0} is the only language, it cannot be removed.'
        f_step2       = 'Step 2/2: removing {0} from the profile…'
        f_done        = 'Done: {0} removed, other languages preserved.'
        f_retry       = 'Windows brought {0} back, removing again (attempt {1})…'
        f_fail        = 'Could not remove {0}. Remove it manually in Settings or sign out and back in.'
        f_fixing      = 'Fixing {0} (add → remove cycle)…'
        f_removing    = 'Removing {0} from the profile (no cycle)…'
        s_scanning    = 'Scanning layouts…'
        st_none       = '✔ No phantom layouts'
        st_found      = '✖ Phantom: {0}'
        d_none        = 'No phantom layouts: everything loaded in the session is in the profile.'
        f_autonone    = 'Nothing to fix: no phantom layouts.'
        s_problem     = 'Problem found. Press "Fix".'
        s_clean       = 'No critical problems found.'
        r_none        = 'No backups yet.'
        r_restoring   = 'Restoring from {0}…'
        r_done        = 'Language list restored.'
        error         = 'Error: {0}'
        started       = 'Keyboard Layout Fixer started. A backup is made before every change.'
    }
}

function T([string]$Key, [object[]]$Args2) {
    $s = $script:S[$script:Lang][$Key]
    if ($Args2) { $s -f $Args2 } else { $s }
}

# ------------------------------------------------------------------ data -----

function Get-LayoutTable {
    if ($script:LayoutTable) { return $script:LayoutTable }
    $root = 'HKLM:\SYSTEM\CurrentControlSet\Control\Keyboard Layouts'
    $script:LayoutTable = foreach ($k in Get-ChildItem $root -ErrorAction SilentlyContinue) {
        $p = Get-ItemProperty $k.PSPath
        [pscustomobject]@{ Klid = $k.PSChildName.ToUpper(); Text = $p.'Layout Text'; LayoutId = $p.'Layout Id' }
    }
    $script:LayoutTable
}

function Get-LayoutText([string]$Klid) {
    $hit = Get-LayoutTable | Where-Object Klid -eq $Klid.ToUpper() | Select-Object -First 1
    if ($hit -and $hit.Text) { $hit.Text } else { T 'unknown' }
}

function Get-LangId([string]$Tag) {
    try { [Globalization.CultureInfo]::GetCultureInfo($Tag).LCID } catch { 0 }
}

# User profile language list (what Settings -> Time & language shows)
function Get-ProfileLayouts {
    $langs = Get-WinUserLanguageList   # the cmdlet returns the list as ONE object - enumerate through a variable
    foreach ($l in $langs) {
        foreach ($tip in $l.InputMethodTips) {
            $klid = ($tip -split ':')[1]
            [pscustomobject]@{ Tag = $l.LanguageTag; Language = $l.LocalizedName; Klid = $klid; Layout = Get-LayoutText $klid; Tip = $tip }
        }
        if (-not $l.InputMethodTips.Count) {
            [pscustomobject]@{ Tag = $l.LanguageTag; Language = $l.LocalizedName; Klid = ''; Layout = (T 'nolayout'); Tip = '' }
        }
    }
}

# Layouts actually loaded in the current session (what Win+Space switches between)
function Get-LiveLayouts {
    $n = [Kbd.Native]::GetKeyboardLayoutList(0, $null)
    $buf = New-Object IntPtr[] $n
    [void][Kbd.Native]::GetKeyboardLayoutList($n, $buf)
    foreach ($h in $buf) {
        $v = $h.ToInt64()
        $lang = [int]($v -band 0xFFFF)
        $hi = [int](($v -shr 16) -band 0xFFFF)
        if (($hi -band 0xF000) -eq 0xF000) {
            $id = '{0:X4}' -f ($hi -band 0x0FFF)
            $hit = Get-LayoutTable | Where-Object { $_.LayoutId -eq $id -and $_.Klid.EndsWith('{0:X4}' -f $lang) } | Select-Object -First 1
            $klid = if ($hit) { $hit.Klid } else { '' }
        } elseif ($hi -eq $lang) { $klid = '0000{0:X4}' -f $lang }
        else { $klid = '{0:X4}{1:X4}' -f $hi, $lang }
        $cult = try { [Globalization.CultureInfo]::GetCultureInfo($lang).Name } catch { '?' }
        [pscustomobject]@{ LangId = $lang; Culture = $cult; Klid = $klid; Layout = $(if ($klid) { Get-LayoutText $klid } else { T 'undetermined' }) }
    }
}

# Phantom = layout loaded in the session whose KLID is not in the profile language list
function Get-Phantoms($Prof = @(Get-ProfileLayouts), $Live = @(Get-LiveLayouts)) {
    $known = @($Prof | Where-Object Klid | ForEach-Object { $_.Klid.ToUpper() })
    $Live | Where-Object { $_.Klid -and ($known -notcontains $_.Klid.ToUpper()) }
}

function Get-Preload {
    $p = Get-ItemProperty 'HKCU:\Keyboard Layout\Preload' -ErrorAction SilentlyContinue
    if (-not $p) { return @() }
    $p.PSObject.Properties | Where-Object { $_.Name -match '^\d+$' } | Sort-Object { [int]$_.Name } | ForEach-Object { $_.Value.ToUpper() }
}

# ------------------------------------------------------------------ backup ---

function Save-Backup {
    New-Item -ItemType Directory -Force -Path $script:BackupDir | Out-Null
    $file = Join-Path $script:BackupDir ('layouts_{0:yyyyMMdd_HHmmss}.json' -f (Get-Date))
    $langs = Get-WinUserLanguageList
    # ConvertTo-Json on a pipeline of ONE object emits an object, not an array - force an array
    ConvertTo-Json -InputObject @($langs | ForEach-Object { [pscustomobject]@{ Tag = $_.LanguageTag; Tips = @($_.InputMethodTips) } }) -Depth 4 |
        Set-Content -Path $file -Encoding UTF8
    $file
}

function Restore-Backup([string]$File) {
    $data = @(Get-Content $File -Raw | ConvertFrom-Json)
    $list = New-WinUserLanguageList -Language $data[0].Tag
    foreach ($d in $data | Select-Object -Skip 1) { $list.Add($d.Tag) }
    foreach ($item in $list) {
        $src = $data | Where-Object Tag -eq $item.LanguageTag
        if ($src) { $item.InputMethodTips.Clear(); foreach ($t in @($src.Tips)) { $item.InputMethodTips.Add($t) } }
    }
    Set-WinUserLanguageList $list -Force
}

# ------------------------------------------------------------------ logic ----

# Returns findings: @{Level=ok|warn|err|info; Text=...}
function Invoke-Diagnose([string]$Tag) {
    $res = New-Object System.Collections.Generic.List[object]
    $add = { param($l, $t) $res.Add([pscustomobject]@{ Level = $l; Text = $t }) }

    $prof = @(Get-ProfileLayouts)
    $live = @(Get-LiveLayouts)
    $pre = @(Get-Preload)

    & $add 'info' (T 'd_summary' @($prof.Count, $live.Count, $pre.Count))

    $phantoms = @(Get-Phantoms $prof $live)
    if ($phantoms) { foreach ($p in $phantoms) { & $add 'err' (T 'd_ghost' @($p.Culture, $p.Layout)) } }
    else { & $add 'ok' (T 'd_none') }

    if ($Tag) {
        $inProfile = @($prof | Where-Object Tag -like "$Tag*")
        if ($inProfile) { & $add 'warn' (T 'd_inprofile' @($Tag, ($inProfile.Layout -join ', '))) }
    }

    # FYI only: profile layout missing from Preload, or Preload entry missing from the profile
    foreach ($p in $prof | Where-Object Klid) {
        if ($pre -notcontains $p.Klid.ToUpper()) { & $add 'info' (T 'd_desync_a' @($p.Layout, $p.Klid)) }
    }
    foreach ($q in $pre) {
        if (-not ($prof | Where-Object { $_.Klid.ToUpper() -eq $q })) { & $add 'info' (T 'd_desync_b' @($q, (Get-LayoutText $q))) }
    }
    $res
}

function Invoke-Fix([string]$Tag, [bool]$Cycle) {
    $file = Save-Backup
    Write-Log (T 'f_backup' @($file)) 'info'

    $list = Get-WinUserLanguageList
    if ($Cycle -and -not ($list | Where-Object LanguageTag -like $Tag)) {
        Write-Log (T 'f_step1' @($Tag)) 'info'
        $list.Add($Tag)
        Set-WinUserLanguageList $list -Force -WarningAction SilentlyContinue
    }

    $list = Get-WinUserLanguageList
    $rm = @($list | Where-Object LanguageTag -like $Tag)
    if (-not $rm) { Write-Log (T 'f_nothing' @($Tag)) 'warn'; return }
    if ($rm.Count -ge $list.Count) { Write-Log (T 'f_only' @($Tag)) 'err'; return }

    Write-Log (T 'f_step2' @($Tag)) 'info'
    # Windows sometimes re-adds an empty entry right after the removal, so verify and retry
    for ($try = 1; $try -le 3; $try++) {
        foreach ($i in $rm) { [void]$list.Remove($i) }
        Set-WinUserLanguageList $list -Force -WarningAction SilentlyContinue
        Start-Sleep -Milliseconds 1500
        $list = Get-WinUserLanguageList
        $rm = @($list | Where-Object LanguageTag -like $Tag)
        if (-not $rm) { break }
        Write-Log (T 'f_retry' @($Tag, $try)) 'warn'
    }
    if ($rm) { Write-Log (T 'f_fail' @($Tag)) 'err'; return }
    Write-Log (T 'f_done' @($Tag)) 'ok'
}

# ------------------------------------------------------------------ GUI ------

function New-Color($hex) { [Drawing.ColorTranslator]::FromHtml($hex) }
$C = @{
    Bg = New-Color '#1E1F26'; Panel = New-Color '#272933'; Text = New-Color '#E6E8EF'; Dim = New-Color '#8B90A3'
    Accent = New-Color '#4C8DFF'; Ok = New-Color '#4ADE80'; Warn = New-Color '#FBBF24'; Err = New-Color '#F87171'; Info = New-Color '#7DD3FC'
}

function New-Button($x, $w, $color) {
    $b = New-Object Windows.Forms.Button
    $b.Location = [Drawing.Point]::new($x, 74); $b.Size = [Drawing.Size]::new($w, 40)
    $b.FlatStyle = 'Flat'; $b.FlatAppearance.BorderSize = 0; $b.BackColor = $color; $b.ForeColor = [Drawing.Color]::White
    $b.Font = New-Object Drawing.Font('Segoe UI Semibold', 9.5); $b.Cursor = 'Hand'
    $b
}

function New-List($y, $h, $widths) {
    $lv = New-Object Windows.Forms.ListView
    $lv.Location = [Drawing.Point]::new(20, $y); $lv.Size = [Drawing.Size]::new(880, $h)
    $lv.View = 'Details'; $lv.FullRowSelect = $true; $lv.BorderStyle = 'None'; $lv.HeaderStyle = 'None'
    $lv.BackColor = $C.Panel; $lv.ForeColor = $C.Text; $lv.Font = New-Object Drawing.Font('Segoe UI', 9.5)
    foreach ($w in $widths) { [void]$lv.Columns.Add('', $w) }
    $lv
}

# Dark header strip above a list (the native ListView header cannot be themed reliably)
function New-Header($y, $widths) {
    $panel = New-Object Windows.Forms.Panel
    $panel.Location = [Drawing.Point]::new(20, $y); $panel.Size = [Drawing.Size]::new(880, 24); $panel.BackColor = New-Color '#32343F'
    $x = 0
    foreach ($w in $widths) {
        $l = New-Object Windows.Forms.Label
        $l.Location = [Drawing.Point]::new($x + 6, 0); $l.Size = [Drawing.Size]::new($w - 6, 24); $l.TextAlign = 'MiddleLeft'
        $l.ForeColor = $C.Text; $l.Font = New-Object Drawing.Font('Segoe UI Semibold', 9); $l.UseMnemonic = $false
        $panel.Controls.Add($l); $x += $w
    }
    $panel
}

function New-Caption($y) {
    $l = New-Object Windows.Forms.Label
    $l.Location = [Drawing.Point]::new(20, $y); $l.AutoSize = $true
    $l.ForeColor = $C.Dim; $l.Font = New-Object Drawing.Font('Segoe UI Semibold', 9); $l.UseMnemonic = $false
    $l
}

function Write-Log([string]$Msg, [string]$Level = 'info') {
    if (-not $script:Log) {   # console mode (-Fix)
        $cc = @{ ok = 'Green'; warn = 'Yellow'; err = 'Red'; info = 'Cyan' }
        Write-Host ('{0:HH:mm:ss}  {1}' -f (Get-Date), $Msg) -ForegroundColor $cc[$Level]; return
    }
    $map = @{ ok = @('✔', $C.Ok); warn = @('⚠', $C.Warn); err = @('✖', $C.Err); info = @('ℹ', $C.Info) }
    $icon, $color = $map[$Level]
    $script:Log.SelectionStart = $script:Log.TextLength
    $script:Log.SelectionColor = $C.Dim
    $script:Log.AppendText(('{0:HH:mm:ss}  ' -f (Get-Date)))
    $script:Log.SelectionColor = $color
    $script:Log.AppendText("$icon  $Msg`r`n")
    $script:Log.ScrollToCaret()
    [Windows.Forms.Application]::DoEvents()
}

# Re-applies all static texts for the current language
function Set-UiLanguage {
    $script:Sub.Text = T 'subtitle'
    $script:Lbl.Text = T 'target'
    $script:BScan.Text = T 'btn_scan'; $script:BFix.Text = T 'btn_fix'; $script:BRes.Text = T 'btn_restore'
    $script:BLang.Text = T 'btn_lang'
    $script:C1.Text = T 'cap_profile'; $script:C2.Text = T 'cap_live'; $script:C3.Text = T 'cap_log'
    $p = 'col_tag', 'col_lang', 'col_layout', 'col_code'; $l = 'col_culture', 'col_layout', 'col_klid', 'col_inpreload'
    for ($i = 0; $i -lt 4; $i++) {
        $script:HdrProfile.Controls[$i].Text = T $p[$i]
        $script:HdrLive.Controls[$i].Text = if ($l[$i] -eq 'col_klid') { 'KLID' } else { T $l[$i] }
    }
}

function Update-Views {
    $script:LvProfile.Items.Clear(); $script:LvLive.Items.Clear()
    $tag = $script:Target
    $prof = @(Get-ProfileLayouts)
    $live = @(Get-LiveLayouts)
    $pre = @(Get-Preload)
    $phantoms = @(Get-Phantoms $prof $live)

    foreach ($p in $prof) {
        $it = New-Object Windows.Forms.ListViewItem($p.Tag)
        [void]$it.SubItems.Add($p.Language); [void]$it.SubItems.Add($p.Layout); [void]$it.SubItems.Add($p.Tip)
        if ($tag -and $p.Tag -like "$tag*") { $it.ForeColor = $C.Warn }
        [void]$script:LvProfile.Items.Add($it)
    }
    foreach ($l in $live) {
        $it = New-Object Windows.Forms.ListViewItem($l.Culture)
        [void]$it.SubItems.Add($l.Layout); [void]$it.SubItems.Add($l.Klid)
        [void]$it.SubItems.Add($(if ($pre -contains $l.Klid) { T 'yes' } else { T 'no' }))
        if ($phantoms | Where-Object { $_.Klid -eq $l.Klid }) { $it.ForeColor = $C.Err }
        [void]$script:LvLive.Items.Add($it)
    }
    if ($phantoms) { $script:PhLbl.Text = T 'st_found' @($phantoms[0].Culture); $script:PhLbl.ForeColor = $C.Err }
    else { $script:PhLbl.Text = T 'st_none'; $script:PhLbl.ForeColor = $C.Ok }
    $script:Status.Text = T 'status' @($script:LvProfile.Items.Count, $live.Count, $pre.Count)
}

function Invoke-Scan {
    Write-Log (T 's_scanning') 'info'
    $ph = @(Get-Phantoms)
    $script:Target = if ($ph) { $ph[0].Culture } else { '' }
    $script:TagBox.Text = if ($script:Target) { $script:Target } else { '—' }
    Update-Views
    $found = Invoke-Diagnose $script:Target
    foreach ($f in $found) { Write-Log $f.Text $f.Level }
    $bad = @($found | Where-Object Level -eq 'err').Count
    if ($bad) { Write-Log (T 's_problem') 'err' } else { Write-Log (T 's_clean') 'ok' }
}

function Invoke-Action([scriptblock]$Body) {
    $script:Form.Cursor = 'WaitCursor'
    try { & $Body } catch { Write-Log (T 'error' @($_.Exception.Message)) 'err' }
    finally { $script:Form.Cursor = 'Default' }
    Update-Views
}

function Show-Gui {
    $f = New-Object Windows.Forms.Form
    $script:Form = $f
    $f.Text = 'Keyboard Layout Fixer'; $f.Size = [Drawing.Size]::new(940, 760); $f.StartPosition = 'CenterScreen'
    $f.BackColor = $C.Bg; $f.ForeColor = $C.Text; $f.Font = New-Object Drawing.Font('Segoe UI', 9.5)
    $f.FormBorderStyle = 'FixedSingle'; $f.MaximizeBox = $false

    $title = New-Object Windows.Forms.Label
    $title.Text = '⌨  Keyboard Layout Fixer'; $title.Location = [Drawing.Point]::new(20, 12); $title.AutoSize = $true
    $title.Font = New-Object Drawing.Font('Segoe UI Semibold', 17); $title.ForeColor = $C.Text
    $script:Sub = New-Object Windows.Forms.Label
    $script:Sub.Location = [Drawing.Point]::new(24, 48); $script:Sub.AutoSize = $true; $script:Sub.ForeColor = $C.Dim

    $script:Lbl = New-Caption 22; $script:Lbl.Location = [Drawing.Point]::new(640, 14)
    $tb = New-Object Windows.Forms.TextBox
    $tb.Text = ''; $tb.Location = [Drawing.Point]::new(640, 36); $tb.Width = 120
    $tb.ReadOnly = $true; $tb.TabStop = $false   # display only: filled in by the scan
    $tb.BackColor = $C.Panel; $tb.ForeColor = $C.Text; $tb.BorderStyle = 'FixedSingle'
    $script:TagBox = $tb

    $script:PhLbl = New-Object Windows.Forms.Label
    $script:PhLbl.Location = [Drawing.Point]::new(640, 64); $script:PhLbl.AutoSize = $true
    $script:PhLbl.Font = New-Object Drawing.Font('Segoe UI Semibold', 9.5); $script:PhLbl.ForeColor = $C.Dim

    $script:BLang = New-Button 830 70 (New-Color '#475569'); $script:BLang.Location = [Drawing.Point]::new(830, 20); $script:BLang.Height = 30

    $script:BScan = New-Button 20 170 $C.Accent
    $script:BFix = New-Button 200 170 (New-Color '#16A34A')
    $script:BRes = New-Button 380 150 (New-Color '#6D28D9')

    $script:C1 = New-Caption 126
    $script:HdrProfile = New-Header 148 @(90, 220, 330, 240)
    $script:LvProfile = New-List 172 88 @(90, 220, 330, 240)
    $script:C2 = New-Caption 268
    $script:HdrLive = New-Header 290 @(90, 330, 220, 240)
    $script:LvLive = New-List 314 88 @(90, 330, 220, 240)
    $script:C3 = New-Caption 410
    $log = New-Object Windows.Forms.RichTextBox
    $log.Location = [Drawing.Point]::new(20, 430); $log.Size = [Drawing.Size]::new(880, 250); $log.ReadOnly = $true
    $log.BackColor = New-Color '#14151A'; $log.BorderStyle = 'None'; $log.Font = New-Object Drawing.Font('Consolas', 10)
    $script:Log = $log

    $st = New-Object Windows.Forms.Label
    $st.Location = [Drawing.Point]::new(20, 690); $st.AutoSize = $true; $st.ForeColor = $C.Dim
    $script:Status = $st

    $script:BScan.Add_Click({ Invoke-Action { Invoke-Scan } })
    $script:BFix.Add_Click({ Invoke-Action {
        $tag = $script:Target
        if (-not $tag) { Write-Log (T 'f_autonone') 'ok'; return }
        Write-Log (T 'f_fixing' @($tag)) 'info'
        Invoke-Fix $tag $true
        Invoke-Scan
    } })
    $script:BRes.Add_Click({ Invoke-Action {
        $last = Get-ChildItem $script:BackupDir -Filter *.json -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $last) { Write-Log (T 'r_none') 'warn'; return }
        Write-Log (T 'r_restoring' @($last.Name)) 'info'
        Restore-Backup $last.FullName
        Write-Log (T 'r_done') 'ok'
    } })
    $script:BLang.Add_Click({
        $script:Lang = if ($script:Lang -eq 'ru') { 'en' } else { 'ru' }
        Set-UiLanguage
        Update-Views
    })

    $f.Controls.AddRange(@($title, $script:Sub, $script:Lbl, $tb, $script:PhLbl, $script:BLang, $script:BScan, $script:BFix, $script:BRes,
            $script:C1, $script:HdrProfile, $script:LvProfile, $script:C2, $script:HdrLive, $script:LvLive, $script:C3, $log, $st))
    Set-UiLanguage

    $f.Add_Shown({
        Write-Log (T 'started') 'info'
        Invoke-Action { Invoke-Scan }
        if ($Screenshot) {
            $script:Form.Activate(); Start-Sleep -Milliseconds 400; [Windows.Forms.Application]::DoEvents()
            $bmp = New-Object Drawing.Bitmap($script:Form.Width, $script:Form.Height)
            $g = [Drawing.Graphics]::FromImage($bmp)
            $hdc = $g.GetHdc()
            [void][Kbd.Native]::PrintWindow($script:Form.Handle, $hdc, 2)   # PW_RENDERFULLCONTENT
            $g.ReleaseHdc($hdc); $g.Dispose()
            $bmp.Save($Screenshot, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
            $script:Form.Close()
        }
    })
    [void]$f.ShowDialog()
}

if ($Fix) {
    foreach ($f in Invoke-Diagnose $Tag) { Write-Log $f.Text $f.Level }
    $targets = if ($Tag) { @($Tag) } else { @(Get-Phantoms | ForEach-Object { $_.Culture } | Sort-Object -Unique) }
    if (-not $targets) { Write-Log (T 'f_autonone') 'ok' }
    foreach ($t in $targets) {
        Write-Log (T 'f_fixing' @($t)) 'info'
        Invoke-Fix $t $true
    }
    if ($targets) { foreach ($f in Invoke-Diagnose $Tag) { Write-Log $f.Text $f.Level } }
} elseif ($NoGui) {
    'PROFILE'; Get-ProfileLayouts | Format-Table -AutoSize | Out-String
    'LIVE'; Get-LiveLayouts | Format-Table -AutoSize | Out-String
    'PRELOAD'; Get-Preload
    'DIAG'; Invoke-Diagnose '' | ForEach-Object { '[{0}] {1}' -f $_.Level, $_.Text }
} else {
    Show-Gui
}
