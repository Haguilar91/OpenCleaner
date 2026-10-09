# OpenCleaner for Android

Kotlin + Jetpack Compose. Same idea as the desktop apps, within what Android allows
(no root: Android stops apps from clearing other apps' caches or touching system files).

| Tab | What it does |
|---|---|
| Clean | OpenCleaner's own cache, thumbnail leftovers, old APKs in Downloads, WhatsApp sent-media copies, files over 100 MB. Every category always shows a row, with **Clean ✓** when empty. |
| Storage | Browse internal storage by size and delete items (permanent: Android has no trash for files). |
| Apps | Every app ranked by size with its cache size. Tap one to open its Android settings page, where you can clear its cache or uninstall. |

Nothing is deleted until you tick items and confirm. Deletes are limited to shared storage and
never touch `Android/data` or `Android/obb`.

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

Compiles. Not yet tested on a device.
