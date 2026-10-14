package com.opencleaner.android

import java.util.Locale

/**
 * Translations keyed by the English text, so the code stays readable and a missing entry falls back to English.
 * The app follows the system language. "{0}", "{1}"... are filled from the arguments. A key may start with
 * "context|" when the same English word needs different translations ("status|Clean" = the state, not the verb).
 */
fun tr(en: String, vararg args: Any?): String {
    var s = (if (Locale.getDefault().language == "es") ES[en] else null) ?: en.substringAfter('|')
    args.forEachIndexed { i, a -> s = s.replace("{$i}", a.toString()) }
    return s
}

private val ES = mapOf(
    // navigation and header
    "Clean" to "Limpiar",
    "status|Clean" to "Limpio",
    "Storage" to "Almacenamiento",
    "Apps" to "Apps",
    "{0} free · {1}% used" to "{0} libres · {1}% usado",
    // updates
    "OpenCleaner {0} is up to date." to "OpenCleaner {0} está actualizado.",
    "Could not check for updates: {0}" to "No se pudo buscar actualizaciones: {0}",
    "Updating…" to "Actualizando…",
    "Update to {0}" to "Actualizar a {0}",
    "Check for updates ({0})" to "Buscar actualizaciones ({0})",
    "Update to OpenCleaner {0}?" to "¿Actualizar a OpenCleaner {0}?",
    "You have {0}. The update is downloaded from GitHub and checked against the release checksums; Android then asks you to confirm the install." to
        "Tienes la {0}. La actualización se descarga de GitHub y se comprueba con las sumas de verificación de la versión; luego Android te pide confirmar la instalación.",
    "Update failed: {0}" to "La actualización falló: {0}",
    "Update" to "Actualizar",
    "Later" to "Más tarde",
    "OK" to "Aceptar",
    "This release has no Android download." to "Esta versión no tiene descarga para Android.",
    "This release has no checksums, so it was not installed." to "Esta versión no tiene sumas de verificación, así que no se instaló.",
    "The download did not match the release checksum, so it was not installed." to
        "La descarga no coincide con la suma de verificación de la versión, así que no se instaló.",
    // permissions
    "OpenCleaner needs file access" to "OpenCleaner necesita acceso a los archivos",
    "To find and delete junk files, Android requires you to allow \"All files access\". The app only deletes items you tick and confirm, and never touches other apps' private data." to
        "Para encontrar y eliminar archivos basura, Android pide que permitas el \"Acceso a todos los archivos\". La app solo elimina lo que marques y confirmes, y nunca toca los datos privados de otras apps.",
    "Allow access" to "Permitir acceso",
    "Open settings" to "Abrir ajustes",
    "This device has no \"All files access\" settings page (common on TVs). Grant it once from a computer with adb:" to
        "Este dispositivo no tiene la página de ajustes \"Acceso a todos los archivos\" (es común en los televisores). Concédelo una vez desde una computadora con adb:",
    "then reopen OpenCleaner." to "y vuelve a abrir OpenCleaner.",
    "Allow usage access" to "Permitir acceso al uso",
    "Android only shares each app's size if you allow \"Usage access\". OpenCleaner only reads sizes. It cannot see what you do in apps." to
        "Android solo comparte el tamaño de cada app si permites el \"Acceso al uso\". OpenCleaner solo lee tamaños. No puede ver lo que haces en las apps.",
    "This device has no \"Usage access\" settings page (common on TVs). Grant it once from a computer with adb:" to
        "Este dispositivo no tiene la página de ajustes \"Acceso al uso\" (es común en los televisores). Concédelo una vez desde una computadora con adb:",
    "then come back to this tab." to "y vuelve a esta pestaña.",
    // clean
    "size unknown" to "tamaño desconocido",
    "This app" to "Esta app",
    "OpenCleaner's own cache" to "Caché de OpenCleaner",
    "Temporary files this app created" to "Archivos temporales que creó esta app",
    "Junk files" to "Archivos basura",
    "Thumbnail leftovers" to "Miniaturas sobrantes",
    "Old gallery thumbnails in DCIM/.thumbnails. Rebuilt on demand." to
        "Miniaturas viejas de la galería en DCIM/.thumbnails. Se vuelven a crear cuando hacen falta.",
    "Old installer files (.apk) in Downloads" to "Instaladores viejos (.apk) en Descargas",
    "Old installer files ({0})" to "Instaladores viejos ({0})",
    "APKs left in Downloads after installing. Keep any you still need." to
        "APK que quedaron en Descargas después de instalar. Conserva los que todavía necesites.",
    "Messaging apps" to "Apps de mensajería",
    "WhatsApp sent-media copies" to "Copias enviadas por WhatsApp",
    "Copies of media you sent. If you deleted the original from your gallery, this may be the only copy." to
        "Copias de los archivos que enviaste. Si borraste el original de tu galería, esta puede ser la única copia.",
    "Large files" to "Archivos grandes",
    "Files over 100 MB" to "Archivos de más de 100 MB",
    "Nothing to clean" to "Nada que limpiar",
    "Scan failed: {0}" to "Falló el análisis: {0}",
    "Refusing to delete {0}" to "No se permite eliminar {0}",
    "Scanning…" to "Analizando…",
    "Cleaning…" to "Limpiando…",
    "Cleaning finished" to "Limpieza terminada",
    "freed {0}" to "se liberaron {0}",
    "{0} item(s) could not be deleted." to "No se pudieron eliminar {0} elemento(s).",
    "Rescan" to "Volver a analizar",
    "{0} selected · {1}" to "Seleccionados: {0} · {1}",
    "Delete selected items?" to "¿Eliminar los elementos seleccionados?",
    "This cannot be undone." to "Esto no se puede deshacer.",
    "Cancel" to "Cancelar",
    "Review files" to "Revisar archivos",
    // storage
    "Biggest first" to "Más grandes",
    "Oldest first" to "Más antiguos",
    "Newest first" to "Más recientes",
    "File type" to "Tipo",
    "Up" to "Subir",
    "Internal storage" to "Almacenamiento interno",
    "Measuring {0}% ({1} of {2} items)" to "Midiendo {0}% ({1} de {2} elementos)",
    "Total {0}" to "Total {0}",
    "Sort: {0}" to "Orden: {0}",
    "Show: {0}" to "Mostrar: {0}",
    "All" to "Todos",
    "Top {0}" to "Primeros {0}",
    "List view" to "Vista de lista",
    "Map view" to "Vista de mapa",
    "Delete" to "Eliminar",
    "Clear" to "Quitar selección",
    "Delete selected" to "Eliminar seleccionados",
    "Delete \"{0}\" permanently?" to "¿Eliminar \"{0}\" para siempre?",
    "Delete {0} items permanently?" to "¿Eliminar {0} elementos para siempre?",
    "Android has no trash for files, so this cannot be undone." to "Android no tiene papelera para archivos, así que esto no se puede deshacer.",
    "…and {0} more" to "…y {0} más",
    "Could not delete {0}" to "No se pudo eliminar {0}",
    "Open" to "Abrir",
    "Preview" to "Vista previa",
    "Deselect" to "Quitar selección",
    "Select" to "Seleccionar",
    "Close" to "Cerrar",
    "Nothing to show" to "No hay nada que mostrar",
    "Selected" to "Seleccionado",
    "Press OK on a block to open, select or delete it" to "Pulsa OK en un bloque para abrirlo, seleccionarlo o eliminarlo",
    "Long-press a block to select it" to "Mantén presionado un bloque para seleccionarlo",
    // file kinds (map legend)
    "Folders" to "Carpetas",
    "Images" to "Imágenes",
    "Video" to "Video",
    "Audio" to "Audio",
    "Documents" to "Documentos",
    "Archives" to "Comprimidos",
    "Other" to "Otros",
    // apps
    "Least recently used" to "Menos usadas",
    "Recently used" to "Recientes",
    "Name" to "Nombre",
    "last used: unknown" to "último uso: desconocido",
    "last used {0}" to "último uso: {0}",
    "Clearing caches {0} of {1}: {2}" to "Borrando cachés {0} de {1}: {2}",
    "Tap Storage & cache, then Clear cache, then go back." to "Toca Almacenamiento y caché, luego Borrar caché y vuelve atrás.",
    "Skip" to "Omitir",
    "Stop" to "Detener",
    "Free up space" to "Liberar espacio",
    "Clear caches ({0})" to "Borrar cachés ({0})",
    "Press OK on an app to open its settings, or the bin to uninstall it. Uninstalling is permanent and removes its data; Android asks you to confirm." to
        "Pulsa OK en una app para abrir sus ajustes, o en la papelera para desinstalarla. Desinstalar es permanente y borra sus datos; Android te pide confirmar.",
    "Tap an app to open its settings, or the bin to uninstall it. Uninstalling is permanent and removes its data; Android asks you to confirm." to
        "Toca una app para abrir sus ajustes, o la papelera para desinstalarla. Desinstalar es permanente y borra sus datos; Android te pide confirmar.",
    "{0} apps · {1}" to "{0} apps · {1}",
    "data {0} · cache {1}" to "datos {0} · caché {1}",
    "Uninstall {0}" to "Desinstalar {0}",
    // preview
    "Pause" to "Pausa",
    "Play" to "Reproducir",
    "No preview for this file type. Use Open with... to view it in another app." to
        "No hay vista previa para este tipo de archivo. Usa Abrir con... para verlo en otra app.",
    "Open with" to "Abrir con",
    "Open with..." to "Abrir con...",
    "Tap a file to preview it." to "Toca un archivo para verlo.",
    "folder" to "carpeta",
)
