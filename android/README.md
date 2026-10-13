# OpenCleaner for Android

Kotlin + Jetpack Compose. Same idea as the desktop apps, within what Android allows
(no root: Android stops apps from clearing other apps' caches or touching system files).

| Tab | What it does |
|---|---|
| Clean | OpenCleaner's own cache, thumbnail leftovers, old APKs in Downloads, WhatsApp sent-media copies, files over 100 MB. Every category always shows a row, with **Clean ✓** when empty. The eye button on a row (**Review**) lists its files, and tapping one previews it before you tick the item. |
| Storage | Browse internal storage as a **list** or a **disk map** (colored treemap, tap a block to open a folder or preview a file). **Sort** by biggest, oldest, newest or file type, and show **Top** 5 / 10 / 50 / 100 or all. A percentage shows while sizes are measured. Tap a file to preview it (images, text, details), **play mp4 / mp3 / flac and other video and audio** in the built-in player, open it in another app, or delete it (permanent: Android has no trash for files). |
| Apps | Every app with its size, cache size and **when it was last used** (needs Usage access). **Sort** by biggest, least or most recently used, or name, and show **Top** 5 / 10 / 50 / 100. Tap an app to open its Android settings page, or the bin to uninstall it (permanent, removes its data; Android shows its own confirmation). |

Nothing is deleted until you tick items and confirm. Deletes are limited to shared storage and
never touch `Android/data` or `Android/obb`.

## TV app (Android TV, Google TV, Fire TV)

The same project builds a second APK for TVs (`tv` flavor, application id `com.opencleaner.tv`,
Android 9+ so older Fire TV on Fire OS 7 works too). It shows in the TV launcher with a banner, needs
no touchscreen, and is laid out for the remote: a navigation rail on the left, TV-safe margins, a
highlight around the focused item, and on the disk map OK opens a menu (Open / Select / Delete)
because remotes have no long-press.

On Android 9-10 the app asks for the classic storage permission. Many TVs have no settings page for
"All files access" or "Usage access"; then the app shows the command that grants it, for example:

    adb shell appops set com.opencleaner.tv MANAGE_EXTERNAL_STORAGE allow
    adb shell appops set com.opencleaner.tv GET_USAGE_STATS allow

Build: `gradle assembleTvDebug` (phone: `gradle assemblePhoneDebug`). Tested on the Google TV emulator
(Android 12); the Android 9 path needs a real older Fire TV (no ARM emulator image exists for it).

## Permissions

- **All files access**: needed to find and delete junk on shared storage. Requested on first launch.
- **Usage access** (optional): lets the Apps tab read each app's size. It cannot see what you do.

## Build

Needs Android Studio, or JDK 17 + the Android SDK (platform 34, build-tools 34.0.0) + Gradle 8.7:

    ./gradlew assembleDebug          # or: gradle assembleDebug
    # APK: app/build/outputs/apk/debug/app-debug.apk

Install it by copying the APK to the phone, or `adb install app-debug.apk`.
You may need to allow "Install unknown apps" for your file manager.

Requires Android 11 (API 30) or newer.

## Status

Builds and runs on a Pixel (version 1.8). Preview, player and map have been tried; sort / Top and the progress percentage are new and untried. Playback depends on the codecs the phone supports.

## Icons

UI icons are from [Lucide](https://lucide.dev) (ISC license), stored as vector drawables in `app/src/main/res/drawable/ic_*.xml`.
