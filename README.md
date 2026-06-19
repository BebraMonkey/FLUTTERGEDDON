# FLUTTERGEDDON

FLUTTERGEDDON is a simple program to enable/disable [koalageddon-fixed](https://github.com/DumbCodeGenerator/Koalageddon-fixed?tab=License-1-ov-file) (by Dumb Code Generator).
Games like CS2 and DOTA2 can't connect to servers with Koalageddon installed, and manually deleting DLL files takes a lot of time, so I made an application to automate this process.
In the future, I will add new useful features as needed, not only related to Koalageddon.

The repository contains the source code so users can inspect what the app does before running a release build.

This is a personal tool for me and friends. Source code may be incomplete or not always published. Use at your own risk.

<img width="200" height="164" alt="fluttershy" src="https://github.com/user-attachments/assets/6ffb7bfe-77de-4d6b-81c3-fe1236d1c2a9" />

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

## Source Layout

```text
FLUTTERGEDDON/
  Program.cs                         Windows WinForms source code
  FLUTTERGEDDON.csproj               .NET project file
  Assets/                            Images, icon, music, changelog
  GITHUB_UPLOAD_INSTRUCTION_RU.md    Release upload instructions
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

## License

MIT License.
