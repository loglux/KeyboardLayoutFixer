# Keyboard Layout Fixer

A small PowerShell + WinForms tool that removes the **phantom "stuck" keyboard layout** (typically `en-US`) that shows up in the Windows 10/11 language bar and in the **Win+Space** switcher, even though it isn't in Settings and can't be removed there.

Documentation in other languages: [Русский](docs/README.ru.md)

![Win+Space switcher before the fix: a phantom English (United States)](docs/before-flyout.png)

## Why it happens

Windows keeps more than one list of keyboard layouts, and they can disagree:

| Where | What | Who reads it |
|---|---|---|
| User language list (`Get-WinUserLanguageList`) | Languages/layouts in your profile | Settings app |
| Live session layouts (`GetKeyboardLayoutList`) | What is loaded right now | Win+Space, language bar |
| `HKCU\Keyboard Layout\Preload` | Layouts loaded at sign-in | Logon / input subsystem |

A layout that is loaded in the session but missing from the profile is a *phantom*: you can see it in Win+Space, but there is nothing in Settings to remove.

## What the tool does

- **Scan** — shows all three lists, flags the phantom in red (any language, not just en-US) and shows it in the header, or a green *No phantom layouts* when the session is clean.
- **Fix** — backs up your language list, temporarily adds the detected phantom language (usually `en-US`, but any language works) so Windows turns the phantom into a real entry, removes it, verifies the result (retrying if Windows re-adds an empty entry).
- **Restore** — puts the language list back from the last backup (`backups\*.json`).
- **Language button** — switches the interface language when more than one language is available (see [Translations](#translations)); the default follows your Windows UI language.

It never removes your only language, and it only touches the **current user's** language list. It is the scripted version of this well-known workaround:

```powershell
$l = Get-WinUserLanguageList
$l.Add("en-US");  Set-WinUserLanguageList $l -Force      # make the phantom real
$l.Remove(($l | Where-Object LanguageTag -like 'en-US')) # ...then remove it
Set-WinUserLanguageList $l -Force
```

**Before** — the scan finds en-US loaded in the session but missing from the profile:

![Scan with a phantom en-US layout](docs/app-before-en.png)

**After** — a clean session:

![After the fix](docs/app-after-en.png)

## Download

Get a ready-made program from the [Releases page](https://github.com/loglux/KeyboardLayoutFixer/releases/latest) (Windows 10/11, 64-bit, no admin rights needed). Same window and same fix as the script, but no PowerShell involved:

| File | Size | Needs |
|---|---|---|
| `KeyboardLayoutFixer-full.zip` | ~58 MB | nothing, runs anywhere (EXE + `lang` folder) |
| `KeyboardLayoutFixer-light.zip` | ~0.1 MB | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows offers to install it on first launch) (EXE + `lang` folder) |
| `KeyboardLayoutFixer-languages.zip` | few KB | only the `lang` folder: `ru.json` and the blank `template.json` for your own language |

The EXE itself is English only; every other language, Russian included, is a file in the `lang` folder next to it (see [Translations](#translations)). Keep the folder next to the EXE to get the Russian interface.

The EXE is not code-signed yet, so Windows SmartScreen may warn about an unknown publisher: click **More info → Run anyway**. The source is in [`native/`](native/), build it with `dotnet build -c Release`. Backups are stored in `%LOCALAPPDATA%\KeyboardLayoutFixer\backups`. It also has a console mode: `--scan`, `--fix`, `--restore`, `--lang xx` (language code).

## Usage (PowerShell script)

1. Download `KeyboardFix.ps1` and `KeyboardFix.bat` (and the `lang` folder for languages other than English) into the same folder.
2. Double-click `KeyboardFix.bat` (or run `powershell -STA -File KeyboardFix.ps1`).
3. Press **Scan**; if you see a red row, press **Fix**.
4. If the layout is still in the Win+Space list, sign out and back in — layouts already loaded into a session can survive until the next logon.

Command-line options:

```powershell
.\KeyboardFix.ps1 -Lang en            # force UI language (any code from the lang folder)
.\KeyboardFix.ps1 -NoGui              # read-only diagnostics in the console
.\KeyboardFix.ps1 -Fix               # console mode: scan, back up, fix detected phantoms, scan again
.\KeyboardFix.ps1 -Fix -Tag de-DE     # ...or force a specific language
.\KeyboardFix.ps1 -Screenshot out.png # scan and save a PNG of the window (used for docs)
```

Requirements: Windows 10/11, Windows PowerShell 5.1 (the script relaunches itself from PowerShell 7, because there the language-list objects arrive deserialized, without `Add`/`Remove`). No admin rights needed.

> The scan may also print an *FYI* when a profile layout isn't listed in `Preload` (for example Russian *Typewriter* `00010419` vs `00000419`). That is not the phantom; the tool only reports it and never touches `Preload`.

## Translations

English is built in; Russian ships as `lang/ru.json`. To add another language, copy `lang/template.json` to `lang/xx.json` (`xx` = language code, e.g. `de.json`), set `"_name"` to the language's own name and fill in the values. Empty values fall back to English, so a partial translation works. Put the `lang` folder next to `KeyboardFix.ps1` or next to the EXE, or send the file as a pull request. `--lang xx` (EXE) / `-Lang xx` (script) selects it; by default the Windows UI language is used.

This README stays in English. A translated README goes into its own file, `docs/README.xx.md` (for example `docs/README.ru.md`), with a link to it near the top of this page, so the main README does not grow with every language.

## License

[MIT](LICENSE)
