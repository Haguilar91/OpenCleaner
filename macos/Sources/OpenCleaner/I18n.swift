import Foundation

/// Translations keyed by the English text, so the code stays readable and a missing entry falls back to English.
/// The app follows the first language in System Settings > General > Language & Region.
/// "{0}", "{1}"... are filled from the arguments. A key may start with "context|" when the same English word
/// needs different translations ("status|Clean" = the state, not the verb).
let spanish = Locale.preferredLanguages.first?.hasPrefix("es") ?? false

func tr(_ en: String, _ args: Any...) -> String {
    var s = (spanish ? es[en] : nil) ?? (en.split(separator: "|", maxSplits: 1).last.map(String.init) ?? en)
    for (i, a) in args.enumerated() { s = s.replacingOccurrences(of: "{\(i)}", with: "\(a)") }
    return s
}

private let es: [String: String] = [
    // header
    "Rescan": "Volver a analizar",
    "Clean": "Limpiar",
    "status|Clean": "Limpio",
    "Disk usage": "Uso del disco",
    "Apps": "Apps",
    "{0} free · {1}% used": "{0} libres · {1}% usado",
    // updates
    "Updating…": "Actualizando…",
    "Update to {0}": "Actualizar a {0}",
    "Check for updates (OpenCleaner {0})": "Buscar actualizaciones (OpenCleaner {0})",
    "Update to OpenCleaner {0}?": "¿Actualizar a OpenCleaner {0}?",
    "Update and restart": "Actualizar y reiniciar",
    "Release page": "Página de la versión",
    "Later": "Más tarde",
    "You have {0}. The update is downloaded from GitHub, checked against the release checksums, and installed in place of this app.":
        "Tienes la {0}. La actualización se descarga de GitHub, se comprueba con las sumas de verificación de la versión y se instala en lugar de esta app.",
    "OpenCleaner {0} is up to date.": "OpenCleaner {0} está actualizado.",
    "Could not check for updates: {0}": "No se pudo buscar actualizaciones: {0}",
    "Update failed: {0}": "La actualización falló: {0}",
    "You can download it from {0}": "Puedes descargarla desde {0}",
    "No release found on GitHub.": "No se encontró ninguna versión en GitHub.",
    "This release has no macOS download.": "Esta versión no tiene descarga para macOS.",
    "The download did not match the release checksum, so it was not installed.":
        "La descarga no coincide con la suma de verificación de la versión, así que no se instaló.",
    "Could not unpack the update.": "No se pudo descomprimir la actualización.",
    "OpenCleaner cannot replace itself in {0}. Download it from the release page instead.":
        "OpenCleaner no puede reemplazarse a sí mismo en {0}. Descárgalo desde la página de la versión.",
    // clean
    "Clean selected": "Limpiar seleccionados",
    "Delete selected items?": "¿Eliminar los elementos seleccionados?",
    "Cancel": "Cancelar",
    "This cannot be undone.": "Esto no se puede deshacer.",
    "OK": "Aceptar",
    "{0} selected · {1}": "Seleccionados: {0} · {1}",
    "+ more": "+ más",
    "Scan failed": "Falló el análisis",
    "Review files": "Revisar archivos",
    "Scanning…": "Analizando…",
    "Cleaning…": "Limpiando…",
    "Cleaning finished": "Limpieza terminada",
    "freed {0}": "se liberaron {0}",
    "Some items failed:": "Algunos elementos fallaron:",
    "size unknown": "tamaño desconocido",
    "Refusing to delete outside home: {0}": "No se permite eliminar fuera de la carpeta de inicio: {0}",
    "Nothing to clean": "Nada que limpiar",
    "Download cache and old versions": "Caché de descargas y versiones viejas",
    "Runs brew cleanup -s": "Ejecuta brew cleanup -s",
    "Unneeded dependencies": "Dependencias innecesarias",
    "Unneeded dependencies ({0})": "Dependencias innecesarias ({0})",
    "Removes:": "Elimina:",
    "System junk": "Basura del sistema",
    "User logs and crash reports": "Registros e informes de fallos",
    "Large app caches": "Cachés grandes de apps",
    "App cache, rebuilt automatically": "Caché de la app, se vuelve a crear sola",
    "Trash": "Papelera",
    "Permanently deletes everything in your Trash": "Elimina para siempre todo lo que hay en la Papelera",
    "Developer": "Desarrollo",
    "Xcode build data": "Datos de compilación de Xcode",
    "Build products, rebuilt on next build": "Productos de compilación, se vuelven a crear en la próxima compilación",
    "Debug symbols for devices you plugged in": "Símbolos de depuración de los dispositivos que conectaste",
    "Archived app builds. Keep any you still need": "Compilaciones archivadas. Conserva las que todavía necesites",
    "Unavailable simulators": "Simuladores no disponibles",
    "Unavailable simulators ({0})": "Simuladores no disponibles ({0})",
    "Simulators for removed iOS runtimes": "Simuladores de versiones de iOS ya eliminadas",
    "Device backups": "Respaldos de dispositivos",
    "iPhone and iPad backups": "Respaldos de iPhone y iPad",
    "unknown date": "fecha desconocida",
    "Last backup {0}. Cannot be recovered once deleted.": "Último respaldo: {0}. No se puede recuperar una vez eliminado.",
    "Steam leftovers": "Restos de Steam",
    "App {0} (not in Steam store)": "App {0} (no está en la tienda de Steam)",
    "Some Steam library drives are not connected, so the game may live there.":
        "Algunas unidades de bibliotecas de Steam no están conectadas, así que el juego podría estar ahí.",
    "Shader cache: {0}": "Caché de sombreadores: {0}",
    "Game not installed (app {0}). Steam rebuilds this if you reinstall.":
        "El juego no está instalado (app {0}). Steam la vuelve a crear si lo reinstalas.",
    "Steam browser cache": "Caché del navegador de Steam",
    "Steam is running. Close it first, or in-use files are skipped.":
        "Steam está abierto. Ciérralo primero, o se omitirán los archivos en uso.",
    "Cache of the Steam store and community pages": "Caché de las páginas de la tienda y la comunidad de Steam",
    "Unfinished game downloads": "Descargas de juegos sin terminar",
    "Partial downloads. Don't clean while a download you want to resume is paused.":
        "Descargas parciales. No limpies mientras esté en pausa una descarga que quieras continuar.",
    // disk usage
    "Move to Trash": "Mover a la Papelera",
    "Delete permanently": "Eliminar para siempre",
    "Biggest first": "Más grandes",
    "Oldest first": "Más antiguos",
    "Newest first": "Más recientes",
    "File type": "Tipo",
    "Measuring {0}% ({1} of {2} items)": "Midiendo {0}% ({1} de {2} elementos)",
    "Total {0}": "Total {0}",
    "Could not delete {0}": "No se pudo eliminar {0}",
    "Home": "Inicio",
    "System (/)": "Sistema (/)",
    "Sort": "Orden",
    "Sort by size, age or type": "Ordenar por tamaño, antigüedad o tipo",
    "Sort by size, last use or name": "Ordenar por tamaño, último uso o nombre",
    "Show": "Mostrar",
    "All": "Todos",
    "Top {0}": "Primeros {0}",
    "Show only the first items of the sort": "Mostrar solo los primeros elementos del orden",
    "List or map": "Lista o mapa",
    "What Delete does in Disk usage (uninstalling is always permanent)":
        "Qué hace Eliminar en Uso del disco (desinstalar siempre es permanente)",
    "Clear": "Quitar selección",
    "Move selected to Trash": "Mover seleccionados a la Papelera",
    "Delete selected permanently": "Eliminar seleccionados para siempre",
    "{0} items": "{0} elementos",
    "Move {0} to the Trash?": "¿Mover {0} a la Papelera?",
    "Delete {0} permanently?": "¿Eliminar {0} para siempre?",
    "…and {0} more": "…y {0} más",
    "You can put them back from the Trash.": "Puedes recuperarlos desde la Papelera.",
    "This cannot be undone: nothing goes to the Trash.": "Esto no se puede deshacer: nada va a la Papelera.",
    // map
    "Folders": "Carpetas",
    "Images": "Imágenes",
    "Video": "Video",
    "Audio": "Audio",
    "Documents": "Documentos",
    "Archives": "Comprimidos",
    "Other": "Otros",
    "Measuring…": "Midiendo…",
    "Nothing to show": "No hay nada que mostrar",
    "Right-click to select or delete.": "Clic derecho para seleccionar o eliminar.",
    "Open": "Abrir",
    "Preview": "Vista previa",
    "Deselect": "Quitar selección",
    "Select": "Seleccionar",
    // apps
    "Least recently used": "Menos usadas",
    "Recently used": "Recientes",
    "Name": "Nombre",
    "Measuring {0}% ({1} of {2} apps)": "Midiendo {0}% ({1} de {2} apps)",
    "{0} apps · {1}": "{0} apps · {1}",
    "{0} is running. Quit it first, then try again.": "{0} está abierta. Ciérrala primero y vuelve a intentarlo.",
    "outside the allowed folders": "fuera de las carpetas permitidas",
    "{0} was deleted permanently ({1} freed).": "{0} se eliminó para siempre (se liberaron {1}).",
    "Some items could not be deleted:": "Algunos elementos no se pudieron eliminar:",
    "cancelled at the administrator prompt": "se canceló en la ventana de administrador",
    "Last used: unknown": "Último uso: desconocido",
    "Last used {0}": "Último uso: {0}",
    "Uninstall…": "Desinstalar…",
    "Uninstall {0}?": "¿Desinstalar {0}?",
    "Ticked items are deleted permanently. They do not go to the Trash, so this cannot be undone.":
        "Los elementos marcados se eliminan para siempre. No van a la Papelera, así que esto no se puede deshacer.",
    "Items that may hold your data (Application Support, containers) start unticked. Apps installed for all users (like Microsoft Office) ask for your administrator password.":
        "Los elementos que pueden tener tus datos (Application Support, contenedores) empiezan sin marcar. Las apps instaladas para todos los usuarios (como Microsoft Office) piden tu contraseña de administrador.",
    "Looking for leftover files…": "Buscando archivos sobrantes…",
    // preview
    "File": "Archivo",
    "No preview for this file type. Use Open to view it in another app.":
        "No hay vista previa para este tipo de archivo. Usa Abrir para verlo en otra app.",
    "Reveal": "Mostrar en Finder",
    "Close": "Cerrar",
    "Click a file to preview it.": "Haz clic en un archivo para verlo.",
    "folder": "carpeta",
]
