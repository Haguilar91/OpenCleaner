# Builds on GitHub Actions

`.github/workflows/build.yml` builds every app on GitHub's machines:

| Job | Machine | Output |
|---|---|---|
| linux | Ubuntu x64 and Ubuntu arm64, Debian 12 container (`build_release_docker.sh`) | `opencleaner-<v>-linux-x86_64`, `-linux-arm64` |
| linux-tests | the same | distro logic in Debian 12/13, Ubuntu, Fedora, Rocky, Arch x86_64 and Arch ARM containers, plus the real window under Xvfb on Debian 12 |
| macos | macOS (Apple silicon) | `OpenCleaner-<v>-macOS.zip` |
| windows | Windows x64, one run per architecture | `OpenCleaner-<v>-Setup-x64.exe`, `-arm64.exe`, `-x86.exe`, `OpenCleaner-<v>-Windows-portable.exe` |
| android | Ubuntu, JDK 17, Gradle 8.7 | `OpenCleaner-<v>-android.apk`, `OpenCleaner-<v>-androidtv.apk` |
| release | Ubuntu, only for a `v*` tag | a **draft** release with the 10 files and `SHA256SUMS.txt` |

Every push to `main` and every pull request builds and tests; the files are under the run's
**Artifacts** for 14 days. A tag makes a draft release; nothing is public until you publish it.

## Making a release

1. Set the same version in every app: `VERSION` in `linux-cachyos/opencleaner.py`, `CFBundleShortVersionString`
   (and `CFBundleVersion`) in `macos/Resources/Info.plist`, `AssemblyVersion`/`AssemblyFileVersion` in
   `windows/Program.cs`, `Version`/`FileVersion` in `windows-winui/OpenCleaner.csproj`, `versionName`
   (and a higher `versionCode`) in `android/app/build.gradle.kts`, and the default `AppVersion` in
   `installer/opencleaner.iss`. `python3 .github/scripts/versions.py` prints them; the workflow stops if
   they differ or do not match the tag.
2. Write the notes in `docs/releases/<version>.md` (used as the release text; without it GitHub
   generates notes from the commits).
3. Push, then tag: `git tag -a v1.4.0 -m "OpenCleaner 1.4" && git push origin v1.4.0`.
4. When the run is green, open the draft on the Releases page, check it, and publish.

Re-running a tag's workflow replaces the files of its draft.

## Android signing (one-time setup)

Android installs an update only when it is signed with the same key as the installed app. The 1.2 and
1.3 APKs were signed with the debug key of the Mac they were built on (`~/.android/debug.keystore`,
certificate SHA-256 `ccffce5a…8a48`), so GitHub must sign with that same key. Store it as a repository
secret, from that Mac:

```sh
base64 -i ~/.android/debug.keystore | gh secret set ANDROID_SIGNING_KEYSTORE --repo Haguilar91/OpenCleaner
```

The workflow checks every APK's certificate against that SHA-256. Without the secret, a tag build fails
(rather than ship APKs that cannot update), and other builds use a throwaway key with a warning.

Keep a backup of that keystore somewhere safe: if it is lost, users must uninstall the app to install
a newer one.

## Notes

- The Mac app is one universal binary (Apple silicon and Intel), ad-hoc signed and not notarized, as before.
- The Windows installers and exes are unsigned, as before (SmartScreen warns).
- Local builds still work exactly as before; the workflow only calls the same scripts.
