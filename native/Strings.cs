using System.Globalization;

namespace KeyboardLayoutFixer;

internal static class Loc
{
    public static string Lang { get; set; } =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";

    public static string T(string key, params object[] args)
    {
        var s = Table[Lang][key];
        return args.Length > 0 ? string.Format(s, args) : s;
    }

    private static readonly Dictionary<string, Dictionary<string, string>> Table = new()
    {
        ["ru"] = new()
        {
            ["subtitle"] = "Лечит «прилипшую» раскладку-призрак (обычно en-US) в списке Win+Space",
            ["btn_scan"] = "Сканировать",
            ["btn_fix"] = "Исправить",
            ["btn_restore"] = "↩  Откатить",
            ["btn_lang"] = "EN",
            ["cap_profile"] = "ЯЗЫКИ ПРОФИЛЯ (Параметры → Время и язык)",
            ["cap_live"] = "ЖИВЫЕ РАСКЛАДКИ СЕССИИ (то, что переключает Win+Space)",
            ["cap_log"] = "ЖУРНАЛ",
            ["col_tag"] = "Тег", ["col_lang"] = "Язык", ["col_layout"] = "Раскладка", ["col_code"] = "Код",
            ["col_culture"] = "Культура", ["col_inpreload"] = "В Preload",
            ["yes"] = "да", ["no"] = "нет",
            ["status"] = "Языков в профиле: {0}   •   живых раскладок: {1}   •   Preload: {2}",
            ["unknown"] = "(неизвестная раскладка)",
            ["nolayout"] = "(без раскладки)",
            ["undetermined"] = "(не определена)",
            ["d_summary"] = "Профиль: {0} раскладок, живых в сессии: {1}, записей Preload: {2}",
            ["d_ghost"] = "ПРИЗРАК: раскладка {0} ({1}) активна в сессии, хотя в списке языков её нет. Это и есть «прилипшая» раскладка.",
            ["d_none"] = "Призрачных раскладок нет: всё, что загружено в сессии, есть в профиле.",
            ["d_desync_a"] = "К сведению: раскладка «{0}» ({1}) есть в профиле, но не записана в Preload. Это не призрак.",
            ["d_desync_b"] = "К сведению: Preload содержит {0} («{1}»), а в профиле такой раскладки нет. Это не призрак.",
            ["f_backup"] = "Бэкап сохранён: {0}",
            ["f_step1"] = "Шаг 1/2: временно добавляю {0}, чтобы Windows пересобрала список…",
            ["f_step2"] = "Шаг 2/2: удаляю {0} из профиля…",
            ["f_done"] = "Готово: {0} убрана, остальные раскладки сохранены.",
            ["f_retry"] = "Раскладка {0} всё ещё на месте, повторяю удаление (попытка {1})…",
            ["f_fail"] = "Не удалось убрать {0}. Перезайдите в систему и повторите.",
            ["f_apifail"] = "Windows отклонила изменение ({0}). Ничего не изменено.",
            ["f_fixing"] = "Исправляю {0} (цикл «добавить → удалить»)…",
            ["f_autonone"] = "Исправлять нечего: призраков нет.",
            ["st_none"] = "✔ Призраков нет",
            ["st_found"] = "✖ Призрак: {0}",
            ["s_scanning"] = "Сканирую раскладки…",
            ["s_problem"] = "Найдена проблема. Нажмите «Исправить».",
            ["s_clean"] = "Критичных проблем не найдено.",
            ["r_none"] = "Бэкапов пока нет.",
            ["r_restoring"] = "Восстанавливаю из {0}…",
            ["r_added"] = "Добавлена раскладка {0}.",
            ["r_removed"] = "Убрана раскладка {0}.",
            ["r_done"] = "Список раскладок восстановлен (порядок языков не меняется).",
            ["error"] = "Ошибка: {0}",
            ["started"] = "Keyboard Layout Fixer запущен. Бэкап создаётся перед каждым изменением.",
        },
        ["en"] = new()
        {
            ["subtitle"] = "Fixes the \"stuck\" phantom layout (usually en-US) in the Win+Space list",
            ["btn_scan"] = "Scan",
            ["btn_fix"] = "Fix",
            ["btn_restore"] = "↩  Restore",
            ["btn_lang"] = "RU",
            ["cap_profile"] = "PROFILE LANGUAGES (Settings → Time & language)",
            ["cap_live"] = "LIVE SESSION LAYOUTS (what Win+Space switches between)",
            ["cap_log"] = "LOG",
            ["col_tag"] = "Tag", ["col_lang"] = "Language", ["col_layout"] = "Layout", ["col_code"] = "Code",
            ["col_culture"] = "Culture", ["col_inpreload"] = "In Preload",
            ["yes"] = "yes", ["no"] = "no",
            ["status"] = "Profile languages: {0}   •   live layouts: {1}   •   Preload: {2}",
            ["unknown"] = "(unknown layout)",
            ["nolayout"] = "(no layout)",
            ["undetermined"] = "(undetermined)",
            ["d_summary"] = "Profile: {0} layouts, live in session: {1}, Preload entries: {2}",
            ["d_ghost"] = "PHANTOM: layout {0} ({1}) is active in the session although it is not in the language list. This is the \"stuck\" layout.",
            ["d_none"] = "No phantom layouts: everything loaded in the session is in the profile.",
            ["d_desync_a"] = "FYI: layout \"{0}\" ({1}) is in the profile but not listed in Preload. This is not the phantom.",
            ["d_desync_b"] = "FYI: Preload contains {0} (\"{1}\") but the profile has no such layout. This is not the phantom.",
            ["f_backup"] = "Backup saved: {0}",
            ["f_step1"] = "Step 1/2: temporarily adding {0} so Windows rebuilds the list…",
            ["f_step2"] = "Step 2/2: removing {0} from the profile…",
            ["f_done"] = "Done: {0} removed, other layouts preserved.",
            ["f_retry"] = "Layout {0} is still there, removing again (attempt {1})…",
            ["f_fail"] = "Could not remove {0}. Sign out and back in, then try again.",
            ["f_apifail"] = "Windows rejected the change ({0}). Nothing was changed.",
            ["f_fixing"] = "Fixing {0} (add → remove cycle)…",
            ["f_autonone"] = "Nothing to fix: no phantom layouts.",
            ["st_none"] = "✔ No phantom layouts",
            ["st_found"] = "✖ Phantom: {0}",
            ["s_scanning"] = "Scanning layouts…",
            ["s_problem"] = "Problem found. Press \"Fix\".",
            ["s_clean"] = "No critical problems found.",
            ["r_none"] = "No backups yet.",
            ["r_restoring"] = "Restoring from {0}…",
            ["r_added"] = "Added layout {0}.",
            ["r_removed"] = "Removed layout {0}.",
            ["r_done"] = "Layout list restored (language order is not changed).",
            ["error"] = "Error: {0}",
            ["started"] = "Keyboard Layout Fixer started. A backup is made before every change.",
        },
    };
}
