# Keyboard Layout Fixer

A small PowerShell + WinForms tool that removes the **phantom "stuck" keyboard layout** (typically `en-US`) that shows up in the Windows 10/11 language bar and in the **Win+Space** switcher, even though it isn't in Settings and can't be removed there.

*Русская версия — ниже.*

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

- **Scan** — shows all three lists, flags the phantom in red (any language, not just en-US) and fills in its language tag.
- **Fix** — backs up your language list, temporarily adds the detected phantom language (usually `en-US`, but any language works) so Windows turns the phantom into a real entry, removes it, verifies the result (retrying if Windows re-adds an empty entry).
- **Restore** — puts the language list back from the last backup (`backups\*.json`).
- **RU / EN** — switches the interface language (default follows your Windows UI language).

It never removes your only language, and it only touches the **current user's** language list. It is the scripted version of this well-known workaround:

```powershell
$l = Get-WinUserLanguageList
$l.Add("en-US");  Set-WinUserLanguageList $l -Force      # make the phantom real
$l.Remove(($l | Where-Object LanguageTag -like 'en-US')) # ...then remove it
Set-WinUserLanguageList $l -Force
```

![After the fix](docs/app-after-en.png)

## Usage

1. Download `KeyboardFix.ps1` and `KeyboardFix.bat` into the same folder.
2. Double-click `KeyboardFix.bat` (or run `powershell -STA -File KeyboardFix.ps1`).
3. Press **Scan**; if you see a red row, press **Fix**.
4. If the layout is still in the Win+Space list, sign out and back in — layouts already loaded into a session can survive until the next logon.

Command-line options:

```powershell
.\KeyboardFix.ps1 -Lang en            # force UI language (ru | en)
.\KeyboardFix.ps1 -NoGui              # read-only diagnostics in the console
.\KeyboardFix.ps1 -Fix               # console mode: scan, back up, fix detected phantoms, scan again
.\KeyboardFix.ps1 -Fix -Tag de-DE     # ...or force a specific language
.\KeyboardFix.ps1 -Screenshot out.png # scan and save a PNG of the window (used for docs)
```

Requirements: Windows 10/11, Windows PowerShell 5.1 (the script relaunches itself from PowerShell 7, because there the language-list objects arrive deserialized, without `Add`/`Remove`). No admin rights needed.

> The scan may also print an *FYI* when a profile layout isn't listed in `Preload` (for example Russian *Typewriter* `00010419` vs `00000419`). That is not the phantom; the tool only reports it and never touches `Preload`.

## Русская версия

Небольшая утилита на PowerShell + WinForms, которая убирает «прилипшую» раскладку-призрак (обычно `en-US`): она есть в Win+Space и в языковой панели, но её нет в «Параметрах», и удалить её оттуда нельзя.

Утилита показывает три списка (профиль, живая сессия, `Preload`), подсвечивает призрак красным и лечит его приёмом «добавить язык → применить → удалить → применить». Перед правкой делается резервная копия, кнопка **Откатить** возвращает её. Единственный язык не удаляется. Интерфейс переключается RU/EN.

Запуск: положите `KeyboardFix.ps1` и `KeyboardFix.bat` в одну папку и запустите `KeyboardFix.bat` → **Сканировать** → **Исправить**. Если раскладка осталась в списке, перезайдите в систему.

![Русский интерфейс](docs/app-after-ru.png)

## License

[MIT](LICENSE)
