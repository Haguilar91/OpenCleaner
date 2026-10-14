# Translations

Every app shows English or Spanish, following the system language. Texts are keyed by their English
wording, so the code stays readable and a missing translation falls back to English.

| App | Function | Spanish table |
|---|---|---|
| Android (phone and TV) | `tr("...")` | `android/app/src/main/java/com/opencleaner/android/I18n.kt` |
| macOS | `tr("...")` | `macos/Sources/OpenCleaner/I18n.swift` |
| Linux | `tr("...")` | the `ES` dict near the top of `linux-cachyos/opencleaner.py` |
| Windows WinForms and WinUI | `L.T("...")` | `windows/Strings.cs` (C# 5; WinForms `build.bat` compiles it, the WinUI project links it) |

Rules:

- Numbers and names go in as `{0}`, `{1}`... with the arguments: `tr("{0} selected · {1}", n, size)`.
  Never build a sentence from translated pieces; word order differs between languages.
- Avoid singular/plural forms: "Seleccionados: 3" instead of "3 seleccionados".
- When one English word needs two translations, prefix the key with a context: `"status|Clean"` is
  "Limpio" (the state), `"Clean"` is "Limpiar" (the action). The English text is what follows the `|`.
- Saved settings keep their English values (macOS `DeleteMode` raw values, Linux `delete_mode`, WinForms
  registry); only what is shown is translated.
- Check a table after changes: `python3 docs/i18n/check_keys.py <table> <sources...>` lists missing keys
  (and unused ones; keys translated through a variable, like sort names, show as unused).

Seeing Spanish without changing the system language:

| App | How |
|---|---|
| Android | `adb shell cmd locale set-app-locales com.opencleaner.android --locales es-419` (Android 13+) |
| macOS | `open -n OpenCleaner.app --args -AppleLanguages "(es)"` |
| Linux | `LANGUAGE=es python3 opencleaner.py`; Docker screenshots: `tests/docs_shots.sh arch-x86 es` |
| Windows | `OpenCleaner.exe --lang es`; screenshots: `take_screenshots.ps1 -Lang es` |

## Spanish glossary

Neutral Latin American Spanish, informal "tú" (as Android, macOS and the 1.3 video use). Keep these
words the same everywhere:

| English | Español |
|---|---|
| Clean (action / tab) | Limpiar |
| Clean (state, nothing to remove) | Limpio |
| Clean selected | Limpiar seleccionados |
| Disk usage | Uso del disco |
| Storage (Android tab) | Almacenamiento |
| Apps | Apps |
| Scan / Rescan | Analizar / Volver a analizar |
| Delete | Eliminar |
| Delete permanently | Eliminar para siempre |
| Move to Trash (macOS, Linux) | Mover a la Papelera / papelera |
| Recycle Bin (Windows) | Papelera de reciclaje ("Mover a la papelera" on buttons) |
| Uninstall | Desinstalar |
| Leftover files | Archivos sobrantes |
| Preview | Vista previa |
| Review files | Revisar archivos |
| Disk map | Mapa del disco |
| Biggest first / Oldest / Newest / File type | Más grandes / Más antiguos / Más recientes / Tipo |
| All / Top N | Todos / Primeros N |
| Last used | Último uso |
| free · used | libres · usado |
| Cache | Caché |
| Update to X | Actualizar a X |
| Check for updates | Buscar actualizaciones |
| Slogan: Free up space. See exactly what goes. | Libera espacio. Mira exactamente qué se borra. |
