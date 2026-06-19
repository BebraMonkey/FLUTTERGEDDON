# FLUTTERGEDDON

FLUTTERGEDDON is a small Windows utility for quickly enabling/disabling existing Steam-folder DLL files used by Koalageddon-style setups.

The repository contains the source code so users can inspect what the app does before running a release build.

## What The App Does

- Lets the user choose a Steam folder.
- Closes Steam-related processes before switching files.
- Renames existing user files:
  - `version.dll` -> `version.dll.disabled`
  - `winhttp.dll` -> `winhttp.dll.disabled`
- Renames them back when enabling.
- Starts Steam again after the operation.
- Plays local bundled music and visual effects.
- Checks GitHub Releases for updates.

## What The App Does Not Do

- It does not download or install DLL files into Steam.
- It does not inject code into games.
- It does not steal tokens, passwords, cookies, browser data, or Steam login data.
- It does not require Python to run release builds.
- The macOS demo port does not change DLL files at all.

## Source Layout

```text
FLUTTERGEDDON/
  Program.cs                         Windows WinForms source code
  FLUTTERGEDDON.csproj               .NET project file
  Assets/                            Images, icon, music, changelog
  GITHUB_UPLOAD_INSTRUCTION_RU.md    Release upload instructions

FLUTTERGEDDON_MAC_APP/
  Sources/main.swift                 Native macOS demo source
  Assets/                            macOS demo assets
  build_mac_app.command              macOS build script

.github/workflows/
  build-fluttergeddon-mac.yml        GitHub Actions macOS .app builder
```

## Build Windows Version

Install .NET SDK 8, then run:

```powershell
dotnet publish .\FLUTTERGEDDON\FLUTTERGEDDON.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:PublishReadyToRun=false -p:EnableCompressionInSingleFile=true -o .\FLUTTERGEDDON\single
```

Output:

```text
FLUTTERGEDDON/single/FLUTTERGEDDON.exe
```

## Build macOS Demo

The macOS port must be built on macOS because it uses AppKit.

```bash
cd FLUTTERGEDDON_MAC_APP
chmod +x build_mac_app.command
./build_mac_app.command
```

Output:

```text
FLUTTERGEDDON_MAC_APP/build/FLUTTERGEDDON.app
```

You can also build the macOS app with GitHub Actions using:

```text
.github/workflows/build-fluttergeddon-mac.yml
```

## License

MIT License.
